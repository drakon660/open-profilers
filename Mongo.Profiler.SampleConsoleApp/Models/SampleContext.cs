using Mongo.Profiler.SampleConsoleApp.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Models;

internal sealed record SampleContext(IMongoClient Client, SampleOptions Options, GrpcRelayManager Relay)
{
    public IMongoDatabase Database => Client.GetDatabase(Options.DatabaseName);
    public IMongoCollection<BsonDocument> Orders => Database.GetCollection<BsonDocument>(Options.CollectionName);
}
