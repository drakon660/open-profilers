using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using System.Text.RegularExpressions;

namespace Mongo.Profiler;

public sealed class MongoSystemProfileCheckpoint
{
    public BsonValue? LastTimestamp { get; set; }
}

public sealed class MongoSystemProfileEntry
{
    public string EventKey { get; set; } = string.Empty;
    public DateTimeOffset? TimestampUtc { get; set; }
    public string TsRaw { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string CommandDocument { get; set; } = string.Empty;
    public long? DocsExamined { get; set; }
    public long? NReturned { get; set; }
    public string Op { get; set; } = string.Empty;
    public string CommandName { get; set; } = string.Empty;
    public string ServerEndpoint { get; set; } = string.Empty;
    public double DurationMs { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class MongoSystemProfileReadPage
{
    public MongoSystemProfileCheckpoint Checkpoint { get; set; } = new();
    public IReadOnlyList<MongoSystemProfileEntry> Entries { get; set; } = [];
    public int RawDocumentCount { get; set; }
}

public static class MongoSystemProfileReader
{
    public const string ReaderComment = "mongo-profiler-direct-profile-reader";

    public static BsonDocument BuildExcludeSystemProfileNamespaceFilter(string databaseName = "profiler_samples")
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            databaseName = "profiler_samples";

        var escapedDatabase = Regex.Escape(databaseName.Trim());
        var namespaceRegex = new BsonRegularExpression($"^{escapedDatabase}\\.system\\.profile$", "i");
        return new BsonDocument("ns", new BsonDocument("$not", namespaceRegex));
    }

    public static async Task<MongoSystemProfileCheckpoint> BootstrapAsync(
        IMongoDatabase database,
        string? readerComment,
        CancellationToken cancellationToken)
    {
        readerComment ??= ReaderComment;
        var profileCollection = database.GetCollection<BsonDocument>("system.profile");
        var checkpoint = new MongoSystemProfileCheckpoint();
        var readOptions = new FindOptions { Comment = readerComment };
        var bootstrap = await profileCollection
            .Find(FilterDefinition<BsonDocument>.Empty, readOptions)
            .Sort(Builders<BsonDocument>.Sort.Descending("$natural"))
            .Limit(1)
            .ToListAsync(cancellationToken);

        if (bootstrap.Count == 0)
            return checkpoint;

        var bootstrapDoc = bootstrap[0];
        if (TryGetComparableTs(bootstrapDoc, out var tsValue))
            checkpoint.LastTimestamp = tsValue;

        return checkpoint;
    }

    public static async Task<MongoSystemProfileReadPage> ReadNextPageAsync(
        IMongoDatabase database,
        MongoSystemProfileCheckpoint checkpoint,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var profileCollection = database.GetCollection<BsonDocument>("system.profile");
        var readOptions = new FindOptions { Comment = ReaderComment };
        var checkpointFilter = BuildCheckpointFilter(checkpoint.LastTimestamp);
        var namespaceFilter = new BsonDocumentFilterDefinition<BsonDocument>(BuildExcludeSystemProfileNamespaceFilter());
        var filter = Builders<BsonDocument>.Filter.And(checkpointFilter, namespaceFilter);
        var documents = await profileCollection
            .Find(filter, readOptions)
            .Sort(Builders<BsonDocument>.Sort.Ascending("ts"))
            .Limit(batchSize)
            .ToListAsync(cancellationToken);

        var entries = new List<MongoSystemProfileEntry>();
        foreach (var document in documents)
        {
            if (TryGetComparableTs(document, out var tsValue))
                checkpoint.LastTimestamp = tsValue;

            if (ShouldSkipProfileDocument(document, ReaderComment))
                continue;

            entries.Add(BuildEntry(document));
        }

        return new MongoSystemProfileReadPage
        {
            Checkpoint = checkpoint,
            Entries = entries,
            RawDocumentCount = documents.Count
        };
    }

    private static MongoSystemProfileEntry BuildEntry(BsonDocument profileDoc)
    {
        var timestampUtc = ExtractTimestampUtc(profileDoc);
        var tsRaw = ExtractRawTs(profileDoc);

        var commandDoc = profileDoc.TryGetValue("command", out var commandValue) && commandValue.BsonType == BsonType.Document
            ? commandValue.AsBsonDocument
            : new BsonDocument();
        var queryText = commandDoc.ElementCount > 0
            ? commandDoc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.Shell })
            : profileDoc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.Shell });

        var commandName = ExtractCommandName(profileDoc, commandDoc);
        var success = !profileDoc.Contains("errMsg") && !profileDoc.Contains("exception");
        var errorMessage = profileDoc.TryGetValue("errMsg", out var errMsg) && errMsg.IsString
            ? errMsg.AsString
            : profileDoc.TryGetValue("exception", out var exception) && exception.IsString
                ? exception.AsString
                : string.Empty;

        var client = profileDoc.TryGetValue("client", out var clientValue) && clientValue.IsString
            ? clientValue.AsString
            : "-";

        return new MongoSystemProfileEntry
        {
            EventKey = BuildProfileEventKey(profileDoc),
            TimestampUtc = timestampUtc,
            TsRaw = tsRaw,
            AppName = profileDoc.TryGetValue("appName", out var appNameValue) && appNameValue.IsString
                ? appNameValue.AsString
                : string.Empty,
            Client = client,
            CommandDocument = queryText,
            DocsExamined = TryToLong(profileDoc, "docsExamined"),
            NReturned = TryToLong(profileDoc, "nreturned"),
            Op = profileDoc.TryGetValue("op", out var opValue) && opValue.IsString ? opValue.AsString : string.Empty,
            CommandName = commandName,
            ServerEndpoint = client,
            DurationMs = TryToDouble(profileDoc, "millis"),
            Success = success,
            ErrorMessage = errorMessage
        };
    }

    private static DateTimeOffset? ExtractTimestampUtc(BsonDocument profileDoc)
    {
        if (!profileDoc.TryGetValue("ts", out var tsValue))
            return null;

        return tsValue.BsonType switch
        {
            BsonType.Timestamp => DateTimeOffset.FromUnixTimeSeconds(tsValue.AsBsonTimestamp.Timestamp),
            BsonType.DateTime => DateTime.SpecifyKind(tsValue.ToUniversalTime(), DateTimeKind.Utc),
            _ => null
        };
    }

    private static string ExtractRawTs(BsonDocument profileDoc)
    {
        if (!profileDoc.TryGetValue("ts", out var tsValue))
            return "-";

        return tsValue.BsonType switch
        {
            BsonType.Timestamp => $"{tsValue.AsBsonTimestamp.Timestamp}:{tsValue.AsBsonTimestamp.Increment}",
            BsonType.DateTime => tsValue.ToUniversalTime().ToString("O"),
            _ => tsValue.ToString() ?? "-"
        };
    }

    private static FilterDefinition<BsonDocument> BuildCheckpointFilter(BsonValue? lastTimestamp)
    {
        if (lastTimestamp is null)
            return FilterDefinition<BsonDocument>.Empty;

        var builder = Builders<BsonDocument>.Filter;
        return builder.Gt("ts", lastTimestamp);
    }

    private static bool TryGetComparableTs(BsonDocument doc, out BsonValue tsValue)
    {
        tsValue = BsonNull.Value;
        if (!doc.TryGetValue("ts", out var ts))
            return false;

        if (ts.BsonType is BsonType.Timestamp or BsonType.DateTime)
        {
            tsValue = ts;
            return true;
        }

        return false;
    }

    private static bool ShouldSkipProfileDocument(BsonDocument profileDoc, string readerComment)
    {
        if (profileDoc.TryGetValue("ns", out var nsValue) &&
            nsValue.BsonType == BsonType.String &&
            nsValue.AsString.EndsWith(".system.profile", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (profileDoc.TryGetValue("command", out var commandValue) &&
            commandValue.BsonType == BsonType.Document)
        {
            var command = commandValue.AsBsonDocument;
            if (command.TryGetValue("comment", out var commentValue) &&
                commentValue.BsonType == BsonType.String &&
                string.Equals(commentValue.AsString, readerComment, StringComparison.Ordinal))
            {
                return true;
            }

            if (CommandTargetsProfileCollection(command, "find") ||
                CommandTargetsProfileCollection(command, "aggregate") ||
                CommandTargetsProfileCollection(command, "count"))
            {
                return true;
            }
        }

        if (profileDoc.TryGetValue("query", out var queryValue) &&
            queryValue.BsonType == BsonType.Document)
        {
            var query = queryValue.AsBsonDocument;
            if (CommandTargetsProfileCollection(query, "find") ||
                CommandTargetsProfileCollection(query, "aggregate") ||
                CommandTargetsProfileCollection(query, "count"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CommandTargetsProfileCollection(BsonDocument command, string commandName)
    {
        if (!command.TryGetValue(commandName, out var targetValue) || targetValue.BsonType != BsonType.String)
            return false;

        return string.Equals(targetValue.AsString, "system.profile", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProfileEventKey(BsonDocument profileDoc)
    {
        if (profileDoc.TryGetValue("_id", out var idValue))
            return idValue.ToString() ?? "<null-id>";

        if (TryExtractLsidKey(profileDoc, out var lsidKey))
        {
            if (profileDoc.TryGetValue("ts", out var tsValue) &&
                (tsValue.BsonType is BsonType.Timestamp or BsonType.DateTime))
            {
                return $"lsid:{lsidKey}|ts:{tsValue}";
            }

            return $"lsid:{lsidKey}";
        }

        if (profileDoc.TryGetValue("ts", out var fallbackTsValue) && fallbackTsValue.BsonType == BsonType.Timestamp)
        {
            var timestamp = fallbackTsValue.AsBsonTimestamp;
            return $"ts:{timestamp.Timestamp}:{timestamp.Increment}";
        }

        return profileDoc.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
    }

    private static bool TryExtractLsidKey(BsonDocument profileDoc, out string lsidKey)
    {
        lsidKey = string.Empty;

        if (profileDoc.TryGetValue("lsid", out var lsidValue) &&
            lsidValue.BsonType == BsonType.Document &&
            TryExtractLsidFromDocument(lsidValue.AsBsonDocument, out lsidKey))
        {
            return true;
        }

        if (profileDoc.TryGetValue("command", out var commandValue) &&
            commandValue.BsonType == BsonType.Document &&
            commandValue.AsBsonDocument.TryGetValue("lsid", out var commandLsidValue) &&
            commandLsidValue.BsonType == BsonType.Document &&
            TryExtractLsidFromDocument(commandLsidValue.AsBsonDocument, out lsidKey))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractLsidFromDocument(BsonDocument lsidDoc, out string lsidKey)
    {
        lsidKey = string.Empty;

        if (!lsidDoc.TryGetValue("id", out var idValue))
            return false;

        lsidKey = idValue.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
        return !string.IsNullOrWhiteSpace(lsidKey);
    }

    private static string ExtractCommandName(BsonDocument profileDoc, BsonDocument commandDoc)
    {
        if (commandDoc.ElementCount > 0)
        {
            foreach (var element in commandDoc)
            {
                if (!element.Name.StartsWith("$", StringComparison.Ordinal) &&
                    !string.Equals(element.Name, "lsid", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(element.Name, "comment", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(element.Name, "readConcern", StringComparison.OrdinalIgnoreCase))
                {
                    return element.Name;
                }
            }
        }

        if (profileDoc.TryGetValue("op", out var opValue) && opValue.IsString)
            return opValue.AsString;

        return "profile_event";
    }

    private static long? TryToLong(BsonDocument source, string key)
    {
        if (!source.TryGetValue(key, out var value))
            return null;

        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => (long)value.AsDouble,
            BsonType.Decimal128 => (long)value.AsDecimal128,
            _ => null
        };
    }

    private static double TryToDouble(BsonDocument source, string key)
    {
        if (!source.TryGetValue(key, out var value))
            return 0d;

        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => value.AsInt64,
            BsonType.Double => value.AsDouble,
            BsonType.Decimal128 => (double)value.AsDecimal128,
            _ => 0d
        };
    }
}
