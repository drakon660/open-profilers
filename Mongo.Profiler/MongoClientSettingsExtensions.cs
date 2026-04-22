using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Events;

namespace Mongo.Profiler;

public static class MongoClientSettingsExtensions
{
    private static readonly JsonWriterSettings OriginalCommandJson = new()
    {
        Indent = true,
        OutputMode = JsonOutputMode.Shell
    };

    public static MongoClientSettings SubscribeToMongoQueries(
        this MongoClientSettings settings,
        ILogger? logger = null,
        IMongoProfilerEventSink? sink = null,
        MongoProfilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        options ??= new MongoProfilerOptions();
        var redaction = BuildRedactionConfig(options.Redaction);
        var applicationName = string.IsNullOrWhiteSpace(options.ApplicationName)
            ? Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty
            : options.ApplicationName;

        var existingClusterConfigurator = settings.ClusterConfigurator;
        var commandByRequestId = new ConcurrentDictionary<int, CommandEnvelope>();
        var indexAdvisor = MongoIndexAdvisor.Create(settings, options.IndexAdvisor, logger);
        var lastHeartbeatFailureByEndpoint = new ConcurrentDictionary<string, DateTimeOffset>();
        var heartbeatDebounce = TimeSpan.FromSeconds(10);
        var lastConnectionFailureByEndpoint = new ConcurrentDictionary<string, DateTimeOffset>();
        var connectionFailureDebounce = TimeSpan.FromSeconds(10);

        settings.ClusterConfigurator = clusterBuilder =>
        {
            existingClusterConfigurator?.Invoke(clusterBuilder);

            //PublishHeartbeatEvent(sink, applicationName, "profiler", "profiler attached", logger, success: true);

            clusterBuilder.Subscribe<CommandStartedEvent>(commandStartedEvent =>
            {
                if (ShouldSkipCommand(commandStartedEvent.CommandName, commandStartedEvent.Command))
                    return;

                var sanitizedCommand = RedactAndTruncate(commandStartedEvent.Command, redaction);
                var query = MongoCommandQueryBuilder.Build(commandStartedEvent.CommandName, sanitizedCommand);
                if (!string.IsNullOrWhiteSpace(query))
                {
                    commandByRequestId[commandStartedEvent.RequestId] = new CommandEnvelope(
                        query,
                        BuildQueryFingerprint(commandStartedEvent.CommandName, commandStartedEvent.Command),
                        GetDatabaseName(commandStartedEvent.Command),
                        GetCollectionName(commandStartedEvent.CommandName, commandStartedEvent.Command),
                        GetSessionId(commandStartedEvent.Command),
                        GetServerEndpoint(commandStartedEvent),
                        commandStartedEvent.OperationId?.ToString() ?? string.Empty,
                        GetStringifiedValue(commandStartedEvent.Command, "$readPreference", redaction.MaxStringLength),
                        GetStringifiedValue(commandStartedEvent.Command, "readConcern", redaction.MaxStringLength),
                        GetStringifiedValue(commandStartedEvent.Command, "writeConcern", redaction.MaxStringLength),
                        ConvertToIntOrNull(commandStartedEvent.Command, "maxTimeMS"),
                        ConvertToBoolOrNull(commandStartedEvent.Command, "allowDiskUse"),
                        CalculateBsonSize(commandStartedEvent.Command),
                        commandStartedEvent.Command.DeepClone().AsBsonDocument,
                        Activity.Current?.TraceId.ToString() ?? string.Empty,
                        Activity.Current?.SpanId.ToString() ?? string.Empty);
                }
            });

            clusterBuilder.Subscribe<CommandSucceededEvent>(commandSucceededEvent =>
            {
                if (!commandByRequestId.TryRemove(commandSucceededEvent.RequestId, out var commandEnvelope))
                    return;

                WriteQueryLog(logger, commandEnvelope.Query, commandSucceededEvent.CommandName, commandSucceededEvent.Duration, null);
                var outcome = ExtractSucceededOutcome(commandSucceededEvent.CommandName, commandSucceededEvent.Reply);
                var advice = indexAdvisor?.Analyze(commandEnvelope, commandSucceededEvent.CommandName, commandSucceededEvent.Duration)
                             ?? IndexAdvice.None;
                PublishEvent(sink, applicationName, commandEnvelope, commandSucceededEvent.CommandName, commandSucceededEvent.RequestId,
                    commandSucceededEvent.Duration, true, null, outcome, advice);
            });

            clusterBuilder.Subscribe<CommandFailedEvent>(commandFailedEvent =>
            {
                if (!commandByRequestId.TryRemove(commandFailedEvent.RequestId, out var commandEnvelope))
                    return;

                var error = commandFailedEvent.Failure?.Message;
                WriteQueryLog(logger, commandEnvelope.Query, commandFailedEvent.CommandName, commandFailedEvent.Duration, error);
                var outcome = ExtractFailedOutcome(commandFailedEvent.Failure);
                var advice = IndexAdvice.None;
                PublishEvent(sink, applicationName, commandEnvelope, commandFailedEvent.CommandName, commandFailedEvent.RequestId,
                    commandFailedEvent.Duration, false, error, outcome, advice);
            });
            
            clusterBuilder.Subscribe<ClusterDescriptionChangedEvent>(changedEvent =>
            {
                var oldDescription = changedEvent.OldDescription;
                var newDescription = changedEvent.NewDescription;
                if (oldDescription.State == newDescription.State && oldDescription.Type == newDescription.Type)
                    return;

                PublishConnectivityEvent(sink, applicationName, oldDescription, newDescription, logger);
            });
            
            clusterBuilder.Subscribe<ServerHeartbeatFailedEvent>(heartbeatFailedEvent =>
            {
                var endpoint = heartbeatFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
                var now = DateTimeOffset.UtcNow;
                if (lastHeartbeatFailureByEndpoint.TryGetValue(endpoint, out var last) && now - last < heartbeatDebounce)
                    return;

                lastHeartbeatFailureByEndpoint[endpoint] = now;
                PublishHeartbeatEvent(
                    sink,
                    applicationName,
                    endpoint,
                    heartbeatFailedEvent.Exception?.Message ?? "heartbeat failed",
                    logger,
                    success: false);
            });

            clusterBuilder.Subscribe<ServerHeartbeatSucceededEvent>(heartbeatSucceededEvent =>
            {
                var endpoint = heartbeatSucceededEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
                if (!lastHeartbeatFailureByEndpoint.TryRemove(endpoint, out _))
                    return;

                PublishHeartbeatEvent(sink, applicationName, endpoint, null, logger, success: true);
            });

            clusterBuilder.Subscribe<ConnectionOpeningFailedEvent>(connectionOpeningFailedEvent =>
            {
                var endpoint = connectionOpeningFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
                var now = DateTimeOffset.UtcNow;
                if (lastConnectionFailureByEndpoint.TryGetValue(endpoint, out var last) && now - last < connectionFailureDebounce)
                    return;

                lastConnectionFailureByEndpoint[endpoint] = now;
                PublishConnectionOpeningFailedEvent(
                    sink,
                    applicationName,
                    endpoint,
                    connectionOpeningFailedEvent.Exception?.Message ?? "connection opening failed",
                    logger);
            });
        };

        return settings;
    }

