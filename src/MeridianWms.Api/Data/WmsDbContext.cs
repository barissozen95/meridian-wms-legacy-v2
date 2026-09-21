using System.Data.Entity;
using System.Data.Entity.ModelConfiguration.Conventions;
using MeridianWms.Api.Models;

namespace MeridianWms.Api.Data
{
    public class WmsDbContext : DbContext
    {
        // Uses the "WmsDb" connection string from Web.config.
        public WmsDbContext() : base("name=WmsDb")
        {
            Configuration.LazyLoadingEnabled = false;
            Configuration.ProxyCreationEnabled = false;
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Warehouse> Warehouses { get; set; }
        public DbSet<InventoryItem> InventoryItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLine> OrderLines { get; set; }
        public DbSet<Shipment> Shipments { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Conventions.Remove<PluralizingTableNameConvention>();

            modelBuilder.Entity<Product>().Property(p => p.UnitWeightKg).HasPrecision(10, 3);
            modelBuilder.Entity<Product>().Property(p => p.UnitPrice).HasPrecision(12, 2);
            modelBuilder.Entity<OrderLine>().Property(l => l.UnitPriceAtOrder).HasPrecision(12, 2);
            modelBuilder.Entity<Shipment>().Property(s => s.TotalWeightKg).HasPrecision(10, 3);

            base.OnModelCreating(modelBuilder);
        }
    }
}
