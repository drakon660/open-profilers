namespace Mongo.Profiler;

public sealed class MongoProfilerOptions
{
    public string ApplicationName { get; set; } = string.Empty;
    public MongoProfilerIndexAdvisorOptions IndexAdvisor { get; set; } = new();
    public MongoProfilerRedactionOptions Redaction { get; set; } = new();
    public MongoProfilerRawEventOptions RawEvents { get; set; } = new();
}

public sealed class MongoProfilerRawEventOptions
{
    public bool Enabled { get; set; }
    public string? DestinationDirectory { get; set; }
}

public sealed class MongoProfilerIndexAdvisorOptions
{
    public bool Enabled { get; set; }
    public int SlowQueryThresholdMs { get; set; } = 500;
    public int MinDocsExaminedForWarning { get; set; } = 200;
    public int MaxAnalysesPerFingerprintPerMinute { get; set; } = 1;
    public int ExplainTimeoutMs { get; set; } = 1500;
}

public sealed class MongoProfilerRedactionOptions
{
    public int MaxStringLength { get; set; } = 256;

    public string[] SensitiveKeys { get; set; } =
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