    private static void PublishEvent(
        IMongoProfilerEventSink? sink,
        string applicationName,
        CommandEnvelope commandEnvelope,
        string commandName,
        int requestId,
        TimeSpan duration,
        bool success,
        string? errorMessage,
        CommandOutcome outcome,
        IndexAdvice advice)
    {
        if (sink is null)
            return;

        try
        {
            sink.Publish(new MongoProfilerQueryEvent
            {
                ApplicationName = applicationName,
                CommandName = commandName,
                DatabaseName = commandEnvelope.DatabaseName,
                CollectionName = commandEnvelope.CollectionName,
                SessionId = commandEnvelope.SessionId,
                Query = commandEnvelope.Query,
                DurationMs = duration.TotalMilliseconds,
                ResultCount = outcome.ResultCount,
                Success = success,
                ErrorMessage = errorMessage,
                RequestId = requestId.ToString(),
                ServerEndpoint = commandEnvelope.ServerEndpoint,
                OperationId = commandEnvelope.OperationId,
                ErrorCode = outcome.ErrorCode,
                ErrorCodeName = outcome.ErrorCodeName,
                ErrorLabels = outcome.ErrorLabels,
                CursorId = outcome.CursorId,
                ReplySizeBytes = outcome.ReplySizeBytes,
                CommandSizeBytes = commandEnvelope.CommandSizeBytes,
                QueryFingerprint = commandEnvelope.QueryFingerprint,
                ReadPreference = commandEnvelope.ReadPreference,
                ReadConcern = commandEnvelope.ReadConcern,
                WriteConcern = commandEnvelope.WriteConcern,
                MaxTimeMs = commandEnvelope.MaxTimeMs,
                AllowDiskUse = commandEnvelope.AllowDiskUse,
                IndexAdviceStatus = advice.Status,
                IndexAdviceReason = advice.Reason,
                ExplainDocsExamined = advice.DocsExamined,
                ExplainKeysExamined = advice.KeysExamined,
                ExplainNReturned = advice.NReturned,
                WinningPlanSummary = advice.WinningPlanSummary,
                TraceId = commandEnvelope.TraceId,
                SpanId = commandEnvelope.SpanId,
                OriginalCommand = SerializeOriginalCommand(commandEnvelope.OriginalCommand)
            });
        }
        catch
        {
            // Event publishing must never affect application execution.
        }
    }

