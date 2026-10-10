using System.Collections.Generic;
using System.Configuration;
using Effort;
using MeridianWms.Api.Data;
using MeridianWms.Api.Models;

namespace MeridianWms.Tests.Helpers
{
    /// <summary>
    /// Sets up ConfigurationManager app settings required by production services,
    /// so tests do not need a Web.config at runtime.
    /// </summary>
    public static class ConfigHelper
    {
        private static bool _initialized;

        public static void EnsureAppSettings()
        {
            if (_initialized) return;
            _initialized = true;

            SetOrAdd("DefaultWarehouseCode", DbContextFactory.DefaultWarehouseCode);
            SetOrAdd("LowStockThreshold", "10");
        }

        private static void SetOrAdd(string key, string value)
        {
            if (ConfigurationManager.AppSettings[key] == null)
                ConfigurationManager.AppSettings.Set(key, value);
            else
                ConfigurationManager.AppSettings[key] = value;
        }
    }

    /// <summary>
    /// Creates a fresh Effort in-memory <see cref="WmsDbContext"/> for each test,
    /// seeded with a known baseline that mirrors the schema seed data.
    /// Each call returns an independent context so tests do not share state.
    /// </summary>
    public static class DbContextFactory
    {
        /// <summary>
        /// The warehouse code used as the default warehouse in AppSettings.
        /// The <see cref="ConfigHelper.EnsureAppSettings"/> method sets the
        /// "DefaultWarehouseCode" app setting to this value.
        /// </summary>
        public const string DefaultWarehouseCode = "WH-CENTRAL";

        // Fixed IDs that tests can reference by name.
        public const int WarehouseCentralId = 1;
        public const int WarehouseWestId    = 2;

        public const int ProductPumpId    = 1;  // PMP-3000-A, UnitPrice=1240, Weight=18.5kg
        public const int ProductValveId   = 2;  // VLV-12-SS,  UnitPrice=34.90, Weight=0.75kg
        public const int ProductBearingId = 3;  // BRG-6204,   UnitPrice=8.50,  Weight=0.11kg
        public const int ProductMotorId   = 4;  // MTR-7K5,    UnitPrice=2110,  Weight=42.0kg
        public const int ProductFilterId  = 5;  // FLT-AIR-M,  UnitPrice=22.00, Weight=0.30kg

