using MongoDB.Bson.Serialization.Attributes;

namespace Mongo.Profiler.AspNet;

internal sealed class Order
{
    [BsonId]
    public MongoDB.Bson.ObjectId Id { get; set; }

    public string Customer { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
}
