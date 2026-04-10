using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace EFCore.Profiler;

internal static class SqlServerCommandParser
{
    public static SqlCommandMetadata Parse(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return SqlCommandMetadata.Empty;

        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            var fragment = parser.Parse(reader, out var errors);
            if (fragment is null || errors.Count > 0)
                return SqlCommandMetadata.Empty;

            var statement = GetFirstStatement(fragment);
            if (statement is null)
                return SqlCommandMetadata.Empty;

            var commandName = statement switch
            {
                SelectStatement => "SELECT",
                InsertStatement => "INSERT",
                UpdateStatement => "UPDATE",
                DeleteStatement => "DELETE",
                MergeStatement => "MERGE",
                ExecuteStatement => "EXEC",
                _ => statement.GetType().Name.Replace("Statement", string.Empty, StringComparison.Ordinal).ToUpperInvariant()
            };

            var tableCollector = new TableReferenceVisitor();
            statement.Accept(tableCollector);

            var shapeVisitor = new SelectShapeVisitor();
            statement.Accept(shapeVisitor);

            var warningVisitor = new SafetyAndSargabilityVisitor();
            statement.Accept(warningVisitor);

            var workloadVisitor = new WorkloadHeuristicsVisitor();
            statement.Accept(workloadVisitor);

            return new SqlCommandMetadata(
                commandName,
                tableCollector.TableNames,
                shapeVisitor.HasAnySelect,
                shapeVisitor.HasTop,
                shapeVisitor.HasOffsetFetch,
                shapeVisitor.HasWhereClause,
                shapeVisitor.HasAggregateOnlyProjection,
                shapeVisitor.JoinCount,
                warningVisitor.MissingWhereOnDml,
                warningVisitor.HasSelectStarUsage,
                warningVisitor.HasCartesianJoinRisk,
                warningVisitor.HasLeadingWildcardLike,
                warningVisitor.HasNonSargablePredicate,
                warningVisitor.HasImplicitConversionRisk,
                workloadVisitor.HasOrderByClause,
                workloadVisitor.MaxInListItemCount,
                workloadVisitor.MaxOrPredicateFanout);
        }
        catch
        {
            return SqlCommandMetadata.Empty;
        }
    }

    private static TSqlStatement? GetFirstStatement(TSqlFragment fragment)
    {
        return fragment switch
        {
            TSqlScript script when script.Batches.Count > 0 && script.Batches[0].Statements.Count > 0 =>
                script.Batches[0].Statements[0],
            TSqlBatch batch when batch.Statements.Count > 0 => batch.Statements[0],
            TSqlStatement statement => statement,
            _ => null
        };
    }

    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _tableNames = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> TableNames => _tableNames.ToArray();

        public override void ExplicitVisit(NamedTableReference node)
        {
            var baseIdentifier = node.SchemaObject.BaseIdentifier?.Value;
            if (string.IsNullOrWhiteSpace(baseIdentifier))
                return;

            var schemaIdentifier = node.SchemaObject.SchemaIdentifier?.Value;
            var tableName = string.IsNullOrWhiteSpace(schemaIdentifier)
                ? baseIdentifier
                : $"{schemaIdentifier}.{baseIdentifier}";

            _tableNames.Add(tableName);
        }
    }
}

internal sealed record SqlCommandMetadata(
    string CommandName,
    IReadOnlyList<string> TableNames,
    bool IsSelect,
    bool HasTopClause,
    bool HasOffsetFetchClause,
    bool HasWhereClause,
    bool IsAggregateOnlyProjection,
    int JoinCount,
    bool MissingWhereOnDml,
    bool HasSelectStarUsage,
    bool HasCartesianJoinRisk,
    bool HasLeadingWildcardLike,
    bool HasNonSargablePredicate,
    bool HasImplicitConversionRisk,
    bool HasOrderByClause,
    int MaxInListItemCount,
    int MaxOrPredicateFanout)
{
    public bool HasRowLimitClause => HasTopClause || HasOffsetFetchClause;

    public static readonly SqlCommandMetadata Empty = new(string.Empty, [], false, false, false, false, false, 0, false, false, false, false, false, false, false, 0, 0);
}

internal sealed class SelectShapeVisitor : TSqlFragmentVisitor
{
    private static readonly HashSet<string> AggregateFunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT",
        "SUM",
        "AVG",
        "MIN",
        "MAX",
        "APPROX_COUNT_DISTINCT",
        "STRING_AGG"
    };

    public bool HasAnySelect { get; private set; }
    public bool HasTop { get; private set; }
    public bool HasOffsetFetch { get; private set; }
    public bool HasWhereClause { get; private set; }
    public bool HasAggregateOnlyProjection { get; private set; }
    public int JoinCount { get; private set; }

    public override void ExplicitVisit(QuerySpecification node)
    {
        HasAnySelect = true;
        HasTop |= node.TopRowFilter is not null;
        HasWhereClause |= node.WhereClause is not null;
        HasOffsetFetch |= node.OffsetClause is not null;
        HasAggregateOnlyProjection |= IsAggregateOnlyProjection(node);
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(QualifiedJoin node)
    {
        JoinCount++;
        base.ExplicitVisit(node);
    }

    private static bool IsAggregateOnlyProjection(QuerySpecification query)
    {
        var sawAggregate = false;
        foreach (var element in query.SelectElements)
        {
            if (element is not SelectScalarExpression scalar)
                return false;

            var functionCall = scalar.Expression switch
            {
                FunctionCall function => function,
                ParenthesisExpression { Expression: FunctionCall function } => function,
                _ => null
            };

            var functionName = functionCall?.FunctionName?.Value;
            if (string.IsNullOrWhiteSpace(functionName) || !AggregateFunctionNames.Contains(functionName))
                return false;

            sawAggregate = true;
        }

        return sawAggregate;
    }
}