        /// <summary>
        /// Creates a new in-memory context pre-seeded with warehouses, products,
        /// and inventory items matching the production seed. No orders are pre-inserted
        /// so each test starts with a clean order table.
        /// </summary>
        public static WmsDbContext CreateSeeded()
        {
            var connection = DbConnectionFactory.CreateTransient();
            var ctx = new WmsDbContext(connection, true);

            // ---- Warehouses ------------------------------------------------
            ctx.Warehouses.Add(new Warehouse
            {
                WarehouseId = WarehouseCentralId,
                Code        = "WH-CENTRAL",
                Name        = "Central Distribution Center",
                City        = "Frankfurt",
                Country     = "Germany"
            });
            ctx.Warehouses.Add(new Warehouse
            {
                WarehouseId = WarehouseWestId,
                Code        = "WH-WEST",
                Name        = "Western Hub",
                City        = "Rotterdam",
                Country     = "Netherlands"
            });

            // ---- Products --------------------------------------------------
            ctx.Products.Add(new Product
            {
                ProductId     = ProductPumpId,
                Sku           = "PMP-3000-A",
                Name          = "Industrial Water Pump 3000",
                Category      = "Pumps",
                UnitWeightKg  = 18.500m,
                UnitPrice     = 1240.00m,
                IsActive      = true
            });
            ctx.Products.Add(new Product
            {
                ProductId     = ProductValveId,
                Sku           = "VLV-12-SS",
                Name          = "Stainless Steel Valve 1/2\"",
                Category      = "Valves",
                UnitWeightKg  = 0.750m,
                UnitPrice     = 34.90m,
                IsActive      = true
            });
            ctx.Products.Add(new Product
            {
                ProductId     = ProductBearingId,
                Sku           = "BRG-6204",
                Name          = "Ball Bearing 6204",
                Category      = "Bearings",
                UnitWeightKg  = 0.110m,
                UnitPrice     = 8.50m,
                IsActive      = true
            });
            ctx.Products.Add(new Product
            {
                ProductId     = ProductMotorId,
                Sku           = "MTR-7K5",
                Name          = "Electric Motor 7.5kW",
                Category      = "Motors",
                UnitWeightKg  = 42.000m,
                UnitPrice     = 2110.00m,
                IsActive      = true
            });
            ctx.Products.Add(new Product
            {
                ProductId     = ProductFilterId,
                Sku           = "FLT-AIR-M",
                Name          = "Air Filter Cartridge M",
                Category      = "Filters",
                UnitWeightKg  = 0.300m,
                UnitPrice     = 22.00m,
                IsActive      = true
            });

            // ---- Inventory (WH-CENTRAL) ------------------------------------
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 1,
                ProductId        = ProductPumpId,
                WarehouseId      = WarehouseCentralId,
                QuantityOnHand   = 25,
                QuantityReserved = 0,
                BinLocation      = "A-01-1"
            });
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 2,
                ProductId        = ProductValveId,
                WarehouseId      = WarehouseCentralId,
                QuantityOnHand   = 480,
                QuantityReserved = 0,
                BinLocation      = "B-04-2"
            });
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 3,
                ProductId        = ProductBearingId,
                WarehouseId      = WarehouseCentralId,
                QuantityOnHand   = 6,
                QuantityReserved = 0,
                BinLocation      = "C-09-3"
            });
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 4,
                ProductId        = ProductMotorId,
                WarehouseId      = WarehouseCentralId,
                QuantityOnHand   = 12,
                QuantityReserved = 0,
                BinLocation      = "A-02-1"
            });
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 5,
                ProductId        = ProductFilterId,
                WarehouseId      = WarehouseCentralId,
                QuantityOnHand   = 8,
                QuantityReserved = 0,
                BinLocation      = "D-11-4"
            });

            // ---- Inventory (WH-WEST) ---------------------------------------
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 6,
                ProductId        = ProductPumpId,
                WarehouseId      = WarehouseWestId,
                QuantityOnHand   = 10,
                QuantityReserved = 0,
                BinLocation      = "A-01-1"
            });
            ctx.InventoryItems.Add(new InventoryItem
            {
                InventoryItemId  = 7,
                ProductId        = ProductValveId,
                WarehouseId      = WarehouseWestId,
                QuantityOnHand   = 200,
                QuantityReserved = 0,
                BinLocation      = "B-02-1"
            });

            ctx.SaveChanges();
            return ctx;
        }

        /// <summary>
        /// Convenience: creates a seeded context and inserts one Pending order for
        /// order-lifecycle tests that need an existing order to act on.
        /// Returns both the context and the newly created order ID.
        /// </summary>
        public static (WmsDbContext ctx, int orderId) CreateSeededWithPendingOrder(
            int productId = ProductPumpId, int qty = 2)
        {
            var ctx = CreateSeeded();
            var order = new Order
            {
                OrderNumber         = "ORD-TEST-001",
                CustomerName        = "Test Customer",
                DestinationCity     = "Berlin",
                DestinationCountry  = "Germany",
                Status              = "Pending",
                CreatedUtc          = System.DateTime.UtcNow,
                Lines               = new List<OrderLine>
                {
                    new OrderLine
                    {
                        ProductId        = productId,
                        Quantity         = qty,
                        UnitPriceAtOrder = ctx.Products.Find(productId).UnitPrice
                    }
                }
            };
            ctx.Orders.Add(order);
            ctx.SaveChanges();
            return (ctx, order.OrderId);
        }
    }
}
