namespace Mongo.Profiler.SampleApi.Features.Orders;

public sealed record CreateOrderRequest(string CustomerName, decimal TotalAmount);

public sealed record OrderResponse(
    int Id,
    string CustomerName,
    decimal TotalAmount,
    string Status,
    DateTime CreatedUtc);

public sealed record ProductMiniResponse(
    int Id,
    string Name,
    string SKU,
    decimal Price);

public sealed record OrderItemJoinedResponse(
    int Id,
    int ProductId,
    int Quantity,
    decimal UnitPrice,
    ProductMiniResponse? Product);

public sealed record PaymentResponse(
    int Id,
    string PaymentMethod,
    decimal Amount,
    DateTime? PaidUtc,
    string Status);

public sealed record ShipmentResponse(
    int Id,
    string AddressLine1,
    string City,
    string PostalCode,
    string Country,
    DateTime? ShippedUtc,
    string Status);

public sealed record OrderStatusHistoryResponse(
    int Id,
    string? OldStatus,
    string NewStatus,
    DateTime ChangedUtc,
    string? ChangedBy);

public sealed record OrderHierarchyResponse(
    int Id,
    string CustomerName,
    decimal TotalAmount,
    string Status,
    DateTime CreatedUtc,
    IReadOnlyList<OrderItemJoinedResponse> Items,
    IReadOnlyList<PaymentResponse> Payments,
    IReadOnlyList<ShipmentResponse> Shipments,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory);

public sealed record OrderNPlusOneItemCountResponse(
    int OrderId,
    string CustomerName,
    int ItemCount);
