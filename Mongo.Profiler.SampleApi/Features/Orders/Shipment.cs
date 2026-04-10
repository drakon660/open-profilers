namespace Mongo.Profiler.SampleApi.Features.Orders;

public class Shipment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public DateTime? ShippedUtc { get; set; }
    public string Status { get; set; } = string.Empty;

    public virtual Order? Order { get; set; }
}
