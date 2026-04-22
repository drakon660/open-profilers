using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Events;

namespace Mongo.Profiler;

internal static class MongoRawEventLogger
{
    private static readonly string RawLogsDirectory = InitializeRawLogsDirectory();
    private static readonly JsonWriterSettings BsonJsonSettings = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static void DumpCommandStarted(CommandStartedEvent commandStartedEvent)
    {
        var payload = new JsonObject
        {
            ["EventType"] = nameof(CommandStartedEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["RequestId"] = commandStartedEvent.RequestId,
            ["OperationId"] = commandStartedEvent.OperationId,
            ["CommandName"] = commandStartedEvent.CommandName,
            ["DatabaseNamespace"] = commandStartedEvent.DatabaseNamespace?.ToString(),
            ["ConnectionId"] = commandStartedEvent.ConnectionId?.ToString(),
            ["ServerEndpoint"] = commandStartedEvent.ConnectionId?.ServerId?.EndPoint?.ToString(),
            ["Command"] = BsonToNode(commandStartedEvent.Command)
        };

        Write(commandStartedEvent.RequestId.ToString(), nameof(CommandStartedEvent), payload);
    }

    public static void DumpCommandSucceeded(CommandSucceededEvent commandSucceededEvent)
    {
        var payload = new JsonObject
        {
            ["EventType"] = nameof(CommandSucceededEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["RequestId"] = commandSucceededEvent.RequestId,
            ["OperationId"] = commandSucceededEvent.OperationId,
            ["CommandName"] = commandSucceededEvent.CommandName,
            ["DurationMs"] = commandSucceededEvent.Duration.TotalMilliseconds,
            ["ConnectionId"] = commandSucceededEvent.ConnectionId?.ToString(),
            ["ServerEndpoint"] = commandSucceededEvent.ConnectionId?.ServerId?.EndPoint?.ToString(),
            ["Reply"] = BsonToNode(commandSucceededEvent.Reply)
        };

        Write(commandSucceededEvent.RequestId.ToString(), nameof(CommandSucceededEvent), payload);
    }

    public static void DumpCommandFailed(CommandFailedEvent commandFailedEvent)
    {
        var payload = new JsonObject
        {
            ["EventType"] = nameof(CommandFailedEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["RequestId"] = commandFailedEvent.RequestId,
            ["OperationId"] = commandFailedEvent.OperationId,
            ["CommandName"] = commandFailedEvent.CommandName,
            ["DurationMs"] = commandFailedEvent.Duration.TotalMilliseconds,
            ["ConnectionId"] = commandFailedEvent.ConnectionId?.ToString(),
            ["ServerEndpoint"] = commandFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString(),
            ["FailureType"] = commandFailedEvent.Failure?.GetType().FullName,
            ["FailureMessage"] = commandFailedEvent.Failure?.Message,
            ["FailureStackTrace"] = commandFailedEvent.Failure?.StackTrace
        };

        Write(commandFailedEvent.RequestId.ToString(), nameof(CommandFailedEvent), payload);
    }

    public static void DumpClusterDescriptionChanged(ClusterDescriptionChangedEvent changedEvent)
    {
        var payload = new JsonObject
        {
            ["EventType"] = nameof(ClusterDescriptionChangedEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["OldDescription"] = DescribeCluster(changedEvent.OldDescription),
            ["NewDescription"] = DescribeCluster(changedEvent.NewDescription)
        };

        Write("cluster", nameof(ClusterDescriptionChangedEvent), payload);
    }

    public static void DumpServerHeartbeatFailed(ServerHeartbeatFailedEvent heartbeatFailedEvent)
    {
        var endpoint = heartbeatFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
        var payload = new JsonObject
        {
            ["EventType"] = nameof(ServerHeartbeatFailedEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Endpoint"] = endpoint,
            ["ConnectionId"] = heartbeatFailedEvent.ConnectionId?.ToString(),
            ["FailureType"] = heartbeatFailedEvent.Exception?.GetType().FullName,
            ["FailureMessage"] = heartbeatFailedEvent.Exception?.Message,
            ["FailureStackTrace"] = heartbeatFailedEvent.Exception?.StackTrace
        };

        Write(SanitizeIdentifier(endpoint, "heartbeat"), nameof(ServerHeartbeatFailedEvent), payload);
    }

    public static void DumpServerHeartbeatSucceeded(ServerHeartbeatSucceededEvent heartbeatSucceededEvent)
    {
        var endpoint = heartbeatSucceededEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
        var payload = new JsonObject
        {
            ["EventType"] = nameof(ServerHeartbeatSucceededEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Endpoint"] = endpoint,
            ["ConnectionId"] = heartbeatSucceededEvent.ConnectionId?.ToString(),
            ["DurationMs"] = heartbeatSucceededEvent.Duration.TotalMilliseconds
        };

        Write(SanitizeIdentifier(endpoint, "heartbeat"), nameof(ServerHeartbeatSucceededEvent), payload);
    }

    public static void DumpConnectionOpeningFailed(ConnectionOpeningFailedEvent connectionOpeningFailedEvent)
    {
        var endpoint = connectionOpeningFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
        var payload = new JsonObject
        {
            ["EventType"] = nameof(ConnectionOpeningFailedEvent),
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["Endpoint"] = endpoint,
            ["ConnectionId"] = connectionOpeningFailedEvent.ConnectionId?.ToString(),
            ["FailureType"] = connectionOpeningFailedEvent.Exception?.GetType().FullName,
            ["FailureMessage"] = connectionOpeningFailedEvent.Exception?.Message,
            ["FailureStackTrace"] = connectionOpeningFailedEvent.Exception?.StackTrace
        };

        Write(SanitizeIdentifier(endpoint, "connection"), nameof(ConnectionOpeningFailedEvent), payload);
    }

    private static JsonNode? DescribeCluster(ClusterDescription description)
    {
        var servers = new JsonArray();
        foreach (var server in description.Servers)
        {
            servers.Add(new JsonObject
            {
                ["Endpoint"] = server.EndPoint?.ToString(),
                ["Type"] = server.Type.ToString(),
                ["State"] = server.State.ToString(),
                ["HeartbeatException"] = server.HeartbeatException?.Message
            });
        }

        return new JsonObject
        {
            ["State"] = description.State.ToString(),
            ["Type"] = description.Type.ToString(),
            ["Servers"] = servers
        };
    }

    private static JsonNode? BsonToNode(BsonDocument? document)
    {
        if (document is null)
            return null;

        try
        {
            var rawJson = document.ToJson(BsonJsonSettings);
            return JsonNode.Parse(rawJson);
        }
        catch
        {
            return null;
        }
    }

    private static void Write(string identifier, string eventName, JsonObject payload)
    {
        if (string.IsNullOrEmpty(RawLogsDirectory))
            return;

        try
        {
            var safeId = SanitizeIdentifier(identifier, "unknown");
            var safeEvent = SanitizeIdentifier(eventName, "event");
            var path = Path.Combine(RawLogsDirectory, $"{safeId}_{safeEvent}.json");
            var json = payload.ToJsonString(SerializerOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Raw-event dumping must never affect application execution.
        }
    }

    private static string SanitizeIdentifier(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    private static string InitializeRawLogsDirectory()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Mongo.Profiler",
                "raw_logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            return string.Empty;
        }
    }
}