internal sealed class SafetyAndSargabilityVisitor : TSqlFragmentVisitor
{
    private bool _insideWhereClause;

    public bool MissingWhereOnDml { get; private set; }
    public bool HasSelectStarUsage { get; private set; }
    public bool HasCartesianJoinRisk { get; private set; }
    public bool HasLeadingWildcardLike { get; private set; }
    public bool HasNonSargablePredicate { get; private set; }
    public bool HasImplicitConversionRisk { get; private set; }

    public override void ExplicitVisit(UpdateSpecification node)
    {
        if (node.WhereClause is null)
            MissingWhereOnDml = true;

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(DeleteSpecification node)
    {
        if (node.WhereClause is null)
            MissingWhereOnDml = true;

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(SelectStarExpression node)
    {
        HasSelectStarUsage = true;
        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(UnqualifiedJoin node)
    {
        if (node.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin)
            HasCartesianJoinRisk = true;

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(QualifiedJoin node)
    {
        if (node.SearchCondition is null)
            HasCartesianJoinRisk = true;

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(WhereClause node)
    {
        var wasInsideWhere = _insideWhereClause;
        _insideWhereClause = true;
        base.ExplicitVisit(node);
        _insideWhereClause = wasInsideWhere;
    }

    public override void ExplicitVisit(BooleanComparisonExpression node)
    {
        if (_insideWhereClause)
        {
            if (IsFunctionWrappedColumnExpression(node.FirstExpression) || IsFunctionWrappedColumnExpression(node.SecondExpression))
                HasNonSargablePredicate = true;

            if (IsExplicitConversionWrappedColumnExpression(node.FirstExpression) || IsExplicitConversionWrappedColumnExpression(node.SecondExpression))
                HasImplicitConversionRisk = true;
        }

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(LikePredicate node)
    {
        if (HasLeadingWildcardPattern(node.SecondExpression))
            HasLeadingWildcardLike = true;

        if (_insideWhereClause && IsFunctionWrappedColumnExpression(node.FirstExpression))
            HasNonSargablePredicate = true;

        base.ExplicitVisit(node);
    }

    private static bool HasLeadingWildcardPattern(ScalarExpression expression)
    {
        return expression switch
        {
            StringLiteral literal => StartsWithLeadingWildcard(literal.Value),
            ParenthesisExpression { Expression: { } innerExpression } => HasLeadingWildcardPattern(innerExpression),
            _ => false
        };
    }

    private static bool StartsWithLeadingWildcard(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return trimmed.Length > 1 && trimmed.StartsWith('%');
    }

    private static bool IsFunctionWrappedColumnExpression(ScalarExpression expression)
    {
        return expression switch
        {
            FunctionCall functionCall => HasColumnReference(functionCall),
            CastCall castCall => HasColumnReference(castCall.Parameter),
            ConvertCall convertCall => HasColumnReference(convertCall.Parameter),
            ParenthesisExpression { Expression: { } innerExpression } => IsFunctionWrappedColumnExpression(innerExpression),
            _ => false
        };
    }

    private static bool IsExplicitConversionWrappedColumnExpression(ScalarExpression expression)
    {
        return expression switch
        {
            CastCall castCall => HasColumnReference(castCall.Parameter),
            ConvertCall convertCall => HasColumnReference(convertCall.Parameter),
            ParenthesisExpression { Expression: { } innerExpression } => IsExplicitConversionWrappedColumnExpression(innerExpression),
            _ => false
        };
    }

    private static bool HasColumnReference(TSqlFragment? fragment)
    {
        if (fragment is null)
            return false;

        var visitor = new ColumnReferenceVisitor();
        fragment.Accept(visitor);
        return visitor.HasColumnReference;
    }

    private sealed class ColumnReferenceVisitor : TSqlFragmentVisitor
    {
        public bool HasColumnReference { get; private set; }

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            HasColumnReference = true;
        }
    }
}

internal sealed class WorkloadHeuristicsVisitor : TSqlFragmentVisitor
{
    private bool _insideWhereClause;

    public bool HasOrderByClause { get; private set; }
    public int MaxInListItemCount { get; private set; }
    public int MaxOrPredicateFanout { get; private set; }

    public override void ExplicitVisit(QuerySpecification node)
    {
        if (node.OrderByClause is not null)
            HasOrderByClause = true;

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(WhereClause node)
    {
        var wasInsideWhere = _insideWhereClause;
        _insideWhereClause = true;
        base.ExplicitVisit(node);
        _insideWhereClause = wasInsideWhere;
    }

    public override void ExplicitVisit(InPredicate node)
    {
        if (node.Values is { Count: > 0 })
            MaxInListItemCount = Math.Max(MaxInListItemCount, node.Values.Count);

        base.ExplicitVisit(node);
    }

    public override void ExplicitVisit(BooleanBinaryExpression node)
    {
        if (_insideWhereClause && node.BinaryExpressionType == BooleanBinaryExpressionType.Or)
        {
            var fanout = CountOrFanout(node);
            MaxOrPredicateFanout = Math.Max(MaxOrPredicateFanout, fanout);
        }

        base.ExplicitVisit(node);
    }

    private static int CountOrFanout(BooleanExpression expression)
    {
        if (expression is BooleanBinaryExpression { BinaryExpressionType: BooleanBinaryExpressionType.Or } binary)
            return CountOrFanout(binary.FirstExpression) + CountOrFanout(binary.SecondExpression);

        return 1;
    }
}


