using System;
using System.Collections.Generic;
using System.Linq;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MeridianWms.Core.Data;
using MeridianWms.Core.Models;

namespace MeridianWms.Core.Services
{
    public class InventoryService
    {
        // FIX (Medium): cap free-text search results to prevent data over-fetch / DoS.
        private const int SearchMaxResults = 200;

        private readonly WmsDbContext _db;
        private readonly IConfiguration _config;

        public InventoryService(WmsDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public List<InventoryItem> GetForWarehouse(string warehouseCode)
        {
            var wh = _db.Warehouses.FirstOrDefault(w => w.Code == warehouseCode);
            if (wh == null)
            {
                return new List<InventoryItem>();
            }

            return _db.InventoryItems
                .Where(i => i.WarehouseId == wh.WarehouseId)
                .ToList();
        }

        // Returns products whose available quantity is at or below the
        // configured low-stock threshold, across all warehouses.
        public List<object> GetLowStock()
        {
            // FIX (Low): use TryParse instead of int.Parse so a misconfigured threshold
            // does not crash the application with an unhandled FormatException.
            string? raw = _config["LowStockThreshold"];
            if (!int.TryParse(raw, out int threshold))
            {
                threshold = 10; // safe default
            }

            // FIX (High): filter on the database side instead of loading the entire
            // InventoryItem table into memory; avoids data over-fetch / DoS.
            var items = _db.InventoryItems
                .Include(i => i.Product)
                .Include(i => i.Warehouse)
                .Where(i => (i.QuantityOnHand - i.QuantityReserved) <= threshold)
                .ToList();

            var result = new List<object>();
            foreach (var i in items)
            {
                int available = i.QuantityOnHand - i.QuantityReserved;
                result.Add(new
                {
                    Sku = i.Product!.Sku,
                    Warehouse = i.Warehouse!.Code,
                    Available = available,
                    Bin = i.BinLocation
                });
            }

            return result;
        }

        // Free-text search over inventory by product name fragment.
        // Uses raw ADO.NET against the same database (preserved from legacy for parity).
        public List<Dictionary<string, object>> SearchByProductName(string nameFragment)
        {
            // FIX (High): reject null/empty input early to prevent meaningless full-table
            // scans and to satisfy the parameterized query below.
            if (string.IsNullOrWhiteSpace(nameFragment))
            {
                return new List<Dictionary<string, object>>();
            }

            string connStr = _config.GetConnectionString("WmsDb")
                ?? throw new InvalidOperationException("WmsDb connection string is not configured.");
            var rows = new List<Dictionary<string, object>>();

            using (var conn = new NpgsqlConnection(connStr))
            {
                conn.Open();

                // Parameterized query — user input is bound via NpgsqlCommand.Parameters so
                // it can never alter the query structure (SQL injection prevention).
                // LIMIT $2 caps unbounded data return (DoS / over-fetch prevention).
                string sql = "SELECT p.\"Sku\", p.\"Name\", w.\"Code\" AS \"Warehouse\", i.\"QuantityOnHand\" " +
                             "FROM \"InventoryItem\" i " +
                             "JOIN \"Product\" p ON p.\"ProductId\" = i.\"ProductId\" " +
                             "JOIN \"Warehouse\" w ON w.\"WarehouseId\" = i.\"WarehouseId\" " +
                             "WHERE p.\"Name\" ILIKE $1 " +
                             "LIMIT $2";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    // Bind user input as a typed parameter — never interpolated into SQL text.
                    cmd.Parameters.AddWithValue("$1", "%" + nameFragment + "%");
                    cmd.Parameters.AddWithValue("$2", SearchMaxResults);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, object>();
                            for (int c = 0; c < reader.FieldCount; c++)
                            {
                                row[reader.GetName(c)] = reader.GetValue(c);
                            }
                            rows.Add(row);
                        }
                    }
                }
            }

            return rows;
        }

        public void AdjustQuantity(int productId, string warehouseCode, int delta)
        {
            // FIX (High): validate inputs before touching the database.
            // A null/empty warehouseCode would cause a misleading InvalidOperationException;
            // delta == 0 is a no-op that wastes a round-trip.
            if (string.IsNullOrWhiteSpace(warehouseCode))
            {
                throw new ArgumentException("warehouseCode must not be empty.", nameof(warehouseCode));
            }
            if (delta == 0)
            {
                return; // no-op — nothing to persist
            }

            var wh = _db.Warehouses.First(w => w.Code == warehouseCode);
            var item = _db.InventoryItems
                .First(i => i.ProductId == productId && i.WarehouseId == wh.WarehouseId);

            item.QuantityOnHand = item.QuantityOnHand + delta;
            if (item.QuantityOnHand < 0)
            {
                throw new InvalidOperationException("Quantity on hand cannot be negative.");
            }

            _db.SaveChanges();
        }
    }
}
