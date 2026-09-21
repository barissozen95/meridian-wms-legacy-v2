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
        private readonly WmsDbContext _db = new WmsDbContext();

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
            int threshold = int.Parse(ConfigurationManager.AppSettings["LowStockThreshold"]);

            var items = _db.InventoryItems
                .Include("Product")
                .Include("Warehouse")
                .ToList();
            var result = new List<object>();

            foreach (var i in items)
            {
                int available = i.QuantityOnHand - i.QuantityReserved;
                if (available <= threshold)
                {
                    result.Add(new
                    {
                        Sku = i.Product.Sku,
                        Warehouse = i.Warehouse.Code,
                        Available = available,
                        Bin = i.BinLocation
                    });
                }
            }

            return result;
        }

        // Free-text search over inventory by product name fragment.
        // Uses raw ADO.NET against the same database.
        public List<Dictionary<string, object>> SearchByProductName(string nameFragment)
        {
            string connStr = ConfigurationManager.ConnectionStrings["WmsDb"].ConnectionString;
            var rows = new List<Dictionary<string, object>>();

            using (var conn = new SqlConnection(connStr))
            {
                conn.Open();

                string sql = "SELECT p.Sku, p.Name, w.Code AS Warehouse, i.QuantityOnHand " +
                             "FROM InventoryItem i " +
                             "JOIN Product p ON p.ProductId = i.ProductId " +
                             "JOIN Warehouse w ON w.WarehouseId = i.WarehouseId " +
                             "WHERE p.Name LIKE '%" + nameFragment + "%'";

                using (var cmd = new SqlCommand(sql, conn))
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

            return rows;
        }

        public void AdjustQuantity(int productId, string warehouseCode, int delta)
        {
            var wh = _db.Warehouses.First(w => w.Code == warehouseCode);
            var item = _db.InventoryItems
                .First(i => i.ProductId == productId && i.WarehouseId == wh.WarehouseId);

            item.QuantityOnHand = item.QuantityOnHand + delta;
            if (item.QuantityOnHand < 0)
            {
                throw new Exception("Quantity on hand cannot be negative.");
            }

            _db.SaveChanges();
        }
    }
}