    private static void PublishConnectivityEvent(
        IMongoProfilerEventSink? sink,
        string applicationName,
        ClusterDescription oldDescription,
        ClusterDescription newDescription,
        ILogger? logger)
    {
        var endpoints = string.Join(",", newDescription.Servers.Select(server => server.EndPoint?.ToString() ?? string.Empty));
        var heartbeatError = newDescription.Servers
            .Select(server => server.HeartbeatException?.Message)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

        var success = newDescription.State == ClusterState.Connected;
        var transition = $"cluster {oldDescription.State} -> {newDescription.State} ({newDescription.Type})";
        var errorMessage = success ? null : heartbeatError ?? "cluster disconnected";

        if (logger is not null)
        {
            if (success)
                logger.LogInformation("Mongo connectivity change: {Transition}.", transition);
            else
                logger.LogWarning("Mongo connectivity change: {Transition}. {Error}", transition, errorMessage);
        }

        if (sink is null)
            return;

        try
        {
            sink.Publish(new MongoProfilerQueryEvent
            {
                ApplicationName = applicationName,
                CommandName = "connectivity",
                DatabaseName = string.Empty,
                CollectionName = string.Empty,
                Query = transition,
                DurationMs = 0,
                Success = success,
                ErrorMessage = errorMessage,
                ServerEndpoint = endpoints,
                QueryFingerprint = "SYSTEM:CONNECTIVITY"
            });
        }
        catch
        {
            // Connectivity diagnostics must never affect application execution.
        }
    }

    private static void PublishConnectionOpeningFailedEvent(
        IMongoProfilerEventSink? sink,
        string applicationName,
        string endpoint,
        string error,
        ILogger? logger)
    {
        var message = $"connection opening failed for {endpoint}";

        logger?.LogWarning("Mongo {Message}. {Error}", message, error);

        if (sink is null)
            return;

        try
        {
            sink.Publish(new MongoProfilerQueryEvent
            {
                ApplicationName = applicationName,
                CommandName = "connectivity",
                DatabaseName = string.Empty,
                CollectionName = string.Empty,
                Query = message,
                DurationMs = 0,
                Success = false,
                ErrorMessage = error,
                ServerEndpoint = endpoint,
                QueryFingerprint = "SYSTEM:CONNECTIVITY"
            });
        }
        catch
        {
            // Connectivity diagnostics must never affect application execution.
        }
    }

