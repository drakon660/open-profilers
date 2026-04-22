using Mongo.Profiler.SampleConsoleApp.Data;
using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static async Task<CommandResult> TransactionAsync(SampleContext context)
    {
        using var session = await context.Client.StartSessionAsync();
        session.StartTransaction();
        try
        {
            await context.Orders.InsertOneAsync(session, SampleData.CreateOrder(Random.Shared.Next(40_000, 99_999), temporary: true));
            await context.Orders.UpdateOneAsync(
                session,
                Builders<BsonDocument>.Filter.Eq("status", "open"),
                Builders<BsonDocument>.Update.Inc("transactionTouches", 1));
            await session.CommitTransactionAsync();
            return new TextResult("Transaction committed.");
        }
        catch
        {
            await session.AbortTransactionAsync();
            throw;
        }
    }

    public static async Task<CommandResult> ChangeStreamAsync(SampleContext context)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cursor = await context.Orders.WatchAsync(cancellationToken: cancellation.Token);
        await context.Orders.InsertOneAsync(SampleData.CreateOrder(Random.Shared.Next(50_000, 99_999), temporary: true), cancellationToken: cancellation.Token);

        while (await cursor.MoveNextAsync(cancellation.Token))
        {
            var change = cursor.Current.FirstOrDefault();
            if (change is not null)
                return new DocumentResult(change.BackingDocument);
        }

        return new TextResult("No change was observed before timeout.");
    }

    public static async Task<CommandResult> GridFsMetadataQueryAsync(SampleContext context)
    {
        var files = context.Database.GetCollection<BsonDocument>("fs.files");
        var docs = await files.Find(Builders<BsonDocument>.Filter.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("uploadDate"))
            .Limit(5)
            .ToListAsync();
        return new DocumentsResult(docs);
    }
}
