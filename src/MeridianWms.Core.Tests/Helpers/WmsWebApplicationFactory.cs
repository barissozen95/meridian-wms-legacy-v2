using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MeridianWms.Core.Data;
using MeridianWms.Core.Models;

namespace MeridianWms.Core.Tests.Helpers
{
    /// <summary>
    /// Custom WebApplicationFactory that replaces the SQL Server DbContext with an
    /// EF Core in-memory provider and seeds baseline reference data so every test
    /// starts from a consistent, isolated state.
    /// </summary>
    public class WmsWebApplicationFactory : WebApplicationFactory<Program>
    {
        // Unique DB name per factory instance → each test class gets its own isolated store.
        private readonly string _dbName = "WmsIntegrationTest_" + Guid.NewGuid();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureTestServices(services =>
            {
                // Remove the SQL Server DbContext registration added in Program.cs.
                services.RemoveAll<DbContextOptions<WmsDbContext>>();
                services.RemoveAll<WmsDbContext>();

                // Register EF Core with a named in-memory database.
                services.AddDbContext<WmsDbContext>(options =>
                    options.UseInMemoryDatabase(_dbName));
            });

            // Override config values consumed by services.
            builder.UseSetting("DefaultWarehouseCode", "WH-CENTRAL");
            builder.UseSetting("LowStockThreshold", "10");
            // Provide a dummy connection string so InventoryService.SearchByProductName
            // can read it without throwing on startup (not called in in-memory tests).
            builder.UseSetting("ConnectionStrings:WmsDb",
                "Host=localhost;Port=5432;Database=wms_db;Username=wms_user;Password=unused_in_tests");
        }

        /// <summary>
        /// Creates an <see cref="HttpClient"/> and seeds the in-memory database.
        /// Call this instead of <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/>
        /// when you need a pre-seeded client.
        /// </summary>
        public System.Net.Http.HttpClient CreateSeededClient()
        {
            var client = CreateClient();

            // Seed via the application's DI container so the same DbContext instance
            // (same in-memory database name) is used by both seeding and the app.
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WmsDbContext>();
            db.Database.EnsureCreated();
            SeedDatabase(db);

            return client;
        }

        internal static void SeedDatabase(WmsDbContext db)
        {
            // Idempotent: skip if already seeded (CreateSeeded may be called multiple times).
            if (db.Warehouses.Any()) return;

            db.Warehouses.AddRange(new List<Warehouse>
            {
                new Warehouse { WarehouseId = 1, Code = "WH-CENTRAL", Name = "Central Distribution Center", City = "Frankfurt",  Country = "Germany"     },
                new Warehouse { WarehouseId = 2, Code = "WH-WEST",    Name = "Western Hub",                 City = "Rotterdam", Country = "Netherlands"  }
            });

            db.Products.AddRange(new List<Product>
            {
                new Product { ProductId = 1, Sku = "PMP-3000-A", Name = "Industrial Water Pump 3000",  Category = "Pumps",    UnitWeightKg = 18.500m, UnitPrice = 1240.00m, IsActive = true },
                new Product { ProductId = 2, Sku = "VLV-12-SS",  Name = "Stainless Steel Valve 1/2\"", Category = "Valves",   UnitWeightKg =  0.750m, UnitPrice =   34.90m, IsActive = true },
                new Product { ProductId = 3, Sku = "BRG-6204",   Name = "Ball Bearing 6204",           Category = "Bearings", UnitWeightKg =  0.110m, UnitPrice =    8.50m, IsActive = true },
                new Product { ProductId = 4, Sku = "MTR-7K5",    Name = "Electric Motor 7.5kW",        Category = "Motors",   UnitWeightKg = 42.000m, UnitPrice = 2110.00m, IsActive = true },
                new Product { ProductId = 5, Sku = "FLT-AIR-M",  Name = "Air Filter Cartridge M",      Category = "Filters",  UnitWeightKg =  0.300m, UnitPrice =   22.00m, IsActive = true }
            });

            db.InventoryItems.AddRange(new List<InventoryItem>
            {
                new InventoryItem { InventoryItemId = 1, ProductId = 1, WarehouseId = 1, QuantityOnHand = 25,  QuantityReserved = 0, BinLocation = "A-01-1" },
                new InventoryItem { InventoryItemId = 2, ProductId = 2, WarehouseId = 1, QuantityOnHand = 480, QuantityReserved = 0, BinLocation = "B-04-2" },
                new InventoryItem { InventoryItemId = 3, ProductId = 3, WarehouseId = 1, QuantityOnHand = 6,   QuantityReserved = 0, BinLocation = "C-09-3" },
                new InventoryItem { InventoryItemId = 4, ProductId = 4, WarehouseId = 1, QuantityOnHand = 12,  QuantityReserved = 0, BinLocation = "A-02-1" },
                new InventoryItem { InventoryItemId = 5, ProductId = 5, WarehouseId = 1, QuantityOnHand = 8,   QuantityReserved = 0, BinLocation = "D-11-4" },
                new InventoryItem { InventoryItemId = 6, ProductId = 1, WarehouseId = 2, QuantityOnHand = 10,  QuantityReserved = 0, BinLocation = "A-01-1" },
                new InventoryItem { InventoryItemId = 7, ProductId = 2, WarehouseId = 2, QuantityOnHand = 200, QuantityReserved = 0, BinLocation = "B-02-1" }
            });

            db.SaveChanges();
        }
    }
}
