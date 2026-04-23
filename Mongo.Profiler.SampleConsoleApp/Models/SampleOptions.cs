using System.Text.Json;

namespace Mongo.Profiler.SampleConsoleApp.Models;

internal sealed class SampleOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27018";
    public string DatabaseName { get; set; } = "profiler_samples";
    public string CollectionName { get; set; } = "orders";
    public int GrpcPort { get; set; } = 5179;
    public bool EnableIndexAdvisor { get; set; } = true;
    public int IndexAdvisorSlowQueryThresholdMs { get; set; } = 300;
    public int IndexAdvisorMinDocsExaminedForWarning { get; set; } = 200;
    public int IndexAdvisorMaxAnalysesPerFingerprintPerMinute { get; set; } = 1;
    public int IndexAdvisorExplainTimeoutMs { get; set; } = 1500;
    public int RedactionMaxStringLength { get; set; } = 256;
    public string RawEventLogsDirectory { get; set; } = "raw_logs";
    public int MongoServerSelectionTimeoutMs { get; set; } = 1500;
    public int MongoConnectTimeoutMs { get; set; } = 1500;
    public string[] RedactionSensitiveKeys { get; set; } =
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

    public static SampleOptions Load()
    {
        const string configPath = "appsettings.json";
        if (!File.Exists(configPath))
            return new SampleOptions();

        var json = File.ReadAllText(configPath);
        var options = JsonSerializer.Deserialize<SampleOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return options ?? new SampleOptions();
    }
}
