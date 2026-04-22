using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.SampleConsoleApp.Data;

internal static class SampleData
{
    public static async Task DropCollectionIfExistsAsync(IMongoDatabase database, string collectionName)
    {
        var names = await ListCollectionNamesAsync(database);
        if (names.Contains(collectionName, StringComparer.Ordinal))
            await database.DropCollectionAsync(collectionName);
    }

    public static async Task<HashSet<string>> ListCollectionNamesAsync(IMongoDatabase database)
    {
        using var cursor = await database.ListCollectionNamesAsync();
        return new HashSet<string>(await cursor.ToListAsync(), StringComparer.Ordinal);
    }

    public static BsonDocument CreateOrder(int number, bool temporary = false)
    {
        var customers = new[] { "alice", "bruno", "carla", "dina" };
        var statuses = new[] { "open", "paid", "shipped", "cancelled" };
        var customer = customers[number % customers.Length];
        var status = statuses[number % statuses.Length];
        var itemCount = number % 4 + 1;
        var total = Math.Round((number % 250 + 25) * 1.13, 2);

        return new BsonDocument
        {
            ["orderNumber"] = number,
            ["customer"] = customer,
            ["status"] = status,
            ["createdAt"] = DateTime.UtcNow.AddMinutes(-number),
            ["total"] = total,
            ["temporary"] = temporary,
            ["shipping"] = new BsonDocument
            {
                ["country"] = number % 2 == 0 ? "PL" : "US",
                ["city"] = number % 2 == 0 ? "Warsaw" : "Seattle"
            },
            ["items"] = new BsonArray(Enumerable.Range(1, itemCount).Select(index => new BsonDocument
            {
                ["sku"] = $"SKU-{number:D5}-{index}",
                ["quantity"] = index,
                ["price"] = Math.Round(total / itemCount, 2)
            })),
            ["payment"] = new BsonDocument
            {
                ["method"] = number % 2 == 0 ? "card" : "wire",
                ["token"] = $"sample-token-{number}"
            }
        };
    }
}
