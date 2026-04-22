using Mongo.Profiler.SampleConsoleApp.Data;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static async Task<CommandResult> SeedAsync(SampleContext context)
    {
        await SampleData.DropCollectionIfExistsAsync(context.Database, context.Options.CollectionName);
        await context.Database.CreateCollectionAsync(context.Options.CollectionName);
        await context.Orders.InsertManyAsync(Enumerable.Range(1, 40).Select(number => SampleData.CreateOrder(number)));
        await CreateIndexesAsync(context);

        var customers = context.Database.GetCollection<BsonDocument>("customers");
        await SampleData.DropCollectionIfExistsAsync(context.Database, "customers");
        await context.Database.CreateCollectionAsync("customers");
        await customers.InsertManyAsync(
        [
            new BsonDocument { ["_id"] = "alice", ["tier"] = "gold", ["region"] = "EU" },
            new BsonDocument { ["_id"] = "bruno", ["tier"] = "silver", ["region"] = "US" },
            new BsonDocument { ["_id"] = "carla", ["tier"] = "platinum", ["region"] = "APAC" },
            new BsonDocument { ["_id"] = "dina", ["tier"] = "bronze", ["region"] = "EU" }
        ]);

        return new TextResult($"Seeded 40 orders in {context.Options.DatabaseName}.{context.Options.CollectionName}.");
    }

    public static async Task<CommandResult> CreateCollectionAsync(SampleContext context)
    {
        var names = await SampleData.ListCollectionNamesAsync(context.Database);
        if (!names.Contains(context.Options.CollectionName, StringComparer.Ordinal))
            await context.Database.CreateCollectionAsync(context.Options.CollectionName);

        return new TextResult($"Collection ready: {context.Options.CollectionName}");
    }

    public static async Task<CommandResult> DropCollectionAsync(SampleContext context)
    {
        await context.Database.DropCollectionAsync(context.Options.CollectionName);
        return new TextResult($"Dropped collection: {context.Options.CollectionName}");
    }

    public static async Task<CommandResult> CreateIndexesAsync(SampleContext context)
    {
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("customer").Ascending("status"),
                new CreateIndexOptions { Name = "customer_status_idx" }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Descending("createdAt"),
                new CreateIndexOptions { Name = "created_at_desc_idx" }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("total"),
                new CreateIndexOptions { Name = "total_idx" })
        };

        var names = await context.Orders.Indexes.CreateManyAsync(models);
        return new JsonResult(new BsonDocument("created", new BsonArray(names)));
    }

    public static async Task<CommandResult> ListIndexesAsync(SampleContext context)
    {
        var indexes = await context.Orders.Indexes.ListAsync();
        return new DocumentsResult(await indexes.ToListAsync());
    }

    public static async Task<CommandResult> DropSampleIndexAsync(SampleContext context)
    {
        try
        {
            await context.Orders.Indexes.DropOneAsync("customer_status_idx");
            return new TextResult("Dropped customer_status_idx.");
        }
        catch (MongoCommandException exception) when (exception.CodeName is "IndexNotFound")
        {
            return new TextResult("customer_status_idx did not exist.");
        }
    }
}
