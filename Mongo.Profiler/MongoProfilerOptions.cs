namespace Mongo.Profiler;

public sealed class MongoProfilerOptions
{
    public MongoProfilerIndexAdvisorOptions IndexAdvisor { get; init; } = new();
    public MongoProfilerRedactionOptions Redaction { get; init; } = new();
}

public sealed class MongoProfilerIndexAdvisorOptions
{
    public bool Enabled { get; init; }
    public int SlowQueryThresholdMs { get; init; } = 500;
    public int MinDocsExaminedForWarning { get; init; } = 200;
    public int MaxAnalysesPerFingerprintPerMinute { get; init; } = 1;
    public int ExplainTimeoutMs { get; init; } = 1500;
}

public sealed class MongoProfilerRedactionOptions
{
    public int MaxStringLength { get; init; } = 256;

    public IReadOnlyCollection<string> SensitiveKeys { get; init; } =
    [
        "password",
        "passwd",
        "pwd",
        "token",
        "access_token",
        "refresh_token",
        "secret",
        "apiKey",
        "api_key",
        "authorization",
        "cookie"
    ];
}
