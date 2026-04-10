namespace Mongo.Profiler.SampleApi.Features.Orders;

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime? PaidUtc { get; set; }
    public string Status { get; set; } = string.Empty;

    public virtual Order? Order { get; set; }
}
