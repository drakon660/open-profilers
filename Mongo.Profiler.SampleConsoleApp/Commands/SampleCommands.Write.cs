using Mongo.Profiler.SampleConsoleApp.Data;
using Mongo.Profiler.SampleConsoleApp.ConsoleUi;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static async Task<CommandResult> InsertOneAsync(SampleContext context)
    {
        var document = SampleData.CreateOrder(Random.Shared.Next(10_000, 99_999), temporary: true);
        await context.Orders.InsertOneAsync(document);
        return new DocumentResult(document);
    }

    public static async Task<CommandResult> InsertManyAsync(SampleContext context)
    {
        var documents = Enumerable.Range(0, 5)
            .Select(_ => SampleData.CreateOrder(Random.Shared.Next(10_000, 99_999), temporary: true))
            .ToArray();
        await context.Orders.InsertManyAsync(documents);
        return new DocumentsResult(documents);
    }

    public static async Task<CommandResult> ReplaceOneAsync(SampleContext context)
    {
        var replacement = SampleData.CreateOrder(Random.Shared.Next(20_000, 99_999), temporary: true);
        replacement["status"] = "replaced";
        var result = await context.Orders.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("temporary", true),
            replacement,
            new ReplaceOptions { IsUpsert = true });
        return WriteResultFormatter.ToDocument(result);
    }

    public static async Task<CommandResult> UpdateOneAsync(SampleContext context)
    {
        var result = await context.Orders.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("status", "open"),
            Builders<BsonDocument>.Update.Set("priority", "high").CurrentDate("updatedAt"));
        return WriteResultFormatter.ToDocument(result);
    }

    public static async Task<CommandResult> UpdateManyAsync(SampleContext context)
    {
        var result = await context.Orders.UpdateManyAsync(
            Builders<BsonDocument>.Filter.Lt("total", 100),
            Builders<BsonDocument>.Update.Set("segment", "small").CurrentDate("updatedAt"));
        return WriteResultFormatter.ToDocument(result);
    }

    public static async Task<CommandResult> DeleteOneAsync(SampleContext context)
    {
        var result = await context.Orders.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("temporary", true));
        return WriteResultFormatter.ToDocument(result);
    }

    public static async Task<CommandResult> DeleteManyAsync(SampleContext context)
    {
        var result = await context.Orders.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq("temporary", true));
        return WriteResultFormatter.ToDocument(result);
    }

    public static async Task<CommandResult> BulkWriteAsync(SampleContext context)
    {
        var insert = SampleData.CreateOrder(Random.Shared.Next(30_000, 99_999), temporary: true);
        var replace = SampleData.CreateOrder(Random.Shared.Next(30_000, 99_999), temporary: true);
        replace["status"] = "bulk-replaced";

        var result = await context.Orders.BulkWriteAsync(
        [
            new InsertOneModel<BsonDocument>(insert),
            new UpdateOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("status", "open"),
                Builders<BsonDocument>.Update.Inc("bulkTouches", 1)),
            new ReplaceOneModel<BsonDocument>(
                Builders<BsonDocument>.Filter.Eq("temporary", true),
                replace) { IsUpsert = true },
            new DeleteOneModel<BsonDocument>(Builders<BsonDocument>.Filter.Eq("status", "cancelled"))
        ]);

        return new DocumentResult(new BsonDocument
        {
            ["inserted"] = result.InsertedCount,
            ["matched"] = result.MatchedCount,
            ["modified"] = result.ModifiedCount,
            ["deleted"] = result.DeletedCount,
            ["upserts"] = new BsonArray(result.Upserts.Select(upsert => new BsonDocument
            {
                ["index"] = upsert.Index,
                ["id"] = upsert.Id
            }))
        });
    }
}
