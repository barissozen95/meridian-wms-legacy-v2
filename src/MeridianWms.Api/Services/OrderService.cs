using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using MeridianWms.Api.Data;
using MeridianWms.Api.Models;

namespace MeridianWms.Api.Services
{
    public class OrderService
    {
        private readonly WmsDbContext _db;

        public OrderService()
        {
            _db = new WmsDbContext();
        }

        // Constructor used by unit tests to inject an in-memory context.
        internal OrderService(WmsDbContext db)
        {
            _db = db;
        }

        public Order GetById(int orderId)
        {
            return _db.Orders
                .Include("Lines")
                .FirstOrDefault(o => o.OrderId == orderId);
        }

        public List<Order> GetAll()
        {
            // FIX (Medium): limit result set to prevent full-table scans being returned
            // to callers in a warehouse with a large order history (DoS / over-fetch).
            return _db.Orders.OrderByDescending(o => o.CreatedUtc).Take(500).ToList();
        }

        // Creates a new order in "Pending" status. Order number is generated
        // from the current time.
        public Order CreateOrder(string customerName, string destinationCity,
            string destinationCountry, List<OrderLine> lines)
        {
            var order = new Order
            {
                OrderNumber = "ORD-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
                CustomerName = customerName,
                DestinationCity = destinationCity,
                DestinationCountry = destinationCountry,
                Status = "Pending",
                CreatedUtc = DateTime.Now,
                Lines = new List<OrderLine>()
            };

            foreach (var line in lines)
            {
                var product = _db.Products.Find(line.ProductId);
                if (product == null)
                {
                    // FIX (Medium — error disclosure): use InvalidOperationException (a
                    // business-rule type) so the controller can distinguish it from
                    // unexpected infrastructure errors and decide whether to surface it.
                    // ProductId is an internal int — safe to include in a business message.
                    throw new InvalidOperationException("Unknown product: " + line.ProductId);
                }

                order.Lines.Add(new OrderLine
                {
                    ProductId = product.ProductId,
                    Quantity = line.Quantity,
                    UnitPriceAtOrder = product.UnitPrice
                });
            }

            _db.Orders.Add(order);
            _db.SaveChanges();
            return order;
        }

        // Allocates stock for every line of the order from the default warehouse,
        // reserving quantity. Moves the order to "Allocated".
        public Order Allocate(int orderId)
        {
            string defaultWh = ConfigurationManager.AppSettings["DefaultWarehouseCode"];
            var wh = _db.Warehouses.First(w => w.Code == defaultWh);

            var order = _db.Orders.Include("Lines").First(o => o.OrderId == orderId);
            if (order.Status != "Pending")
            {
                // FIX (Medium — error disclosure): use InvalidOperationException so the
                // controller can safely surface this business-rule message to callers.
                throw new InvalidOperationException("Only Pending orders can be allocated.");
            }

            foreach (var line in order.Lines)
            {
                var item = _db.InventoryItems.First(
                    i => i.ProductId == line.ProductId && i.WarehouseId == wh.WarehouseId);

                int available = item.QuantityOnHand - item.QuantityReserved;
                if (available < line.Quantity)
                {
                    // FIX (Medium — error disclosure): InvalidOperationException marks this
                    // as a safe business message; ProductId is an internal key, not PII.
                    throw new InvalidOperationException("Insufficient stock for product " + line.ProductId);
                }

                item.QuantityReserved = item.QuantityReserved + line.Quantity;
            }

            order.Status = "Allocated";
            _db.SaveChanges();
            return order;
        }

        // Ships an allocated order: decrements on-hand and reserved stock,
        // computes total weight, writes a Shipment row, sets order to "Shipped".
        public Shipment Ship(int orderId, string carrier, string trackingNumber)
        {
            string defaultWh = ConfigurationManager.AppSettings["DefaultWarehouseCode"];
            var wh = _db.Warehouses.First(w => w.Code == defaultWh);

            var order = _db.Orders.Include("Lines").First(o => o.OrderId == orderId);
            if (order.Status != "Allocated")
            {
                // FIX (Medium — error disclosure): use InvalidOperationException.
                throw new InvalidOperationException("Only Allocated orders can be shipped.");
            }

            decimal totalWeight = 0;
            foreach (var line in order.Lines)
            {
                var item = _db.InventoryItems.First(
                    i => i.ProductId == line.ProductId && i.WarehouseId == wh.WarehouseId);
                var product = _db.Products.Find(line.ProductId);

                item.QuantityOnHand = item.QuantityOnHand - line.Quantity;
                item.QuantityReserved = item.QuantityReserved - line.Quantity;
                totalWeight = totalWeight + (product.UnitWeightKg * line.Quantity);
            }

            var shipment = new Shipment
            {
                OrderId = order.OrderId,
                Carrier = carrier,
                TrackingNumber = trackingNumber,
                FromWarehouseCode = wh.Code,
                ShippedUtc = DateTime.Now,
                TotalWeightKg = totalWeight
            };
            _db.Shipments.Add(shipment);

            order.Status = "Shipped";
            _db.SaveChanges();
            return shipment;
        }
    }
}
