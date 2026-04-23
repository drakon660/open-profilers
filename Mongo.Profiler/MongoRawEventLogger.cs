using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Events;

namespace Mongo.Profiler;

internal sealed class MongoRawEventLogger
{
    private static readonly JsonWriterSettings BsonJsonSettings = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _rawLogsDirectory;

    private MongoRawEventLogger(string rawLogsDirectory)
    {
        _rawLogsDirectory = rawLogsDirectory;
    }

    public static MongoRawEventLogger? Create(MongoProfilerRawEventOptions options)
    {
        if (!options.Enabled)
            return null;

        try
        {
            var directory = string.IsNullOrWhiteSpace(options.DestinationDirectory)
                ? GetDefaultRawLogsDirectory()
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.DestinationDirectory));

            Directory.CreateDirectory(directory);
            return new MongoRawEventLogger(directory);
        }
        catch
        {
            return null;
        }
    }

    public void DumpCommandStarted(CommandStartedEvent commandStartedEvent)
    {
        var payload = CreatePayload(commandStartedEvent);
        payload["DatabaseNamespace"] = commandStartedEvent.DatabaseNamespace?.ToString();
        payload["ConnectionId"] = ObjectToNode(commandStartedEvent.ConnectionId);
        payload["ConnectionIdText"] = commandStartedEvent.ConnectionId?.ToString();
        payload["ServerEndpoint"] = commandStartedEvent.ConnectionId?.ServerId?.EndPoint?.ToString();
        payload["Command"] = BsonToNode(commandStartedEvent.Command);

        Write(commandStartedEvent.RequestId.ToString(), nameof(CommandStartedEvent), payload);
    }

    public void DumpCommandSucceeded(CommandSucceededEvent commandSucceededEvent)
    {
        var payload = CreatePayload(commandSucceededEvent);
        payload["DurationMs"] = commandSucceededEvent.Duration.TotalMilliseconds;
        payload["ConnectionId"] = ObjectToNode(commandSucceededEvent.ConnectionId);
        payload["ConnectionIdText"] = commandSucceededEvent.ConnectionId?.ToString();
        payload["ServerEndpoint"] = commandSucceededEvent.ConnectionId?.ServerId?.EndPoint?.ToString();
        payload["Reply"] = BsonToNode(commandSucceededEvent.Reply);

        Write(commandSucceededEvent.RequestId.ToString(), nameof(CommandSucceededEvent), payload);
    }

    public void DumpCommandFailed(CommandFailedEvent commandFailedEvent)
    {
        var payload = CreatePayload(commandFailedEvent);
        payload["DurationMs"] = commandFailedEvent.Duration.TotalMilliseconds;
        payload["ConnectionId"] = ObjectToNode(commandFailedEvent.ConnectionId);
        payload["ConnectionIdText"] = commandFailedEvent.ConnectionId?.ToString();
        payload["ServerEndpoint"] = commandFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString();
        payload["Failure"] = ObjectToNode(commandFailedEvent.Failure);
        payload["FailureType"] = commandFailedEvent.Failure?.GetType().FullName;
        payload["FailureMessage"] = commandFailedEvent.Failure?.Message;
        payload["FailureStackTrace"] = commandFailedEvent.Failure?.StackTrace;

        Write(commandFailedEvent.RequestId.ToString(), nameof(CommandFailedEvent), payload);
    }

    public void DumpClusterDescriptionChanged(ClusterDescriptionChangedEvent changedEvent)
    {
        var payload = CreatePayload(changedEvent);
        payload["OldDescription"] = DescribeCluster(changedEvent.OldDescription);
        payload["NewDescription"] = DescribeCluster(changedEvent.NewDescription);

        Write("cluster", nameof(ClusterDescriptionChangedEvent), payload);
    }

    public void DumpServerHeartbeatFailed(ServerHeartbeatFailedEvent heartbeatFailedEvent)
    {
        var endpoint = heartbeatFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
        var payload = CreatePayload(heartbeatFailedEvent);
        payload["Endpoint"] = endpoint;
        payload["ConnectionId"] = ObjectToNode(heartbeatFailedEvent.ConnectionId);
        payload["ConnectionIdText"] = heartbeatFailedEvent.ConnectionId?.ToString();
        payload["Exception"] = ObjectToNode(heartbeatFailedEvent.Exception);
        payload["FailureType"] = heartbeatFailedEvent.Exception?.GetType().FullName;
        payload["FailureMessage"] = heartbeatFailedEvent.Exception?.Message;
        payload["FailureStackTrace"] = heartbeatFailedEvent.Exception?.StackTrace;

        Write(SanitizeIdentifier(endpoint, "heartbeat"), nameof(ServerHeartbeatFailedEvent), payload);
    }

    public void DumpServerHeartbeatSucceeded(ServerHeartbeatSucceededEvent heartbeatSucceededEvent)
    {
        var endpoint = heartbeatSucceededEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
        var payload = CreatePayload(heartbeatSucceededEvent);
        payload["Endpoint"] = endpoint;
        payload["ConnectionId"] = ObjectToNode(heartbeatSucceededEvent.ConnectionId);
        payload["ConnectionIdText"] = heartbeatSucceededEvent.ConnectionId?.ToString();
        payload["DurationMs"] = heartbeatSucceededEvent.Duration.TotalMilliseconds;
        payload["Reply"] = BsonToNode(heartbeatSucceededEvent.Reply);

        Write(SanitizeIdentifier(endpoint, "heartbeat"), nameof(ServerHeartbeatSucceededEvent), payload);
    }

    public void DumpConnectionOpeningFailed(ConnectionOpeningFailedEvent connectionOpeningFailedEvent)
    {
        var endpoint = connectionOpeningFailedEvent.ConnectionId?.ServerId?.EndPoint?.ToString() ?? string.Empty;
        var payload = CreatePayload(connectionOpeningFailedEvent);
        payload["Endpoint"] = endpoint;
        payload["ConnectionId"] = ObjectToNode(connectionOpeningFailedEvent.ConnectionId);
        payload["ConnectionIdText"] = connectionOpeningFailedEvent.ConnectionId?.ToString();
        payload["Exception"] = ObjectToNode(connectionOpeningFailedEvent.Exception);
        payload["FailureType"] = connectionOpeningFailedEvent.Exception?.GetType().FullName;
        payload["FailureMessage"] = connectionOpeningFailedEvent.Exception?.Message;
        payload["FailureStackTrace"] = connectionOpeningFailedEvent.Exception?.StackTrace;

        Write(SanitizeIdentifier(endpoint, "connection"), nameof(ConnectionOpeningFailedEvent), payload);
    }

    private static JsonObject CreatePayload<TEvent>(TEvent eventData)
    {
        var payload = new JsonObject
        {
            ["EventType"] = typeof(TEvent).Name,
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };

        AddObjectProperties(payload, eventData, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        return payload;
    }

    private static JsonNode? DescribeCluster(ClusterDescription description)
    {
        var servers = new JsonArray();
        foreach (var server in description.Servers)
        {
            var serverNode = ObjectToNode(server) as JsonObject ?? new JsonObject();
            serverNode["Endpoint"] = server.EndPoint?.ToString();
            serverNode["Type"] = server.Type.ToString();
            serverNode["State"] = server.State.ToString();
            serverNode["HeartbeatException"] = ObjectToNode(server.HeartbeatException);
            serverNode["HeartbeatExceptionMessage"] = server.HeartbeatException?.Message;
            servers.Add(serverNode);
        }

        var descriptionNode = ObjectToNode(description) as JsonObject ?? new JsonObject();
        descriptionNode["State"] = description.State.ToString();
        descriptionNode["Type"] = description.Type.ToString();
        descriptionNode["Servers"] = servers;
        return descriptionNode;
    }

    private static JsonNode? BsonToNode(BsonDocument? document)
    {
        return BsonToNode((BsonValue?)document);
    }

    private static JsonNode? BsonToNode(BsonValue? value)
    {
        if (value is null || value.IsBsonNull)
            return null;

        try
        {
            var rawJson = value.ToJson(BsonJsonSettings);
            return JsonNode.Parse(rawJson);
        }
        catch
        {
            return value.ToString();
        }
    }

    private static JsonNode? ObjectToNode(object? value)
    {
        return ObjectToNode(value, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
    }

    private static JsonNode? ObjectToNode(object? value, HashSet<object> visited, int depth)
    {
        if (value is null)
            return null;

        if (depth >= 8)
            return value.ToString();

        if (value is BsonValue bsonValue)
            return BsonToNode(bsonValue);

        if (TryCreateSimpleNode(value, out var simpleNode))
            return simpleNode;

        if (!value.GetType().IsValueType && !visited.Add(value))
            return value.ToString();

        if (value is Exception exception)
            return ExceptionToNode(exception, visited, depth);

        if (value is IDictionary dictionary)
        {
            var dictionaryNode = new JsonObject();
            foreach (DictionaryEntry entry in dictionary)
                dictionaryNode[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] =
                    ObjectToNode(entry.Value, visited, depth + 1);

            return dictionaryNode;
        }

        if (value is IEnumerable enumerable and not string)
        {
            var array = new JsonArray();
            foreach (var item in enumerable)
                array.Add(ObjectToNode(item, visited, depth + 1));

            return array;
        }

        var node = new JsonObject
        {
            ["StringValue"] = value.ToString()
        };

        AddObjectProperties(node, value, visited, depth);
        return node;
    }

    private static JsonObject ExceptionToNode(Exception exception, HashSet<object> visited, int depth)
    {
        var node = new JsonObject
        {
            ["Type"] = exception.GetType().FullName,
            ["Message"] = exception.Message,
            ["StackTrace"] = exception.StackTrace,
            ["Source"] = exception.Source,
            ["HResult"] = exception.HResult
        };

        if (exception.InnerException is not null)
            node["InnerException"] = ObjectToNode(exception.InnerException, visited, depth + 1);

        if (exception.Data.Count > 0)
            node["Data"] = ObjectToNode(exception.Data, visited, depth + 1);

        AddObjectProperties(node, exception, visited, depth);
        return node;
    }

    private static void AddObjectProperties(JsonObject node, object? value, HashSet<object> visited, int depth)
    {
        if (value is null)
            return;

        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetMethod is null || property.GetMethod.GetParameters().Length > 0)
                continue;

            try
            {
                node[property.Name] = ObjectToNode(property.GetValue(value), visited, depth + 1);
            }
            catch (Exception ex)
            {
                node[property.Name] = $"<unavailable: {ex.GetType().Name}>";
            }
        }
    }

    private static bool TryCreateSimpleNode(object value, out JsonNode? node)
    {
        node = value switch
        {
            string stringValue => JsonValue.Create(stringValue),
            char charValue => JsonValue.Create(charValue.ToString()),
            bool boolValue => JsonValue.Create(boolValue),
            byte byteValue => JsonValue.Create(byteValue),
            sbyte sbyteValue => JsonValue.Create(sbyteValue),
            short shortValue => JsonValue.Create(shortValue),
            ushort ushortValue => JsonValue.Create(ushortValue),
            int intValue => JsonValue.Create(intValue),
            uint uintValue => JsonValue.Create(uintValue),
            long longValue => JsonValue.Create(longValue),
            ulong ulongValue => JsonValue.Create(ulongValue),
            float floatValue => JsonValue.Create(floatValue),
            double doubleValue => JsonValue.Create(doubleValue),
            decimal decimalValue => JsonValue.Create(decimalValue),
            DateTime dateTimeValue => JsonValue.Create(dateTimeValue.ToString("O", CultureInfo.InvariantCulture)),
            DateTimeOffset dateTimeOffsetValue => JsonValue.Create(dateTimeOffsetValue.ToString("O", CultureInfo.InvariantCulture)),
            Guid guidValue => JsonValue.Create(guidValue.ToString("D")),
            TimeSpan timeSpanValue => new JsonObject
            {
                ["Value"] = timeSpanValue.ToString("c", CultureInfo.InvariantCulture),
                ["Ticks"] = timeSpanValue.Ticks,
                ["TotalMilliseconds"] = timeSpanValue.TotalMilliseconds
            },
            Uri uriValue => JsonValue.Create(uriValue.ToString()),
            _ when value.GetType().IsEnum => JsonValue.Create(value.ToString()),
            _ => null
        };

        return node is not null;
    }

    private void Write(string identifier, string eventName, JsonObject payload)
    {
        if (string.IsNullOrEmpty(_rawLogsDirectory))
            return;

        try
        {
            var safeId = SanitizeIdentifier(identifier, "unknown");
            var safeEvent = SanitizeIdentifier(eventName, "event");
            var path = Path.Combine(_rawLogsDirectory, $"{safeId}_{safeEvent}.json");
            payload["RawLogDirectory"] = _rawLogsDirectory;
            payload["RawLogFilePath"] = path;
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

    private static string GetDefaultRawLogsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mongo.Profiler",
            "raw_logs");
    }
}
