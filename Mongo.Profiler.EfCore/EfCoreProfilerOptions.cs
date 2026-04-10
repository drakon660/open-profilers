namespace EFCore.Profiler;

public sealed class EfCoreProfilerOptions
{
    public bool Enabled { get; set; } = true;
    public int MinDurationMs { get; set; }
    public bool CaptureParameters { get; set; }
    public int MaxSqlLength { get; set; } = 8_000;
    public bool EnableSafetyAlerts { get; set; } = true;
    public bool BlockUnsafeDmlWithoutWhere { get; set; }
    public int LargeResultWarningThreshold { get; set; } = 10_000;
    public int SlowQueryWarningMs { get; set; } = 1_000;
    public bool EnableNPlusOneAlert { get; set; } = true;
    public int NPlusOneMinRepeatedQueries { get; set; } = 3;
    public int NPlusOneWindowMs { get; set; } = 2_000;
    public bool EnableWarningRepeatAggregation { get; set; } = true;
    public int WarningRepeatWindowMs { get; set; } = 2_000;
    public int WarningRepeatEmitEvery { get; set; } = 5;
    public int WarningRepeatMaxTrackedKeys { get; set; } = 2_000;
    public bool EnableGeneratedSqlComplexityAlert { get; set; } = true;
    public int GeneratedSqlComplexityMinSignals { get; set; } = 3;
    public bool EnableSqlExecutionPlanCapture { get; set; }
    public bool SqlExecutionPlanCaptureOnlyWhenAlerted { get; set; } = true;
    public int SqlExecutionPlanCaptureMinDurationMs { get; set; } = 1_000;
    public bool IncludeExecutionPlanXml { get; set; }
    public int MaxExecutionPlanXmlLength { get; set; } = 64_000;
    public IReadOnlyCollection<string> SensitiveParameterNames { get; set; } =
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
