using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver.Core.Events;

namespace Mongo.Profiler;

public static class MongoCommandQueryBuilder
{
    private static readonly JsonWriterSettings IndentedJson = new()
    {
        Indent = true,
        OutputMode = JsonOutputMode.Shell
    };

    public static string Build(CommandStartedEvent commandStartedEvent)
    {
        return Build(commandStartedEvent.CommandName, commandStartedEvent.Command);
    }

    public static string Build(string commandName, BsonDocument command)
    {
        var databaseName = command.TryGetValue("$db", out var database) ? database.AsString : string.Empty;

        return commandName switch
        {
            "aggregate" => BuildAggregate(command, databaseName),
            "find" => BuildFind(command, databaseName),
            _ => BuildGenericCommand(command, databaseName)
        };
    }

    private static string BuildAggregate(BsonDocument command, string databaseName)
    {
        if (!command.TryGetValue("aggregate", out var collection) ||
            !command.TryGetValue("pipeline", out var pipeline))
            return string.Empty;

        return $"{BuildCollectionAccessor(databaseName, collection.AsString)}.aggregate({pipeline.ToJson(IndentedJson)});";
    }

    private static string BuildFind(BsonDocument command, string databaseName)
    {
        if (!command.TryGetValue("find", out var collection))
            return string.Empty;

        var filter = command.TryGetValue("filter", out var filterValue)
            ? BuildFilter(filterValue)
            : "{}";

        var query = $"{BuildCollectionAccessor(databaseName, collection.AsString)}.find({filter}";
        if (command.TryGetValue("projection", out var projection))
            query += $", {projection.ToJson(IndentedJson)}";
        query += ")";

        if (command.TryGetValue("sort", out var sort))
            query += $".sort({sort.ToJson(IndentedJson)})";

        if (command.TryGetValue("skip", out var skip) && !skip.IsBsonNull)
            query += $".skip({skip})";

        if (command.TryGetValue("limit", out var limit) && !limit.IsBsonNull)
            query += $".limit({limit})";

        if (command.TryGetValue("collation", out var collation))
            query += $".collation({collation.ToJson(IndentedJson)})";

        return $"{query};";
    }

    private static string BuildFilter(BsonValue filterValue)
    {
        var normalizedFilter = NormalizeDateTimeOffsetFilter(filterValue);
        return normalizedFilter.ToJson(IndentedJson);
    }

    private static BsonValue NormalizeDateTimeOffsetFilter(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.Document => NormalizeDocumentFilter(value.AsBsonDocument),
            BsonType.Array => new BsonArray(value.AsBsonArray.Select(NormalizeDateTimeOffsetFilter)),
            _ => value
        };
    }

    private static BsonDocument NormalizeDocumentFilter(BsonDocument document)
    {
        var normalized = new BsonDocument();

        foreach (var element in document)
        {
            if (!element.Name.StartsWith("$") &&
                element.Value.BsonType == BsonType.Document &&
                TryGetDateTimeOffsetDate(element.Value.AsBsonDocument, out var dateTime))
            {
                var startsAtMidnightUtc = dateTime.ToUniversalTime().TimeOfDay == TimeSpan.Zero;
                if (startsAtMidnightUtc)
                {
                    normalized[$"{element.Name}.DateTime"] = new BsonDocument
                    {
                        { "$gte", dateTime },
                        { "$lt", new BsonDateTime(dateTime.ToUniversalTime().AddDays(1)) }
                    };
                }
                else
                {
                    normalized[$"{element.Name}.DateTime"] = dateTime;
                }
                continue;
            }

            if (element.Value.BsonType == BsonType.Document)
            {
                normalized[element.Name] = NormalizeOperatorDocument(element.Value.AsBsonDocument);
                continue;
            }

            if (element.Value.BsonType == BsonType.Array)
            {
                normalized[element.Name] = new BsonArray(element.Value.AsBsonArray.Select(NormalizeDateTimeOffsetFilter));
                continue;
            }

            normalized[element.Name] = element.Value;
        }

        return normalized;
    }

    private static BsonDocument NormalizeOperatorDocument(BsonDocument document)
    {
        var normalized = new BsonDocument();

        foreach (var element in document)
        {
            if (element.Value.BsonType == BsonType.Document &&
                TryGetDateTimeOffsetDate(element.Value.AsBsonDocument, out var dateTime))
            {
                normalized[element.Name] = dateTime;
                continue;
            }

            normalized[element.Name] = NormalizeDateTimeOffsetFilter(element.Value);
        }

        return normalized;
    }

    private static bool TryGetDateTimeOffsetDate(BsonDocument value, out BsonDateTime dateTime)
    {
        dateTime = BsonDateTime.Create(DateTime.UnixEpoch);

        if (!value.TryGetValue("DateTime", out var dateTimeValue) ||
            !value.Contains("Ticks") ||
            !value.Contains("Offset") ||
            dateTimeValue.BsonType != BsonType.DateTime)
        {
            return false;
        }

        dateTime = dateTimeValue.AsBsonDateTime;
        return true;
    }

    private static string BuildCollectionAccessor(string databaseName, string collectionName)
    {
        var databaseLiteral = new BsonString(databaseName).ToJson();
        var collectionLiteral = new BsonString(collectionName).ToJson();

        return $"db.getSiblingDB({databaseLiteral}).getCollection({collectionLiteral})";
    }

    private static string BuildGenericCommand(BsonDocument command, string databaseName)
    {
        var sanitized = command.DeepClone().AsBsonDocument;
        sanitized.Remove("$db");
        var dbAccessor = string.IsNullOrWhiteSpace(databaseName)
            ? "db"
            : $"db.getSiblingDB({new BsonString(databaseName).ToJson()})";

        return $"{dbAccessor}.runCommand({sanitized.ToJson(IndentedJson)});";
    }
}
