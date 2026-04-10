namespace Mongo.Profiler.SampleApi.Features.Orders;

public class Order
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedUtc { get; set; }

    public virtual ICollection<OrderItem> Items { get; set; } = [];
    public virtual ICollection<Payment> Payments { get; set; } = [];
    public virtual ICollection<Shipment> Shipments { get; set; } = [];
    public virtual ICollection<OrderStatusHistory> StatusHistory { get; set; } = [];
}