    private static void PublishHeartbeatEvent(
        IMongoProfilerEventSink? sink,
        string applicationName,
        string endpoint,
        string? error,
        ILogger? logger,
        bool success)
    {
        var message = success
            ? $"heartbeat recovered for {endpoint}"
            : $"heartbeat failed for {endpoint}";

        if (logger is not null)
        {
            if (success)
                logger.LogInformation("Mongo {Message}.", message);
            else
                logger.LogWarning("Mongo {Message}. {Error}", message, error ?? string.Empty);
        }

        if (sink is null)
            return;

        try
        {
            sink.Publish(new MongoProfilerQueryEvent
            {
                ApplicationName = applicationName,
                CommandName = "connectivity",
                DatabaseName = string.Empty,
                CollectionName = string.Empty,
                Query = message,
                DurationMs = 0,
                Success = success,
                ErrorMessage = success ? null : error,
                ServerEndpoint = endpoint,
                QueryFingerprint = "SYSTEM:CONNECTIVITY"
            });
        }
        catch
        {
            // Connectivity diagnostics must never affect application execution.
        }
    }

    private static void WriteQueryLog(ILogger? logger, string query, string commandName, TimeSpan duration, string? error)
    {
        if (logger is not null)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                logger.LogInformation(
                    "Mongo query ({CommandName}) took {DurationMs} ms:{NewLine}{Query}",
                    commandName,
                    duration.TotalMilliseconds,
                    Environment.NewLine,
                    query);
            }
            else
            {
                logger.LogWarning(
                    "Mongo query ({CommandName}) failed after {DurationMs} ms: {Error}{NewLine}{Query}",
                    commandName,
                    duration.TotalMilliseconds,
                    error,
                    Environment.NewLine,
                    query);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(error))
            Console.WriteLine($"Mongo query ({commandName}) took {duration.TotalMilliseconds} ms:{Environment.NewLine}{query}");
        else
            Console.WriteLine(
                $"Mongo query ({commandName}) failed after {duration.TotalMilliseconds} ms: {error}{Environment.NewLine}{query}");
    }

    private static string GetDatabaseName(BsonDocument command)
    {
        return command.TryGetValue("$db", out var database) && database.IsString ? database.AsString : string.Empty;
    }

    private static string GetCollectionName(string commandName, BsonDocument command)
    {
        if (!command.TryGetValue(commandName, out var collection) || !collection.IsString)
            return string.Empty;

        return collection.AsString;
    }

    private static string GetSessionId(BsonDocument command)
    {
        if (!command.TryGetValue("lsid", out var sessionValue))
            return string.Empty;

        return sessionValue.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.Shell });
    }

    private static bool ShouldSkipCommand(string commandName, BsonDocument command)
    {
        if (commandName is "hello" or "isMaster" or "buildInfo")
            return true;

        if (!command.TryGetValue("comment", out var comment) || comment.BsonType != BsonType.String)
            return false;

        return string.Equals(comment.AsString, MongoIndexAdvisor.AdvisorComment, StringComparison.Ordinal);
    }

    private static CommandOutcome ExtractSucceededOutcome(string commandName, BsonDocument reply)
    {
        return new CommandOutcome(
            ExtractResultCount(commandName, reply),
            ExtractCursorId(reply),
            CalculateBsonSize(reply),
            ConvertToIntOrNull(reply, "code"),
            GetCodeName(reply),
            ExtractErrorLabels(reply));
    }

    private static CommandOutcome ExtractFailedOutcome(Exception? failure)
    {
        if (failure is null)
            return CommandOutcome.Empty;

        var responseContext = failure switch
        {
            MongoCommandException commandException => commandException.Result,
            MongoWriteException writeException => writeException.WriteConcernError?.Details,
            _ => null
        };

        var code = failure switch
        {
            MongoCommandException commandException => commandException.Code,
            MongoWriteException writeException => writeException.WriteError?.Code,
            _ => null
        } ?? (responseContext is null ? null : ConvertToIntOrNull(responseContext, "code"));

        var codeName = failure switch
        {
            MongoCommandException commandException => commandException.CodeName ?? string.Empty,
            MongoWriteException writeException => writeException.WriteError?.Category.ToString() ?? string.Empty,
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(codeName) && responseContext is not null)
            codeName = GetCodeName(responseContext);

        var exceptionLabels = failure is MongoException mongoException
            ? mongoException.ErrorLabels?.ToArray() ?? []
            : [];
        var responseLabels = responseContext is null ? [] : ExtractErrorLabels(responseContext);
        var labels = exceptionLabels
            .Concat(responseLabels)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new CommandOutcome(null, null, null, code, codeName, labels);
    }

    private static int? ExtractResultCount(string commandName, BsonDocument reply)
    {
        if ((commandName == "find" || commandName == "aggregate") &&
            reply.TryGetValue("cursor", out var cursorValue) &&
            cursorValue.BsonType == BsonType.Document)
        {
            var cursor = cursorValue.AsBsonDocument;
            if (cursor.TryGetValue("firstBatch", out var firstBatch) && firstBatch.BsonType == BsonType.Array)
                return firstBatch.AsBsonArray.Count;

            if (cursor.TryGetValue("nextBatch", out var nextBatch) && nextBatch.BsonType == BsonType.Array)
                return nextBatch.AsBsonArray.Count;
        }

        if (reply.TryGetValue("n", out var nValue))
            return ConvertToIntOrNull(nValue);

        return null;
    }

    private static long? ExtractCursorId(BsonDocument reply)
    {
        if (!reply.TryGetValue("cursor", out var cursorValue) || cursorValue.BsonType != BsonType.Document)
            return null;

        var cursor = cursorValue.AsBsonDocument;
        if (!cursor.TryGetValue("id", out var idValue))
            return null;

        return idValue.BsonType switch
        {
            BsonType.Int64 => idValue.AsInt64,
            BsonType.Int32 => idValue.AsInt32,
            BsonType.Double => (long)idValue.AsDouble,
            BsonType.Decimal128 => (long)idValue.AsDecimal128,
            _ => null
        };
    }

    private static string[] ExtractErrorLabels(BsonDocument response)
    {
        if (!response.TryGetValue("errorLabels", out var labelsValue) || labelsValue.BsonType != BsonType.Array)
            return [];

        return labelsValue.AsBsonArray
            .Where(value => value.BsonType == BsonType.String)
            .Select(value => value.AsString)
            .ToArray();
    }

    private static string GetCodeName(BsonDocument response)
    {
        return response.TryGetValue("codeName", out var codeNameValue) && codeNameValue.BsonType == BsonType.String
            ? codeNameValue.AsString
            : string.Empty;
    }

    private static string GetStringifiedValue(BsonDocument source, string key, int maxStringLength)
    {
        if (!source.TryGetValue(key, out var value) || value.IsBsonNull)
            return string.Empty;

        if (value.BsonType == BsonType.String)
            return TruncateString(value.AsString, maxStringLength);

        return value.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.Shell });
    }

    private static int? ConvertToIntOrNull(BsonDocument source, string key)
    {
        return source.TryGetValue(key, out var value) ? ConvertToIntOrNull(value) : null;
    }

    private static bool? ConvertToBoolOrNull(BsonDocument source, string key)
    {
        if (!source.TryGetValue(key, out var value))
            return null;

        return value.BsonType switch
        {
            BsonType.Boolean => value.AsBoolean,
            BsonType.Int32 => value.AsInt32 != 0,
            BsonType.Int64 => value.AsInt64 != 0,
            _ => null
        };
    }

    private static int? ConvertToIntOrNull(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.Int32 => value.AsInt32,
            BsonType.Int64 => unchecked((int)value.AsInt64),
            BsonType.Double => (int)value.AsDouble,
            BsonType.Decimal128 => (int)value.AsDecimal128,
            _ => null
        };
    }

    private static int? CalculateBsonSize(BsonDocument document)
    {
        try
        {
            return document.ToBson().Length;
        }
        catch
        {
            return null;
        }
    }

    private static string GetServerEndpoint(CommandStartedEvent commandStartedEvent)
    {
        return commandStartedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
    }

    private static string BuildQueryFingerprint(string commandName, BsonDocument command)
    {
        try
        {
            var normalizedCommand = NormalizeShape(command);
            var normalizedJson = normalizedCommand.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson });
            var payload = $"{commandName}|{normalizedJson}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static BsonValue NormalizeShape(BsonValue value)
    {
        return value.BsonType switch
        {
            BsonType.Document => NormalizeDocumentShape(value.AsBsonDocument),
            BsonType.Array => new BsonArray(value.AsBsonArray.Select(NormalizeShape)),
            BsonType.String => "?s",
            BsonType.Int32 => "?n",
            BsonType.Int64 => "?n",
            BsonType.Double => "?n",
            BsonType.Decimal128 => "?n",
            BsonType.Boolean => "?b",
            BsonType.DateTime => "?d",
            BsonType.Timestamp => "?d",
            BsonType.ObjectId => "?id",
            BsonType.Null => BsonNull.Value,
            _ => $"?{value.BsonType}"
        };
    }

    private static BsonDocument NormalizeDocumentShape(BsonDocument document)
    {
        var normalized = new BsonDocument();
        foreach (var element in document.OrderBy(x => x.Name, StringComparer.Ordinal))
            normalized[element.Name] = NormalizeShape(element.Value);

        return normalized;
    }

    private static RedactionConfig BuildRedactionConfig(MongoProfilerRedactionOptions options)
    {
        var sensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in options.SensitiveKeys ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key))
                sensitiveKeys.Add(key.Trim());
        }

        return new RedactionConfig(Math.Max(16, options.MaxStringLength), sensitiveKeys);
    }

    private static BsonDocument RedactAndTruncate(BsonDocument command, RedactionConfig redactionConfig)
    {
        var redacted = new BsonDocument();
        foreach (var element in command)
        {
            if (redactionConfig.SensitiveKeys.Contains(element.Name))
            {
                redacted[element.Name] = "***REDACTED***";
                continue;
            }

            redacted[element.Name] = RedactAndTruncateValue(element.Value, redactionConfig);
        }

        return redacted;
    }

    private static BsonValue RedactAndTruncateValue(BsonValue value, RedactionConfig redactionConfig)
    {
        return value.BsonType switch
        {
            BsonType.Document => RedactAndTruncate(value.AsBsonDocument, redactionConfig),
            BsonType.Array => new BsonArray(value.AsBsonArray.Select(item => RedactAndTruncateValue(item, redactionConfig))),
            BsonType.String => TruncateString(value.AsString, redactionConfig.MaxStringLength),
            _ => value
        };
    }

    private static string SerializeOriginalCommand(BsonDocument command)
    {
        try
        {
            return command.ToJson(OriginalCommandJson);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TruncateString(string value, int maxStringLength)
    {
        return value.Length <= maxStringLength
            ? value
            : $"{value[..maxStringLength]}...[truncated]";
    }

    private sealed record CommandOutcome(
        int? ResultCount,
        long? CursorId,
        int? ReplySizeBytes,
        int? ErrorCode,
        string ErrorCodeName,
        string[] ErrorLabels)
    {
        public static readonly CommandOutcome Empty = new(null, null, null, null, string.Empty, []);
    }

    private sealed record RedactionConfig(int MaxStringLength, HashSet<string> SensitiveKeys);
}
