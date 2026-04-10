using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Mongo.Profiler;

namespace EFCore.Profiler;

public sealed class EfProfilerCommandInterceptor(
    IMongoProfilerEventSink sink,
    EfCoreProfilerOptions options) : DbCommandInterceptor
{
    private const string RedactedValue = "***REDACTED***";
    private const int InListTooLargeThreshold = 20;
    private const int OrPredicateFanoutThreshold = 6;
    private const int QueryTextTooLargeThreshold = 16_000;
    private readonly ConcurrentDictionary<Guid, CommandExecutionState> _commandStateById = new();
    private readonly ConcurrentDictionary<Guid, byte> _publishedCommandIds = new();
    private readonly ConcurrentDictionary<Guid, int> _activeCommandCountByContext = new();
    private readonly ConcurrentDictionary<Guid, int> _lastThreadByContext = new();
    private readonly ConcurrentDictionary<string, NPlusOneTracker> _nPlusOneTrackerByKey = new();
    private readonly ConcurrentDictionary<string, WarningRepeatTracker> _warningRepeatTrackerByKey = new();
    private readonly HashSet<string> _sensitiveParameterNames = BuildSensitiveParameterSet(options.SensitiveParameterNames);

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        EnforceUnsafeDmlGuard(command);
        TrackStart(eventData);
        return result;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        TrackStart(eventData);
        return result;
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        TrackStart(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnforceUnsafeDmlGuard(command);
        TrackStart(eventData);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        TrackStart(eventData);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        TrackStart(eventData);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        Publish(command, eventData, resultCount: result);
        return result;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        Publish(command, eventData, resultCount: null);
        return result;
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        Publish(command, eventData, resultCount: null);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Publish(command, eventData, resultCount: result);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Publish(command, eventData, resultCount: null);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        Publish(command, eventData, resultCount: null);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        PublishFailure(command, eventData);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        PublishFailure(command, eventData);
        return Task.CompletedTask;
    }

    private void TrackStart(CommandEventData eventData)
    {
        if (!options.Enabled)
            return;

        if (_commandStateById.ContainsKey(eventData.CommandId))
            return;

        _publishedCommandIds.TryRemove(eventData.CommandId, out _);

        var contextId = eventData.Context?.ContextId.InstanceId;
        var threadId = Environment.CurrentManagedThreadId;
        var hasParallelUse = false;
        var hasThreadHop = false;

        if (contextId.HasValue)
        {
            var activeCount = _activeCommandCountByContext.AddOrUpdate(contextId.Value, 1, static (_, current) => current + 1);
            hasParallelUse = activeCount > 1;

            if (_lastThreadByContext.TryGetValue(contextId.Value, out var previousThreadId) && previousThreadId != threadId)
                hasThreadHop = true;

            _lastThreadByContext[contextId.Value] = threadId;
        }

        _commandStateById.TryAdd(eventData.CommandId, new CommandExecutionState(
            Stopwatch.GetTimestamp(),
            contextId,
            threadId,
            hasParallelUse,
            hasThreadHop));
    }

    private void EnforceUnsafeDmlGuard(DbCommand command)
    {
        if (!options.BlockUnsafeDmlWithoutWhere)
            return;

        var metadata = SqlServerCommandParser.Parse(command.CommandText ?? string.Empty);
        if (!metadata.MissingWhereOnDml)
            return;

        throw new InvalidOperationException(
            "Blocked unsafe DML: UPDATE/DELETE without WHERE clause. " +
            "Set EfCoreProfiler:BlockUnsafeDmlWithoutWhere=false to disable this guard.");
    }

    private void Publish(
        DbCommand command,
        CommandExecutedEventData eventData,
        int? resultCount)
    {
        if (!options.Enabled)
            return;

        if (!_publishedCommandIds.TryAdd(eventData.CommandId, 0))
            return;

        var executionState = RemoveExecutionState(eventData);
        var durationMs = ResolveDurationMs(eventData, executionState);
        if (durationMs < options.MinDurationMs)
            return;

        var rawSql = command.CommandText ?? string.Empty;
        var sql = BuildSqlPayload(command);
        var sqlMetadata = SqlServerCommandParser.Parse(rawSql);
        var commandName = !string.IsNullOrWhiteSpace(sqlMetadata.CommandName)
            ? sqlMetadata.CommandName
            : InferCommandName(rawSql);
        var collectionName = sqlMetadata.TableNames.FirstOrDefault() ?? string.Empty;
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
        var queryShapeId = BuildQueryShapeId(command);
        var alert = EvaluateAlert(sqlMetadata, commandName, durationMs, resultCount, executionState, traceId, rawSql);
        if (ApplyWarningRepeatAggregation(
                commandName,
                command.Connection?.Database ?? string.Empty,
                collectionName,
                queryShapeId,
                ref alert))
        {
            return;
        }

        var executionPlan = TryCaptureExecutionPlan(command, durationMs, alert);

        try
        {
            sink.Publish(new MongoProfilerQueryEvent
            {
                CommandName = commandName,
                DatabaseName = command.Connection?.Database ?? string.Empty,
                CollectionName = collectionName,
                Query = sql,
                DurationMs = durationMs,
                ResultCount = resultCount,
                Success = true,
                ErrorMessage = null,
                RequestId = eventData.CommandId.ToString("N"),
                ServerEndpoint = command.Connection?.DataSource ?? string.Empty,
                OperationId = eventData.CommandId.ToString("N"),
                SessionId = executionState.ContextId?.ToString("N") ?? string.Empty,
                QueryFingerprint = queryShapeId,
                IndexAdviceStatus = alert.Status,
                IndexAdviceReason = alert.Reason,
                WinningPlanSummary = executionPlan.Summary,
                ExecutionPlanXml = executionPlan.Xml,
                TraceId = traceId,
                SpanId = Activity.Current?.SpanId.ToString() ?? string.Empty
            });
        }
        catch
        {
            // Profiling failures should never affect user requests.
        }
    }

    private void PublishFailure(
        DbCommand command,
        CommandErrorEventData eventData)
    {
        if (!options.Enabled)
            return;

        if (!_publishedCommandIds.TryAdd(eventData.CommandId, 0))
            return;

        var executionState = RemoveExecutionState(eventData.CommandId);
        var durationMs = ResolveDurationMs(eventData.Duration, executionState);
        if (durationMs < options.MinDurationMs)
            return;

        var rawSql = command.CommandText ?? string.Empty;
        var sql = BuildSqlPayload(command);
        var sqlMetadata = SqlServerCommandParser.Parse(rawSql);
        var commandName = !string.IsNullOrWhiteSpace(sqlMetadata.CommandName)
            ? sqlMetadata.CommandName
            : InferCommandName(rawSql);
        var collectionName = sqlMetadata.TableNames.FirstOrDefault() ?? string.Empty;
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
        var queryShapeId = BuildQueryShapeId(command);
        var alert = EvaluateAlert(sqlMetadata, commandName, durationMs, resultCount: null, executionState, traceId, rawSql);
        if (ApplyWarningRepeatAggregation(
                commandName,
                command.Connection?.Database ?? string.Empty,
                collectionName,
                queryShapeId,
                ref alert))
        {
            return;
        }

        var executionPlan = TryCaptureExecutionPlan(command, durationMs, alert);

        try
        {
            sink.Publish(new MongoProfilerQueryEvent
            {
                CommandName = commandName,
                DatabaseName = command.Connection?.Database ?? string.Empty,
                CollectionName = collectionName,
                Query = sql,
                DurationMs = durationMs,
                ResultCount = null,
                Success = false,
                ErrorMessage = eventData.Exception.Message,
                RequestId = eventData.CommandId.ToString("N"),
                ServerEndpoint = command.Connection?.DataSource ?? string.Empty,
                OperationId = eventData.CommandId.ToString("N"),
                SessionId = executionState.ContextId?.ToString("N") ?? string.Empty,
                QueryFingerprint = queryShapeId,
                IndexAdviceStatus = alert.Status,
                IndexAdviceReason = string.IsNullOrWhiteSpace(alert.Reason)
                    ? "command_failed"
                    : $"{alert.Reason}, command_failed",
                WinningPlanSummary = executionPlan.Summary,
                ExecutionPlanXml = executionPlan.Xml,
                TraceId = traceId,
                SpanId = Activity.Current?.SpanId.ToString() ?? string.Empty
            });
        }
        catch
        {
            // Profiling failures should never affect user requests.
        }
    }

    private CommandExecutionState RemoveExecutionState(CommandExecutedEventData eventData)
    {
        return RemoveExecutionState(eventData.CommandId);
    }

    private CommandExecutionState RemoveExecutionState(Guid commandId)
    {
        if (!_commandStateById.TryRemove(commandId, out var state))
            return CommandExecutionState.Empty;

        if (state.ContextId.HasValue)
        {
            var updatedCount = _activeCommandCountByContext.AddOrUpdate(state.ContextId.Value, 0, static (_, current) => Math.Max(0, current - 1));
            if (updatedCount == 0)
                _activeCommandCountByContext.TryRemove(state.ContextId.Value, out _);
        }

        return state;
    }

    private static double ResolveDurationMs(CommandExecutedEventData eventData, CommandExecutionState executionState)
    {
        return ResolveDurationMs(eventData.Duration, executionState);
    }

    private static double ResolveDurationMs(TimeSpan measuredDuration, CommandExecutionState executionState)
    {
        if (measuredDuration > TimeSpan.Zero)
            return measuredDuration.TotalMilliseconds;

        if (executionState.StartTimestamp == 0)
            return 0d;

        var elapsed = Stopwatch.GetElapsedTime(executionState.StartTimestamp);
        return elapsed.TotalMilliseconds;
    }

    private string BuildSqlPayload(DbCommand command)
    {
        var sql = command.CommandText ?? string.Empty;
        if (!options.CaptureParameters || command.Parameters.Count == 0)
            return Truncate(sql, options.MaxSqlLength);

        var payload = new StringBuilder(sql.Length + 128);
        payload.Append(sql);
        payload.AppendLine();
        payload.Append("-- params: ");

        var isFirst = true;
        foreach (DbParameter parameter in command.Parameters)
        {
            if (!isFirst)
                payload.Append(", ");

            isFirst = false;
            var safeName = parameter.ParameterName ?? string.Empty;
            payload.Append(safeName);
            payload.Append('=');
            payload.Append(GetParameterValue(parameter));
        }

        return Truncate(payload.ToString(), options.MaxSqlLength);
    }

    private string GetParameterValue(DbParameter parameter)
    {
        if (_sensitiveParameterNames.Contains(parameter.ParameterName ?? string.Empty))
            return RedactedValue;

        return parameter.Value switch
        {
            null => "null",
            DBNull => "null",
            string stringValue => Truncate(stringValue, 128),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => Truncate(parameter.Value.ToString() ?? string.Empty, 128)
        };
    }

    private static HashSet<string> BuildSensitiveParameterSet(IReadOnlyCollection<string> names)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawName in names)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                continue;

            var name = rawName.Trim();
            set.Add(name);
            if (!name.StartsWith('@'))
                set.Add($"@{name}");
        }

        return set;
    }

    private static string InferCommandName(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        var span = sql.AsSpan().TrimStart();
        var firstSpace = span.IndexOf(' ');
        var firstToken = firstSpace < 0 ? span : span[..firstSpace];
        return firstToken.ToString().ToUpperInvariant();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0 || value.Length <= maxLength)
            return value;

        return $"{value[..maxLength]}...[truncated]";
    }

    private SqlAlert EvaluateAlert(
        SqlCommandMetadata metadata,
        string commandName,
        double durationMs,
        int? resultCount,
        CommandExecutionState executionState,
        string traceId,
        string rawSql)
    {
        if (!options.EnableSafetyAlerts)
            return SqlAlert.None;

        var reasons = new List<string>();

        if (metadata.MissingWhereOnDml)
            reasons.Add("missing_where_on_dml");

        if (metadata.HasSelectStarUsage)
            reasons.Add("select_star_usage");

        if (metadata.HasCartesianJoinRisk)
            reasons.Add("cartesian_join_risk");

        if (metadata.HasLeadingWildcardLike)
            reasons.Add("leading_wildcard_like");

        if (metadata.HasNonSargablePredicate)
            reasons.Add("non_sargable_predicate");

        if (metadata.HasImplicitConversionRisk)
            reasons.Add("implicit_conversion_risk");

        if (rawSql.Length >= QueryTextTooLargeThreshold)
            reasons.Add("query_text_too_large");

        if (metadata.MaxInListItemCount >= InListTooLargeThreshold)
            reasons.Add("in_list_too_large");

        if (metadata.MaxOrPredicateFanout >= OrPredicateFanoutThreshold)
            reasons.Add("or_predicate_fanout");

        if (string.Equals(commandName, "SELECT", StringComparison.OrdinalIgnoreCase))
        {
            if (metadata.HasRowLimitClause && !metadata.HasOrderByClause)
                reasons.Add("row_limiting_without_orderby");

            if (metadata.HasOrderByClause && !metadata.HasRowLimitClause)
                reasons.Add("order_by_without_limit");

            var readsFromTable = metadata.TableNames.Count > 0;
            if (readsFromTable &&
                !metadata.IsAggregateOnlyProjection &&
                !metadata.HasRowLimitClause &&
                !metadata.HasWhereClause)
            {
                reasons.Add("unbounded_select_risk");
            }
        }

        if (metadata.JoinCount > 4)
            reasons.Add("avoid_to_many_joins");

        if (metadata.TableNames.Count > 4)
            reasons.Add("two_many_tables");

        if (resultCount.HasValue && resultCount.Value >= options.LargeResultWarningThreshold)
            reasons.Add("large_result_set");

        if (durationMs >= options.SlowQueryWarningMs)
            reasons.Add("slow_query");

        if (executionState.HasParallelUse)
            reasons.Add("shared_context_parallel_use");
        else if (executionState.HasThreadHop)
            reasons.Add("context_thread_hop_only");

        var hasNPlusOne = EvaluateNPlusOne(metadata, commandName, traceId, rawSql, executionState);
        if (hasNPlusOne)
            reasons.Add("n_plus_one");

        if (ShouldEmitGeneratedSqlComplexityAlert(metadata, rawSql))
            reasons.Add("generated_sql_complexity");

        if (reasons.Count == 0)
            return SqlAlert.None;

        return new SqlAlert("warning", string.Join(", ", reasons));
    }

    private bool ShouldEmitGeneratedSqlComplexityAlert(
        SqlCommandMetadata metadata,
        string rawSql)
    {
        if (!options.EnableGeneratedSqlComplexityAlert)
            return false;

        var signalCount = 0;
        if (metadata.HasSelectStarUsage)
            signalCount++;
        if (metadata.HasCartesianJoinRisk)
            signalCount++;
        if (metadata.HasLeadingWildcardLike)
            signalCount++;
        if (metadata.HasNonSargablePredicate)
            signalCount++;
        if (metadata.HasImplicitConversionRisk)
            signalCount++;
        if (rawSql.Length >= QueryTextTooLargeThreshold)
            signalCount++;
        if (metadata.MaxInListItemCount >= InListTooLargeThreshold)
            signalCount++;
        if (metadata.MaxOrPredicateFanout >= OrPredicateFanoutThreshold)
            signalCount++;
        if (metadata.JoinCount > 4)
            signalCount++;
        if (metadata.TableNames.Count > 4)
            signalCount++;

        var minSignals = Math.Max(options.GeneratedSqlComplexityMinSignals, 1);
        return signalCount >= minSignals;
    }

    private ExecutionPlanCaptureResult TryCaptureExecutionPlan(
        DbCommand command,
        double durationMs,
        SqlAlert alert)
    {
        if (!ShouldCaptureExecutionPlan(command, durationMs, alert))
            return ExecutionPlanCaptureResult.Empty;

        try
        {
            var connection = command.Connection;
            if (connection is null || connection.State != ConnectionState.Open)
                return ExecutionPlanCaptureResult.Empty;

            var planXml = ExecuteShowPlanCommand(command);
            if (string.IsNullOrWhiteSpace(planXml))
                return ExecutionPlanCaptureResult.Empty;

            var summary = BuildExecutionPlanSummary(planXml);
            var xmlPayload = options.IncludeExecutionPlanXml
                ? Truncate(planXml, options.MaxExecutionPlanXmlLength)
                : string.Empty;
            return new ExecutionPlanCaptureResult(summary, xmlPayload);
        }
        catch
        {
            // Plan capture should never affect request execution.
            return ExecutionPlanCaptureResult.Empty;
        }
    }

    private bool ShouldCaptureExecutionPlan(
        DbCommand command,
        double durationMs,
        SqlAlert alert)
    {
        if (!options.EnableSqlExecutionPlanCapture)
            return false;

        if (command.CommandType != CommandType.Text)
            return false;

        if (string.IsNullOrWhiteSpace(command.CommandText))
            return false;

        if (!IsSqlServerProvider(command))
            return false;

        if (options.SqlExecutionPlanCaptureOnlyWhenAlerted &&
            !string.Equals(alert.Status, "warning", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (durationMs < options.SqlExecutionPlanCaptureMinDurationMs)
            return false;

        return true;
    }

    private static bool IsSqlServerProvider(DbCommand command)
    {
        var typeName = command.GetType().FullName ?? string.Empty;
        return typeName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExecuteShowPlanCommand(DbCommand sourceCommand)
    {
        var connection = sourceCommand.Connection;
        if (connection is null)
            return string.Empty;

        using var showPlanCommand = connection.CreateCommand();
        showPlanCommand.Transaction = sourceCommand.Transaction;
        showPlanCommand.CommandType = sourceCommand.CommandType;
        showPlanCommand.CommandTimeout = sourceCommand.CommandTimeout;
        showPlanCommand.CommandText = sourceCommand.CommandText;
        CloneParameters(sourceCommand, showPlanCommand);

        using var setOnCommand = connection.CreateCommand();
        setOnCommand.Transaction = sourceCommand.Transaction;
        setOnCommand.CommandTimeout = sourceCommand.CommandTimeout;
        setOnCommand.CommandText = "SET SHOWPLAN_XML ON;";

        using var setOffCommand = connection.CreateCommand();
        setOffCommand.Transaction = sourceCommand.Transaction;
        setOffCommand.CommandTimeout = sourceCommand.CommandTimeout;
        setOffCommand.CommandText = "SET SHOWPLAN_XML OFF;";

        setOnCommand.ExecuteNonQuery();
        try
        {
            using var reader = showPlanCommand.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    return reader.GetString(0);
            }

            return string.Empty;
        }
        finally
        {
            try
            {
                setOffCommand.ExecuteNonQuery();
            }
            catch
            {
                // Ignore reset failures; command execution must remain unaffected.
            }
        }
    }

    private static void CloneParameters(DbCommand sourceCommand, DbCommand targetCommand)
    {
        foreach (DbParameter source in sourceCommand.Parameters)
        {
            var clone = targetCommand.CreateParameter();
            clone.ParameterName = source.ParameterName;
            clone.Value = source.Value;
            clone.DbType = source.DbType;
            clone.Direction = source.Direction;
            clone.Precision = source.Precision;
            clone.Scale = source.Scale;
            clone.Size = source.Size;
            clone.IsNullable = source.IsNullable;
            targetCommand.Parameters.Add(clone);
        }
    }

    private static string BuildExecutionPlanSummary(string planXml)
    {
        try
        {
            var document = XDocument.Parse(planXml);
            var operators = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "RelOp", StringComparison.Ordinal))
                .Select(element => (element.Attribute("PhysicalOp")?.Value ?? string.Empty).Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (operators.Count == 0)
                return "estimated_plan_captured";

            var grouped = operators
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .Select(group => $"{group.Key} x{group.Count()}")
                .ToList();

            return $"estimated_plan_captured: {string.Join(", ", grouped)}";
        }
        catch
        {
            return "estimated_plan_captured";
        }
    }

    private bool EvaluateNPlusOne(
        SqlCommandMetadata metadata,
        string commandName,
        string traceId,
        string rawSql,
        CommandExecutionState executionState)
    {
        if (!options.EnableNPlusOneAlert)
            return false;

        if (!string.Equals(commandName, "SELECT", StringComparison.OrdinalIgnoreCase))
            return false;

        // Tune for common child-collection lazy-load pattern:
        // many repeated simple single-table SELECT ... WHERE <FK> = @p...
        if (!metadata.HasWhereClause)
            return false;

        if (metadata.IsAggregateOnlyProjection || metadata.HasRowLimitClause)
            return false;

        if (metadata.JoinCount != 0 || metadata.TableNames.Count != 1)
            return false;

        if (!rawSql.Contains("@", StringComparison.Ordinal) || !rawSql.Contains("=", StringComparison.Ordinal))
            return false;

        var correlationId = !string.IsNullOrWhiteSpace(traceId)
            ? $"trace:{traceId}"
            : executionState.ContextId.HasValue
                ? $"ctx:{executionState.ContextId.Value:N}"
                : string.Empty;
        if (string.IsNullOrWhiteSpace(correlationId))
            return false;

        var shapeHash = BuildShapeHash(rawSql);
        var key = $"{correlationId}|{metadata.TableNames[0]}|{shapeHash}";

        var tracker = _nPlusOneTrackerByKey.AddOrUpdate(
            key,
            _ => new NPlusOneTracker(1),
            (_, existing) => new NPlusOneTracker(existing.Count + 1));

        // Opportunistic cleanup to keep tracker bounded.
        if (_nPlusOneTrackerByKey.Count > 2000)
            _nPlusOneTrackerByKey.Clear();

        return tracker.Count >= options.NPlusOneMinRepeatedQueries;
    }

    private static string BuildShapeHash(string rawSql)
    {
        if (string.IsNullOrWhiteSpace(rawSql))
            return string.Empty;

        var normalized = rawSql
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        var sqlWithoutLiterals = StringBuilderCache.ReplaceQuotedLiterals(normalized);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sqlWithoutLiterals.ToUpperInvariant()));
        return Convert.ToHexString(hash);
    }

    private static string BuildQueryShapeId(DbCommand command)
    {
        var rawSql = command.CommandText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawSql))
            return string.Empty;

        var normalized = rawSql
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        while (normalized.Contains("  ", StringComparison.Ordinal))
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);

        var sqlWithoutLiterals = StringBuilderCache.ReplaceQuotedLiterals(normalized).ToUpperInvariant();
        var parameterTypes = new List<string>(command.Parameters.Count);
        foreach (DbParameter parameter in command.Parameters)
        {
            var name = parameter.ParameterName ?? string.Empty;
            var signature = ResolveParameterTypeSignature(parameter);
            parameterTypes.Add($"{name}:{signature}");
        }

        parameterTypes.Sort(StringComparer.OrdinalIgnoreCase);
        var shapePayload = parameterTypes.Count == 0
            ? sqlWithoutLiterals
            : $"{sqlWithoutLiterals}|{string.Join(",", parameterTypes)}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(shapePayload));
        return Convert.ToHexString(hash);
    }

    private static string ResolveParameterTypeSignature(DbParameter parameter)
    {
        if (parameter.DbType != DbType.Object)
            return parameter.DbType.ToString();

        if (parameter.Value is not null and not DBNull)
            return parameter.Value.GetType().Name;

        return parameter.GetType().Name;
    }

    private bool ApplyWarningRepeatAggregation(
        string commandName,
        string databaseName,
        string collectionName,
        string queryShapeId,
        ref SqlAlert alert)
    {
        if (!options.EnableWarningRepeatAggregation)
            return false;

        if (!string.Equals(alert.Status, "warning", StringComparison.OrdinalIgnoreCase))
            return false;

        var key = BuildWarningRepeatKey(commandName, databaseName, collectionName, queryShapeId, alert.Reason);
        var now = DateTimeOffset.UtcNow;
        var windowMs = Math.Max(options.WarningRepeatWindowMs, 1);

        while (true)
        {
            if (!_warningRepeatTrackerByKey.TryGetValue(key, out var existing))
            {
                if (_warningRepeatTrackerByKey.TryAdd(key, new WarningRepeatTracker(1, now, now)))
                    return false;

                continue;
            }

            var sameWindow = (now - existing.LastSeenUtc).TotalMilliseconds <= windowMs;
            var next = sameWindow
                ? existing with { Count = existing.Count + 1, LastSeenUtc = now }
                : new WarningRepeatTracker(1, now, now);

            if (!_warningRepeatTrackerByKey.TryUpdate(key, next, existing))
                continue;

            if (_warningRepeatTrackerByKey.Count > options.WarningRepeatMaxTrackedKeys)
                _warningRepeatTrackerByKey.Clear();

            if (!sameWindow || next.Count <= 1)
                return false;

            var emitEvery = Math.Max(options.WarningRepeatEmitEvery, 1);
            if (next.Count % emitEvery != 0)
                return true;

            var firstSeen = next.FirstSeenUtc.ToString("O", CultureInfo.InvariantCulture);
            var lastSeen = next.LastSeenUtc.ToString("O", CultureInfo.InvariantCulture);
            alert = alert with
            {
                Reason = $"{alert.Reason}, repeat_count={next.Count}, first_seen_utc={firstSeen}, last_seen_utc={lastSeen}"
            };
            return false;
        }
    }

    private static string BuildWarningRepeatKey(
        string commandName,
        string databaseName,
        string collectionName,
        string queryShapeId,
        string alertReason)
    {
        return string.Join("|",
            commandName,
            databaseName,
            collectionName,
            queryShapeId,
            alertReason);
    }

    private sealed record SqlAlert(string Status, string Reason)
    {
        public static readonly SqlAlert None = new(string.Empty, string.Empty);
    }
    private sealed record ExecutionPlanCaptureResult(string Summary, string Xml)
    {
        public static readonly ExecutionPlanCaptureResult Empty = new(string.Empty, string.Empty);
    }

    private sealed record CommandExecutionState(
        long StartTimestamp,
        Guid? ContextId,
        int ThreadId,
        bool HasParallelUse,
        bool HasThreadHop)
    {
        public static readonly CommandExecutionState Empty = new(0, null, 0, false, false);
    }

    private sealed record NPlusOneTracker(int Count);
    private sealed record WarningRepeatTracker(int Count, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

    private static class StringBuilderCache
    {
        public static string ReplaceQuotedLiterals(string sql)
        {
            var builder = new StringBuilder(sql.Length);
            var inString = false;
            for (var i = 0; i < sql.Length; i++)
            {
                var ch = sql[i];
                if (ch == '\'')
                {
                    inString = !inString;
                    builder.Append('\'');
                    builder.Append('?');
                    continue;
                }

                if (!inString)
                    builder.Append(ch);
            }

            return builder.ToString();
        }
    }
}
