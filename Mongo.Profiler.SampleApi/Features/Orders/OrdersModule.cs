using Carter;
using Microsoft.EntityFrameworkCore;
using Mongo.Profiler;
using Mongo.Profiler.SampleApi.Data;
using System.Diagnostics;

namespace Mongo.Profiler.SampleApi.Features.Orders;

public sealed class OrdersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders");

        orders.MapGet("/", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var data = await dbContext.Orders
                .AsNoTracking()
                .OrderByDescending(order => order.CreatedUtc)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken);

            return Results.Ok(data);
        });

        orders.MapGet("/{id:int}", async (int id, OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var order = await dbContext.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

            return order is null ? Results.NotFound() : Results.Ok(ToResponse(order));
        });

        orders.MapGet("/{id:int}/joined-hierarchy", async (int id, OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var rows = await (
                from order in dbContext.Orders.AsNoTracking()
                where order.Id == id
                join orderItem in dbContext.OrderItems.AsNoTracking() on order.Id equals orderItem.OrderId into orderItemsJoin
                from orderItem in orderItemsJoin.DefaultIfEmpty()
                join product in dbContext.Products.AsNoTracking() on orderItem.ProductId equals product.Id into productsJoin
                from product in productsJoin.DefaultIfEmpty()
                join payment in dbContext.Payments.AsNoTracking() on order.Id equals payment.OrderId into paymentsJoin
                from payment in paymentsJoin.DefaultIfEmpty()
                join shipment in dbContext.Shipments.AsNoTracking() on order.Id equals shipment.OrderId into shipmentsJoin
                from shipment in shipmentsJoin.DefaultIfEmpty()
                join history in dbContext.OrderStatusHistory.AsNoTracking() on order.Id equals history.OrderId into statusHistoryJoin
                from history in statusHistoryJoin.DefaultIfEmpty()
                select new { order, orderItem, product, payment, shipment, history }
            ).ToListAsync(cancellationToken);

            var firstOrder = rows.FirstOrDefault()?.order;
            if (firstOrder is null)
                return Results.NotFound();

            var items = rows
                .Where(row => row.orderItem is not null)
                .GroupBy(row => row.orderItem!.Id)
                .Select(group =>
                {
                    var row = group.First();
                    return new OrderItemJoinedResponse(
                        row.orderItem!.Id,
                        row.orderItem.ProductId,
                        row.orderItem.Quantity,
                        row.orderItem.UnitPrice,
                        row.product is null
                            ? null
                            : new ProductMiniResponse(row.product.Id, row.product.Name, row.product.SKU, row.product.Price));
                })
                .ToList();

            var payments = rows
                .Where(row => row.payment is not null)
                .GroupBy(row => row.payment!.Id)
                .Select(group =>
                {
                    var payment = group.First().payment!;
                    return new PaymentResponse(payment.Id, payment.PaymentMethod, payment.Amount, payment.PaidUtc, payment.Status);
                })
                .ToList();

            var shipments = rows
                .Where(row => row.shipment is not null)
                .GroupBy(row => row.shipment!.Id)
                .Select(group =>
                {
                    var shipment = group.First().shipment!;
                    return new ShipmentResponse(
                        shipment.Id,
                        shipment.AddressLine1,
                        shipment.City,
                        shipment.PostalCode,
                        shipment.Country,
                        shipment.ShippedUtc,
                        shipment.Status);
                })
                .ToList();

            var statusHistory = rows
                .Where(row => row.history is not null)
                .GroupBy(row => row.history!.Id)
                .Select(group =>
                {
                    var history = group.First().history!;
                    return new OrderStatusHistoryResponse(
                        history.Id,
                        history.OldStatus,
                        history.NewStatus,
                        history.ChangedUtc,
                        history.ChangedBy);
                })
                .OrderBy(history => history.ChangedUtc)
                .ToList();

            return Results.Ok(new OrderHierarchyResponse(
                firstOrder.Id,
                firstOrder.CustomerName,
                firstOrder.TotalAmount,
                firstOrder.Status,
                firstOrder.CreatedUtc,
                items,
                payments,
                shipments,
                statusHistory));
        });

        orders.MapGet("/bad/n-plus-one", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            // Intentionally bad sample:
            // 1 query for orders + N lazy-load queries for Order.Items.
            var ordersSlice = await dbContext.Orders
                .OrderByDescending(order => order.CreatedUtc)
                .Take(20)
                .ToListAsync(cancellationToken);

            var result = new List<OrderNPlusOneItemCountResponse>(ordersSlice.Count);
            foreach (var order in ordersSlice)
            {
                var itemCount = order.Items.Count;
                result.Add(new OrderNPlusOneItemCountResponse(order.Id, order.CustomerName, itemCount));
            }

            return Results.Ok(new
            {
                message = "Intentional N+1 sample. One query for orders, one query per order to load Order.Items.",
                rows = result
            });
        });

        orders.MapGet("/bad/sql/select-star", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var rows = await dbContext.Orders
                .AsNoTracking()
                .Take(10)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "LINQ sample for over-fetching shape (replaces previous FromSqlRaw SELECT * demo).",
                rows
            });
        });

        orders.MapGet("/bad/sql/cartesian-join", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var rows = await (
                from order in dbContext.Orders.AsNoTracking()
                from product in dbContext.Products.AsNoTracking()
                select order)
                .AsNoTracking()
                .Take(50)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Intentional bad SQL sample for cartesian_join_risk.",
                rows = rows.Take(10),
                totalRowsMaterialized = rows.Count
            });
        });

        orders.MapGet("/bad/sql/leading-wildcard-like", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var rows = await dbContext.Orders
                .AsNoTracking()
                .Where(order => EF.Functions.Like(order.CustomerName, "%Retail%"))
                .Take(20)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Intentional bad SQL sample for leading_wildcard_like.",
                rows
            });
        });

        orders.MapGet("/bad/sql/non-sargable-predicate", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var rows = await dbContext.Orders
                .AsNoTracking()
                .Where(order => order.CustomerName.ToLower() == "Acme Retail".ToLower())
                .Take(20)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Intentional bad SQL sample for non_sargable_predicate.",
                rows
            });
        });

        orders.MapGet("/bad/sql/implicit-conversion-risk", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var rows = await dbContext.Orders
                .AsNoTracking()
                .Where(order => order.Id.ToString() == "1")
                .Take(20)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Intentional bad SQL sample for implicit_conversion_risk.",
                rows
            });
        });

        orders.MapPost("/bad/sql/missing-where-update", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var affectedRows = await dbContext.Orders
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(order => order.Status, order => order.Status),
                    cancellationToken);

            return Results.Ok(new
            {
                message = "Intentional bad LINQ sample for missing_where_on_dml (ExecuteUpdate without filter).",
                affectedRows
            });
        });

        orders.MapPost("/bad/sql/missing-where-delete", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            var deletedOrderItems = await dbContext.OrderItems
                .ExecuteDeleteAsync(cancellationToken);
            var deletedPayments = await dbContext.Payments
                .ExecuteDeleteAsync(cancellationToken);
            var deletedShipments = await dbContext.Shipments
                .ExecuteDeleteAsync(cancellationToken);
            var deletedStatusHistory = await dbContext.OrderStatusHistory
                .ExecuteDeleteAsync(cancellationToken);
            var deletedOrders = await dbContext.Orders
                .ExecuteDeleteAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Intentional bad LINQ sample for missing_where_on_dml (unfiltered ExecuteDelete). Child rows are deleted first to satisfy FK constraints.",
                affectedRows = new
                {
                    deletedOrderItems,
                    deletedPayments,
                    deletedShipments,
                    deletedStatusHistory,
                    deletedOrders
                }
            });
        });

        orders.MapPost("/", async (CreateOrderRequest request, OrdersDbContext dbContext, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.CustomerName))
            {
                return Results.BadRequest("CustomerName is required.");
            }

            if (request.TotalAmount <= 0)
            {
                return Results.BadRequest("TotalAmount must be greater than zero.");
            }

            var order = new Order
            {
                CustomerName = request.CustomerName.Trim(),
                TotalAmount = request.TotalAmount,
                Status = "Pending",
                CreatedUtc = DateTime.UtcNow
            };

            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/api/orders/{order.Id}", ToResponse(order));
        });

        orders.MapGet("/bad/shared-context", async (OrdersDbContext dbContext, IMongoProfilerEventSink profilerSink, CancellationToken cancellationToken) =>
        {
            // Intentionally broken sample:
            // Runs multiple operations against the same DbContext in parallel.
            // This is useful for reproducing thread-safety/profiler warnings in demos.
            var stopwatch = Stopwatch.StartNew();
            var countTask = Task.Run(() => dbContext.Orders.CountAsync(cancellationToken), cancellationToken);
            var latestTask = Task.Run(() => dbContext.Orders
                .AsNoTracking()
                .OrderByDescending(order => order.CreatedUtc)
                .Take(5)
                .Select(order => ToResponse(order))
                .ToListAsync(cancellationToken), cancellationToken);

            try
            {
                await Task.WhenAll(countTask, latestTask);
                return Results.Ok(new
                {
                    message = "Unexpectedly succeeded; try repeated calls under load.",
                    total = countTask.Result,
                    latest = latestTask.Result
                });
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                var baseException = exception.GetBaseException();
                profilerSink.Publish(new MongoProfilerQueryEvent
                {
                    CommandName = "EF_CONTEXT",
                    DatabaseName = dbContext.Database.GetDbConnection().Database,
                    CollectionName = "dbo.Orders",
                    Query = "Parallel operations were started on the same DbContext instance.",
                    DurationMs = stopwatch.Elapsed.TotalMilliseconds,
                    Success = false,
                    ErrorMessage = baseException.Message,
                    SessionId = dbContext.ContextId.InstanceId.ToString("N"),
                    TraceId = Activity.Current?.TraceId.ToString() ?? string.Empty,
                    SpanId = Activity.Current?.SpanId.ToString() ?? string.Empty,
                    IndexAdviceStatus = "error",
                    IndexAdviceReason = "shared_context_parallel_use, ef_concurrency_detector_triggered"
                });

                Console.Error.WriteLine(
                    $"[BAD-SAMPLE-ERROR] {DateTimeOffset.UtcNow:O} {baseException.GetType().Name}: {baseException.Message}");

                throw;
            }
        });

        orders.MapGet("/good/separate-contexts", async (IDbContextFactory<OrdersDbContext> contextFactory, CancellationToken cancellationToken) =>
        {
            // Correct approach: each parallel flow creates and owns its own DbContext instance.
            var countTask = CountOrdersAsync(contextFactory, cancellationToken);
            var latestTask = GetLatestOrdersAsync(contextFactory, cancellationToken);

            await Task.WhenAll(countTask, latestTask);
            return Results.Ok(new
            {
                message = "Parallel work executed with isolated DbContext instances.",
                total = countTask.Result,
                latest = latestTask.Result
            });
        });
    }

    private static async Task<int> CountOrdersAsync(
        IDbContextFactory<OrdersDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Orders.CountAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<OrderResponse>> GetLatestOrdersAsync(
        IDbContextFactory<OrdersDbContext> contextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Orders
            .AsNoTracking()
            .OrderByDescending(order => order.CreatedUtc)
            .Take(5)
            .Select(order => ToResponse(order))
            .ToListAsync(cancellationToken);
    }

    private static OrderResponse ToResponse(Order order) =>
        new(order.Id, order.CustomerName, order.TotalAmount, order.Status, order.CreatedUtc);
}
