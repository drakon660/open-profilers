namespace Mongo.Profiler.SampleConsoleApp.Models;

internal sealed class SampleOptions
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27018";
    public string DatabaseName { get; set; } = "profiler_samples";
    public string CollectionName { get; set; } = "orders";
    public int GrpcPort { get; set; } = 5179;
    public int MongoServerSelectionTimeoutMs { get; set; } = 1500;
    public int MongoConnectTimeoutMs { get; set; } = 1500;
}
