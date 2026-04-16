namespace Mongo.Profiler.Client;

public sealed class MongoProfilerRelayHostedServiceOptions
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 5179;
    public bool ListenOnAnyIp { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
}
