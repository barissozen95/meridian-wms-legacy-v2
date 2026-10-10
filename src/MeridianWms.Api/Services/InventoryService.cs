using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using MeridianWms.Api.Data;
using MeridianWms.Api.Models;

namespace MeridianWms.Api.Services
{
    public class InventoryService
    {
        // FIX (Medium): cap free-text search results to prevent data over-fetch / DoS.
        private const int SearchMaxResults = 200;

        private readonly WmsDbContext _db;

        public InventoryService()
        {
            _db = new WmsDbContext();
        }

        // Constructor used by unit tests to inject an in-memory context.
        internal InventoryService(WmsDbContext db)
        {
            _db = db;
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
            string raw = ConfigurationManager.AppSettings["LowStockThreshold"];
            if (!int.TryParse(raw, out int threshold))
            {
                threshold = 10; // safe default
            }

            // FIX (High): filter on the database side instead of loading the entire
            // InventoryItem table into memory; avoids data over-fetch / DoS.
            var items = _db.InventoryItems
                .Include("Product")
                .Include("Warehouse")
                .Where(i => (i.QuantityOnHand - i.QuantityReserved) <= threshold)
                .ToList();

            var result = new List<object>();
            foreach (var i in items)
            {
                int available = i.QuantityOnHand - i.QuantityReserved;
                result.Add(new
                {
                    Sku = i.Product.Sku,
                    Warehouse = i.Warehouse.Code,
                    Available = available,
                    Bin = i.BinLocation
                });
            }

            return result;
        }

        // Free-text search over inventory by product name fragment.
        // Uses raw ADO.NET against the same database.
        public List<Dictionary<string, object>> SearchByProductName(string nameFragment)
        {
            // FIX (High): reject null/empty input early to prevent meaningless full-table
            // scans and to satisfy the parameterized query below.
            if (string.IsNullOrWhiteSpace(nameFragment))
            {
                return new List<Dictionary<string, object>>();
            }

            string connStr = ConfigurationManager.ConnectionStrings["WmsDb"].ConnectionString;
            var rows = new List<Dictionary<string, object>>();

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                // FIX (Critical — SQL Injection): replaced unsafe string concatenation with
                // a parameterized query. The @name parameter is bound via SqlCommand.Parameters
                // so user input can never alter the query structure.
                // FIX (High — result limit): TOP (@maxRows) prevents unbounded data return
                // that could be used to exhaust server memory or bandwidth.
                string sql = "SELECT TOP (@maxRows) p.Sku, p.Name, w.Code AS Warehouse, i.QuantityOnHand " +
                             "FROM InventoryItem i " +
                             "JOIN Product p ON p.ProductId = i.ProductId " +
                             "JOIN Warehouse w ON w.WarehouseId = i.WarehouseId " +
                             "WHERE p.Name LIKE @name";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    // Bind user input as a typed parameter — never interpolated into SQL text.
                    cmd.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 200).Value =
                        "%" + nameFragment + "%";
                    cmd.Parameters.Add("@maxRows", System.Data.SqlDbType.Int).Value =
                        SearchMaxResults;

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
