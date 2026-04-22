using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static async Task<CommandResult> FindFilteredAsync(SampleContext context)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("status", "open"),
            Builders<BsonDocument>.Filter.Gte("total", 100));
        var docs = await context.Orders.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
            .Limit(10)
            .ToListAsync();
        return new DocumentsResult(docs);
    }

    public static async Task<CommandResult> FindProjectionAsync(SampleContext context)
    {
        var docs = await context.Orders.Find(Builders<BsonDocument>.Filter.Empty)
            .Project(Builders<BsonDocument>.Projection.Include("customer").Include("status").Include("total").Exclude("_id"))
            .Limit(10)
            .ToListAsync();
        return new DocumentsResult(docs);
    }

    public static async Task<CommandResult> FindOneAndUpdateAsync(SampleContext context)
    {
        var result = await context.Orders.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("status", "open"),
            Builders<BsonDocument>.Update.Inc("touches", 1).Set("lastTouchedAt", DateTime.UtcNow),
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After });
        return new DocumentResult(result ?? new BsonDocument("matched", false));
    }

    public static async Task<CommandResult> CountDocumentsAsync(SampleContext context)
    {
        var count = await context.Orders.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("status", "open"));
        return new TextResult($"Open orders: {count}");
    }

    public static async Task<CommandResult> EstimatedCountAsync(SampleContext context)
    {
        var count = await context.Orders.EstimatedDocumentCountAsync();
        return new TextResult($"Estimated orders: {count}");
    }

    public static async Task<CommandResult> DistinctAsync(SampleContext context)
    {
        var cursor = await context.Orders.DistinctAsync<string>("customer", FilterDefinition<BsonDocument>.Empty);
        return new TextResult(string.Join(", ", await cursor.ToListAsync()));
    }
}
