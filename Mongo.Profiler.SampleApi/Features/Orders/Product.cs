namespace Mongo.Profiler.SampleApi.Features.Orders;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SKU { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }

    public virtual ICollection<OrderItem> OrderItems { get; set; } = [];
}
