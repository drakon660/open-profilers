using Carter;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.AspNet;

public sealed class OrderModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orders", GetOrdersAsync);
        app.MapGet("/api/orders/{id}", GetOrderByIdAsync);
    }

    private static async Task<IResult> GetOrdersAsync(
        IMongoCollection<Order> orders,
        CancellationToken cancellationToken)
    {
        var result = await orders
            .Find(FilterDefinition<Order>.Empty)
            .SortByDescending(x => x.OrderedAt)
            .Limit(100)
            .ToListAsync(cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetOrderByIdAsync(
        string id,
        IMongoCollection<Order> orders,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out var objectId))
            return Results.BadRequest($"Invalid order id '{id}'.");

        var order = await orders
            .Find(x => x.Id == objectId)
            .FirstOrDefaultAsync(cancellationToken);

        return order is null ? Results.NotFound() : Results.Ok(order);
    }
}
