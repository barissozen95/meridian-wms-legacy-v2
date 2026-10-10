using Microsoft.EntityFrameworkCore;
using MeridianWms.Core.Models;

namespace MeridianWms.Core.Data
{
    public class WmsDbContext : DbContext
    {
        public WmsDbContext(DbContextOptions<WmsDbContext> options) : base(options)
        {
        }

        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Warehouse> Warehouses { get; set; } = null!;
        public DbSet<InventoryItem> InventoryItems { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderLine> OrderLines { get; set; } = null!;
        public DbSet<Shipment> Shipments { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Preserve exact legacy table names (singular, PascalCase) to avoid any
            // unintended rename caused by Npgsql's default snake_case convention.
            modelBuilder.Entity<Product>().ToTable("Product");
            modelBuilder.Entity<Warehouse>().ToTable("Warehouse");
            modelBuilder.Entity<InventoryItem>().ToTable("InventoryItem");
            // "Order" is a reserved keyword in PostgreSQL; EF Core quotes it automatically.
            modelBuilder.Entity<Order>().ToTable("Order");
            modelBuilder.Entity<OrderLine>().ToTable("OrderLine");
            modelBuilder.Entity<Shipment>().ToTable("Shipment");

            // ---- Product -------------------------------------------------------
            modelBuilder.Entity<Product>(e =>
            {
                e.HasKey(p => p.ProductId);
                e.Property(p => p.ProductId).UseIdentityByDefaultColumn();
                e.Property(p => p.Sku).HasMaxLength(40).HasColumnType("character varying(40)").IsRequired();
                e.Property(p => p.Name).HasMaxLength(200).HasColumnType("character varying(200)").IsRequired();
                e.Property(p => p.Category).HasMaxLength(100).HasColumnType("character varying(100)");
                e.Property(p => p.UnitWeightKg).HasColumnType("numeric(10,3)");
                e.Property(p => p.UnitPrice).HasColumnType("numeric(12,2)");
                e.Property(p => p.IsActive).HasColumnType("boolean").HasDefaultValue(true);
            });

            // ---- Warehouse -----------------------------------------------------
            modelBuilder.Entity<Warehouse>(e =>
            {
                e.HasKey(w => w.WarehouseId);
                e.Property(w => w.WarehouseId).UseIdentityByDefaultColumn();
                e.Property(w => w.Code).HasMaxLength(20).HasColumnType("character varying(20)").IsRequired();
                e.Property(w => w.Name).HasMaxLength(200).HasColumnType("character varying(200)").IsRequired();
                e.Property(w => w.City).HasMaxLength(100).HasColumnType("character varying(100)");
                e.Property(w => w.Country).HasMaxLength(100).HasColumnType("character varying(100)");
            });

            // ---- InventoryItem -------------------------------------------------
            modelBuilder.Entity<InventoryItem>(e =>
            {
                e.HasKey(i => i.InventoryItemId);
                e.Property(i => i.InventoryItemId).UseIdentityByDefaultColumn();
                e.Property(i => i.QuantityOnHand).HasColumnType("integer").HasDefaultValue(0);
                e.Property(i => i.QuantityReserved).HasColumnType("integer").HasDefaultValue(0);
                e.Property(i => i.BinLocation).HasMaxLength(40).HasColumnType("character varying(40)");

                e.HasOne(i => i.Product)
                    .WithMany(p => p.InventoryItems)
                    .HasForeignKey(i => i.ProductId);

                e.HasOne(i => i.Warehouse)
                    .WithMany(w => w.InventoryItems)
                    .HasForeignKey(i => i.WarehouseId);
            });

            // ---- Order ---------------------------------------------------------
            modelBuilder.Entity<Order>(e =>
            {
                e.HasKey(o => o.OrderId);
                e.Property(o => o.OrderId).UseIdentityByDefaultColumn();
                e.Property(o => o.OrderNumber).HasMaxLength(40).HasColumnType("character varying(40)").IsRequired();
                e.Property(o => o.CustomerName).HasMaxLength(200).HasColumnType("character varying(200)").IsRequired();
                e.Property(o => o.DestinationCity).HasMaxLength(100).HasColumnType("character varying(100)");
                e.Property(o => o.DestinationCountry).HasMaxLength(100).HasColumnType("character varying(100)");
                e.Property(o => o.Status).HasMaxLength(20).HasColumnType("character varying(20)").IsRequired();
                // Map to 'timestamp with time zone' (timestamptz); UTC-only application.
                e.Property(o => o.CreatedUtc).HasColumnType("timestamp with time zone");
            });

            // ---- OrderLine -----------------------------------------------------
            modelBuilder.Entity<OrderLine>(e =>
            {
                e.HasKey(l => l.OrderLineId);
                e.Property(l => l.OrderLineId).UseIdentityByDefaultColumn();
                e.Property(l => l.UnitPriceAtOrder).HasColumnType("numeric(12,2)");

                e.HasOne(l => l.Order)
                    .WithMany(o => o.Lines)
                    .HasForeignKey(l => l.OrderId);

                e.HasOne(l => l.Product)
                    .WithMany()
                    .HasForeignKey(l => l.ProductId);
            });

            // ---- Shipment ------------------------------------------------------
            modelBuilder.Entity<Shipment>(e =>
            {
                e.HasKey(s => s.ShipmentId);
                e.Property(s => s.ShipmentId).UseIdentityByDefaultColumn();
                e.Property(s => s.Carrier).HasMaxLength(100).HasColumnType("character varying(100)");
                e.Property(s => s.TrackingNumber).HasMaxLength(100).HasColumnType("character varying(100)");
                e.Property(s => s.FromWarehouseCode).HasMaxLength(20).HasColumnType("character varying(20)");
                // Map to 'timestamp with time zone' (timestamptz); UTC-only application.
                e.Property(s => s.ShippedUtc).HasColumnType("timestamp with time zone");
                e.Property(s => s.TotalWeightKg).HasColumnType("numeric(10,3)");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
