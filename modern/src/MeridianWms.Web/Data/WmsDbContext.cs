using MeridianWms.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MeridianWms.Web.Data;

// Schema mirrors scripts/schema-sqlserver.sql 1:1 (same table/column names, precisions, nullability)
// so scripts/load_batch.py and scripts/validate_migration.py work against it unchanged.
// Uniqueness / CHECK constraints and the Shipment->Order FK are deliberately NOT in the migration:
// they are applied by scripts/postgres/post-load-hardening.sql after the legacy data is loaded.
public class WmsDbContext(DbContextOptions<WmsDbContext> options) : DbContext(options)
{
    public const string OrderNumberSequence = "OrderNumberSeq";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Collision-free source for OrderNumber (legacy used a 1-second-resolution timestamp).
        b.HasSequence<long>(OrderNumberSequence);

        b.Entity<Product>(e =>
        {
            e.ToTable("Product");
            e.Property(p => p.Sku).HasMaxLength(40);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Category).HasMaxLength(100);
            e.Property(p => p.UnitWeightKg).HasPrecision(10, 3);
            e.Property(p => p.UnitPrice).HasPrecision(12, 2);
            e.Property(p => p.IsActive).HasDefaultValue(true).HasSentinel(true);
        });

        b.Entity<Warehouse>(e =>
        {
            e.ToTable("Warehouse");
            e.Property(w => w.Code).HasMaxLength(20);
            e.Property(w => w.Name).HasMaxLength(200);
            e.Property(w => w.City).HasMaxLength(100);
            e.Property(w => w.Country).HasMaxLength(100);
        });

        b.Entity<InventoryItem>(e =>
        {
            e.ToTable("InventoryItem");
            e.Property(i => i.QuantityOnHand).HasDefaultValue(0);
            e.Property(i => i.QuantityReserved).HasDefaultValue(0);
            e.Property(i => i.BinLocation).HasMaxLength(40);
            // Optimistic concurrency on PostgreSQL's system column xmin (no schema change):
            // two requests reserving the same stock cannot both win.
            e.Property<uint>("xmin").IsRowVersion();
            e.HasOne(i => i.Product).WithMany(p => p.InventoryItems)
                .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(i => i.Warehouse).WithMany(w => w.InventoryItems)
                .HasForeignKey(i => i.WarehouseId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Order>(e =>
        {
            e.ToTable("Order");
            e.Property(o => o.OrderNumber).HasMaxLength(40);
            e.Property(o => o.CustomerName).HasMaxLength(200);
            e.Property(o => o.DestinationCity).HasMaxLength(100);
            e.Property(o => o.DestinationCountry).HasMaxLength(100);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.Property<uint>("xmin").IsRowVersion();
        });

        b.Entity<OrderLine>(e =>
        {
            e.ToTable("OrderLine");
            e.Property(l => l.UnitPriceAtOrder).HasPrecision(12, 2);
            e.HasOne(l => l.Order).WithMany(o => o.Lines)
                .HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(l => l.Product).WithMany()
                .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<Shipment>(e =>
        {
            e.ToTable("Shipment");
            e.Property(s => s.Carrier).HasMaxLength(100);
            e.Property(s => s.TrackingNumber).HasMaxLength(100);
            e.Property(s => s.FromWarehouseCode).HasMaxLength(20);
            e.Property(s => s.TotalWeightKg).HasPrecision(10, 3);
        });
    }
}
