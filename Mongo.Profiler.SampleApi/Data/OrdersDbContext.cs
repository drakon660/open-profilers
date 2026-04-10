using Microsoft.EntityFrameworkCore;
using Mongo.Profiler.SampleApi.Features.Orders;

namespace Mongo.Profiler.SampleApi.Data;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders", "dbo");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.CustomerName).HasMaxLength(200).IsRequired();
            entity.Property(order => order.Status).HasMaxLength(50).IsRequired();
            entity.Property(order => order.TotalAmount).HasColumnType("decimal(18,2)");
            entity.Property(order => order.CreatedUtc).HasPrecision(3);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products", "dbo");
            entity.HasKey(product => product.Id);
            entity.Property(product => product.Name).HasMaxLength(200).IsRequired();
            entity.Property(product => product.SKU).HasMaxLength(100).IsRequired();
            entity.Property(product => product.Price).HasColumnType("decimal(18,2)");
            entity.Property(product => product.IsActive).IsRequired();
            entity.Property(product => product.CreatedUtc).HasPrecision(3);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems", "dbo");
            entity.HasKey(orderItem => orderItem.Id);
            entity.Property(orderItem => orderItem.UnitPrice).HasColumnType("decimal(18,2)");

            entity.HasOne(orderItem => orderItem.Order)
                .WithMany(order => order.Items)
                .HasForeignKey(orderItem => orderItem.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(orderItem => orderItem.Product)
                .WithMany(product => product.OrderItems)
                .HasForeignKey(orderItem => orderItem.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments", "dbo");
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.PaymentMethod).HasMaxLength(50).IsRequired();
            entity.Property(payment => payment.Amount).HasColumnType("decimal(18,2)");
            entity.Property(payment => payment.PaidUtc).HasPrecision(3);
            entity.Property(payment => payment.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(payment => payment.Order)
                .WithMany(order => order.Payments)
                .HasForeignKey(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Shipment>(entity =>
        {
            entity.ToTable("Shipments", "dbo");
            entity.HasKey(shipment => shipment.Id);
            entity.Property(shipment => shipment.AddressLine1).HasMaxLength(200).IsRequired();
            entity.Property(shipment => shipment.City).HasMaxLength(100).IsRequired();
            entity.Property(shipment => shipment.PostalCode).HasMaxLength(20).IsRequired();
            entity.Property(shipment => shipment.Country).HasMaxLength(100).IsRequired();
            entity.Property(shipment => shipment.ShippedUtc).HasPrecision(3);
            entity.Property(shipment => shipment.Status).HasMaxLength(50).IsRequired();

            entity.HasOne(shipment => shipment.Order)
                .WithMany(order => order.Shipments)
                .HasForeignKey(shipment => shipment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("OrderStatusHistory", "dbo");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.OldStatus).HasMaxLength(50);
            entity.Property(history => history.NewStatus).HasMaxLength(50).IsRequired();
            entity.Property(history => history.ChangedUtc).HasPrecision(3);
            entity.Property(history => history.ChangedBy).HasMaxLength(200);

            entity.HasOne(history => history.Order)
                .WithMany(order => order.StatusHistory)
                .HasForeignKey(history => history.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
