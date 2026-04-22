using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Commands;

internal static partial class SampleCommands
{
    public static async Task<CommandResult> AggregateSummaryAsync(SampleContext context)
    {
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("total", new BsonDocument("$gte", 25))),
            new BsonDocument("$group", new BsonDocument
            {
                ["_id"] = "$status",
                ["count"] = new BsonDocument("$sum", 1),
                ["total"] = new BsonDocument("$sum", "$total"),
                ["average"] = new BsonDocument("$avg", "$total")
            }),
            new BsonDocument("$sort", new BsonDocument("total", -1))
        };
        var docs = await context.Orders.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return new DocumentsResult(docs);
    }

    public static async Task<CommandResult> AggregateLookupAsync(SampleContext context)
    {
        var pipeline = new[]
        {
            new BsonDocument("$lookup", new BsonDocument
            {
                ["from"] = "customers",
                ["localField"] = "customer",
                ["foreignField"] = "_id",
                ["as"] = "customerDetails"
            }),
            new BsonDocument("$unwind", new BsonDocument
            {
                ["path"] = "$customerDetails",
                ["preserveNullAndEmptyArrays"] = true
            }),
            new BsonDocument("$project", new BsonDocument
            {
                ["customer"] = 1,
                ["status"] = 1,
                ["total"] = 1,
                ["tier"] = "$customerDetails.tier"
            }),
            new BsonDocument("$limit", 10)
        };
        var docs = await context.Orders.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return new DocumentsResult(docs);
    }

    public static async Task<CommandResult> ExplainAggregateAsync(SampleContext context)
    {
        var command = new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["aggregate"] = context.Options.CollectionName,
                ["pipeline"] = new BsonArray
                {
                    new BsonDocument("$match", new BsonDocument("status", "open")),
                    new BsonDocument("$group", new BsonDocument
                    {
                        ["_id"] = "$customer",
                        ["total"] = new BsonDocument("$sum", "$total")
                    })
                },
                ["cursor"] = new BsonDocument()
            },
            ["verbosity"] = "executionStats"
        };
        return new DocumentResult(await context.Database.RunCommandAsync<BsonDocument>(command));
    }
}
