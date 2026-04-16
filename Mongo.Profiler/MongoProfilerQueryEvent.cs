namespace Mongo.Profiler;

public sealed class MongoProfilerQueryEvent
{
    public string SchemaVersion { get; init; } = "preview";
    public string EventId { get; init; } = Guid.NewGuid().ToString("n");
    public long UnixTimeMs { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public string ApplicationName { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = string.Empty;
    public string CollectionName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string Query { get; init; } = string.Empty;

    public double DurationMs { get; init; }
    public int? ResultCount { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public string RequestId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string ServerEndpoint { get; init; } = string.Empty;
    public string OperationId { get; init; } = string.Empty;
    public int? ErrorCode { get; init; }
    public string ErrorCodeName { get; init; } = string.Empty;
    public IReadOnlyList<string> ErrorLabels { get; init; } = [];
    public long? CursorId { get; init; }
    public int? ReplySizeBytes { get; init; }
    public int? CommandSizeBytes { get; init; }
    public string QueryFingerprint { get; init; } = string.Empty;
    public string ReadPreference { get; init; } = string.Empty;
    public string ReadConcern { get; init; } = string.Empty;
    public string WriteConcern { get; init; } = string.Empty;
    public int? MaxTimeMs { get; init; }
    public bool? AllowDiskUse { get; init; }
    public string IndexAdviceStatus { get; init; } = string.Empty;
    public string IndexAdviceReason { get; init; } = string.Empty;
    public long? ExplainDocsExamined { get; init; }
    public long? ExplainKeysExamined { get; init; }
    public long? ExplainNReturned { get; init; }
    public string WinningPlanSummary { get; init; } = string.Empty;
    public string ExecutionPlanXml { get; init; } = string.Empty;
}
