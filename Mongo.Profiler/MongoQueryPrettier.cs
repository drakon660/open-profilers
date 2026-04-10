using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace Mongo.Profiler;

public static class MongoQueryPrettier
{
    private static readonly JsonWriterSettings IndentedShell = new()
    {
        Indent = true,
        OutputMode = JsonOutputMode.Shell
    };

    private static readonly JsonWriterSettings CompactShell = new()
    {
        Indent = false,
        OutputMode = JsonOutputMode.Shell
    };

    public static string Prettify(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        BsonDocument command;
        try
        {
            command = BsonDocument.Parse(query);
        }
        catch
        {
            return query;
        }

        if (!command.TryGetValue("find", out var findCollection) || findCollection.BsonType != BsonType.String)
            return query;

        var collection = findCollection.AsString;
        var filter = command.TryGetValue("filter", out var filterValue) && filterValue.BsonType == BsonType.Document
            ? filterValue.AsBsonDocument
            : new BsonDocument();

        var result = $"db.{collection}.find({BuildFilter(filter)}";

        if (command.TryGetValue("projection", out var projectionValue) && projectionValue.BsonType == BsonType.Document)
            result += $", {ToIndentedShell(projectionValue)}";

        result += ")";

        if (command.TryGetValue("sort", out var sortValue) && sortValue.BsonType == BsonType.Document)
            result += $".sort({ToIndentedShell(sortValue)})";

        var limitValue = command.TryGetValue("limit", out var limit) ? limit :
            command.TryGetValue("batchSize", out var batchSize) ? batchSize : null;
        if (limitValue is not null && TryFormatIntegerLike(limitValue, out var numeric))
            result += $".limit({numeric})";

        return result;
    }

    private static string BuildFilter(BsonDocument filter)
    {
        if (filter.ElementCount == 0)
            return "{}";

        var parts = new List<string>();
        foreach (var element in filter)
        {
            if (TryBuildCompactComparison(element, out var compact))
            {
                parts.Add(compact);
                continue;
            }

            if (element.Value.BsonType == BsonType.Document)
            {
                var docJson = ToIndentedShell(element.Value);
                parts.Add($"\"{element.Name}\" : {IndentMultiline(docJson, 2)}");
                continue;
            }

            parts.Add($"\"{element.Name}\" : {element.Value.ToJson(CompactShell)}");
        }

        return "{" + string.Join(",  ", parts) + "}";
    }

    private static bool TryBuildCompactComparison(BsonElement element, out string compact)
    {
        compact = string.Empty;
        if (element.Value.BsonType != BsonType.Document)
            return false;

        var valueDoc = element.Value.AsBsonDocument;
        if (valueDoc.ElementCount != 1)
            return false;

        var op = valueDoc.GetElement(0);
        if (!op.Name.StartsWith('$') || !TryFormatIntegerLike(op.Value, out var numeric))
            return false;

        compact = $"{element.Name}:{{{op.Name}:{numeric}}}";
        return true;
    }

    private static bool TryFormatIntegerLike(BsonValue value, out string formatted)
    {
        formatted = string.Empty;
        switch (value.BsonType)
        {
            case BsonType.Int32:
                formatted = value.AsInt32.ToString();
                return true;
            case BsonType.Int64:
                formatted = value.AsInt64.ToString();
                return true;
            case BsonType.Double:
                formatted = ((long)value.AsDouble).ToString();
                return true;
            case BsonType.Decimal128:
                formatted = ((long)value.AsDecimal128).ToString();
                return true;
            default:
                return false;
        }
    }

    private static string ToIndentedShell(BsonValue value)
    {
        return value.ToJson(IndentedShell).Replace("\r\n", "\n");
    }

    private static string IndentMultiline(string value, int extraIndentSpaces)
    {
        var lines = value.Split('\n');
        if (lines.Length <= 1)
            return value;

        for (var i = 1; i < lines.Length; i++)
            lines[i] = new string(' ', extraIndentSpaces) + lines[i];
        return string.Join("\n", lines);
    }
}
