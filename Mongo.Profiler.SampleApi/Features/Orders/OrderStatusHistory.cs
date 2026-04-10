namespace Mongo.Profiler.SampleApi.Features.Orders;

public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string? OldStatus { get; set; }
    public string NewStatus { get; set; } = string.Empty;
    public DateTime ChangedUtc { get; set; }
    public string? ChangedBy { get; set; }

    public virtual Order? Order { get; set; }
}
