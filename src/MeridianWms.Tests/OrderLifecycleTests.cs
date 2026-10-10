// =============================================================================
// OrderLifecycleTests.cs
// -----------------------------------------------------------------------------
// PURPOSE: Pin-down / regression tests that document and lock in the CURRENT,
//   AS-IS behaviour of OrderService.  These tests assert what the code actually
//   does today — including known bugs and quirks — so that future refactoring
//   does not accidentally change observable behaviour without a deliberate
//   decision.
//
// FRAMEWORK: MSTest v3 + Effort.EF6 (in-memory Entity Framework provider)
//
// ORDER LIFECYCLE (as observed in source):
//   Pending -> Allocated  (via Allocate)
//   Allocated -> Shipped  (via Ship)
//   No Cancel endpoint / method exists in the current codebase.
//   Status is stored as a free-text string; no enum validation exists.
//
// KNOWN ISSUES annotated inline with: // KNOWN ISSUE: <description>
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MeridianWms.Api.Models;
using MeridianWms.Api.Services;
using MeridianWms.Tests.Helpers;

namespace MeridianWms.Tests
{
    [TestClass]
    public class OrderLifecycleTests
    {
        // -------------------------------------------------------------------------
        // Test initialisation: configure AppSettings once for the whole class.
        // -------------------------------------------------------------------------
        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            ConfigHelper.EnsureAppSettings();
        }

        // =========================================================================
        // CreateOrder
        // =========================================================================

        [TestMethod]
        [Description("A valid order is created with Status=Pending and the correct field values.")]
        public void CreateOrder_WithValidInputs_ReturnsOrderWithPendingStatus()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var lines = new List<OrderLine>
            {
                new OrderLine { ProductId = DbContextFactory.ProductPumpId, Quantity = 3 }
            };

            var result = svc.CreateOrder("Acme Corp", "Berlin", "Germany", lines);

            Assert.IsNotNull(result);
            Assert.AreEqual("Pending", result.Status);
            Assert.AreEqual("Acme Corp", result.CustomerName);
            Assert.AreEqual("Berlin", result.DestinationCity);
            Assert.AreEqual("Germany", result.DestinationCountry);
        }

        [TestMethod]
        [Description("OrderNumber is generated with the ORD- prefix and a timestamp suffix.")]
        public void CreateOrder_GeneratesOrderNumber_WithOrdPrefix()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var lines = new List<OrderLine>
            {
                new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 1 }
            };

            var result = svc.CreateOrder("Test Customer", "Paris", "France", lines);

            Assert.IsTrue(result.OrderNumber.StartsWith("ORD-"),
                "OrderNumber should start with 'ORD-'");
        }

        [TestMethod]
        [Description("CreatedUtc is populated on creation (field is misnamed — uses DateTime.Now not UTC).")]
        public void CreateOrder_SetsCreatedUtc_ToApproximatelyNow()
        {
            // KNOWN ISSUE: CreatedUtc is set via DateTime.Now (local time), not
            //   DateTime.UtcNow, despite the field name implying UTC.  This test
            //   asserts the current (incorrect) behaviour.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var before = DateTime.Now;
            var result = svc.CreateOrder("Test", "Munich", "Germany",
                new List<OrderLine> { new OrderLine { ProductId = DbContextFactory.ProductPumpId, Quantity = 1 } });
            var after = DateTime.Now;

            Assert.IsTrue(result.CreatedUtc >= before && result.CreatedUtc <= after,
                "CreatedUtc should fall between the timestamps captured before and after creation.");
        }

        [TestMethod]
        [Description("UnitPriceAtOrder on the line is captured from the product catalogue at order time.")]
        public void CreateOrder_CapturesUnitPriceAtOrder_FromProductCatalogue()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var lines = new List<OrderLine>
            {
                new OrderLine { ProductId = DbContextFactory.ProductPumpId, Quantity = 2 }
            };

            var result = svc.CreateOrder("Customer A", "Hamburg", "Germany", lines);

            var line = result.Lines.Single();
            Assert.AreEqual(1240.00m, line.UnitPriceAtOrder,
                "UnitPriceAtOrder should match the product's current UnitPrice.");
        }

        [TestMethod]
        [Description("Creating an order with an unknown ProductId throws an Exception.")]
        public void CreateOrder_WithUnknownProductId_ThrowsException()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var lines = new List<OrderLine>
            {
                new OrderLine { ProductId = 9999, Quantity = 1 }
            };

            var ex = Assert.ThrowsException<Exception>(
                () => svc.CreateOrder("Bad Customer", "NoCity", "NoCountry", lines));

            StringAssert.Contains(ex.Message, "Unknown product: 9999");
        }

        [TestMethod]
        [Description("Two orders created with the same customer and product are distinct objects with different OrderIds.")]
        public void CreateOrder_AllowsDuplicateOrders_NoUniquenessConstraint()
        {
            // KNOWN ISSUE: No duplicate-order guard exists.  Two calls with identical
            //   inputs produce two separate Order rows.  This test pins that current
            //   permissive behaviour.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            Func<List<OrderLine>> mkLines = () => new List<OrderLine>
            {
                new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 5 }
            };

            var o1 = svc.CreateOrder("Same Customer", "Same City", "DE", mkLines());
            var o2 = svc.CreateOrder("Same Customer", "Same City", "DE", mkLines());

            Assert.AreNotEqual(o1.OrderId, o2.OrderId,
                "Each CreateOrder call should produce a distinct Order row.");
        }

        [TestMethod]
        [Description("A null lines list causes a NullReferenceException — no guard exists.")]
        public void CreateOrder_WithNullLinesList_ThrowsNullReferenceException()
        {
            // KNOWN ISSUE: CreateOrder does not validate that 'lines' is non-null.
            //   Passing null causes a NullReferenceException when iterating the list.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            Assert.ThrowsException<NullReferenceException>(
                () => svc.CreateOrder("Customer", "City", "Country", null));
        }

        [TestMethod]
        [Description("An order with zero quantity on a line is accepted without error.")]
        public void CreateOrder_WithZeroQuantityLine_IsAcceptedWithoutError()
        {
            // KNOWN ISSUE: No validation of line quantities.  A line with Quantity=0
            //   is silently accepted and persisted.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var result = svc.CreateOrder("Customer", "City", "Country",
                new List<OrderLine>
                {
                    new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 0 }
                });

            Assert.AreEqual(0, result.Lines.Single().Quantity,
                "A zero-quantity line should be stored as-is.");
        }

        [TestMethod]
        [Description("An order with a negative quantity line is accepted without error.")]
        public void CreateOrder_WithNegativeQuantityLine_IsAcceptedWithoutError()
        {
            // KNOWN ISSUE: No validation of line quantities.  A negative Quantity is
            //   silently persisted.  This could corrupt inventory reservations.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var result = svc.CreateOrder("Customer", "City", "Country",
                new List<OrderLine>
                {
                    new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = -5 }
                });

            Assert.AreEqual(-5, result.Lines.Single().Quantity,
                "A negative-quantity line should be stored as-is (no guard exists).");
        }

        [TestMethod]
        [Description("CreateOrder with an empty lines list creates an order with no lines.")]
        public void CreateOrder_WithEmptyLinesList_CreatesOrderWithNoLines()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var result = svc.CreateOrder("Customer", "City", "Country", new List<OrderLine>());

            Assert.AreEqual(0, result.Lines.Count,
                "An empty line list should produce an order with zero lines.");
            Assert.AreEqual("Pending", result.Status);
        }

        // =========================================================================
        // GetById / GetAll
        // =========================================================================

        [TestMethod]
        [Description("GetById returns the order when it exists.")]
        public void GetById_WithExistingOrderId_ReturnsOrder()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);

            var result = svc.GetById(orderId);

            Assert.IsNotNull(result);
            Assert.AreEqual(orderId, result.OrderId);
        }

        [TestMethod]
        [Description("GetById returns null for an order ID that does not exist.")]
        public void GetById_WithNonExistentOrderId_ReturnsNull()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var result = svc.GetById(99999);

            Assert.IsNull(result);
        }

        [TestMethod]
        [Description("GetById includes the Lines collection.")]
        public void GetById_IncludesLines()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);

            var result = svc.GetById(orderId);

            Assert.IsNotNull(result.Lines, "Lines should be eagerly loaded.");
            Assert.AreEqual(1, result.Lines.Count);
        }

        [TestMethod]
        [Description("GetAll returns all orders ordered by CreatedUtc descending.")]
        public void GetAll_ReturnsOrdersDescendingByCreatedUtc()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var o1 = svc.CreateOrder("Alpha", "A", "AA",
                new List<OrderLine> { new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 1 } });
            System.Threading.Thread.Sleep(10); // ensure distinct timestamps
            var o2 = svc.CreateOrder("Beta", "B", "BB",
                new List<OrderLine> { new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 1 } });

            var all = svc.GetAll();

            Assert.IsTrue(all.Count >= 2);
            // Most recently created should be first.
            Assert.AreEqual(o2.OrderId, all[0].OrderId);
        }

        // =========================================================================
        // Allocate — valid transition
        // =========================================================================

        [TestMethod]
        [Description("Allocating a Pending order moves it to Allocated status.")]
        public void Allocate_PendingOrder_ChangesStatusToAllocated()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);

            var result = svc.Allocate(orderId);

            Assert.AreEqual("Allocated", result.Status);
        }

        [TestMethod]
        [Description("Allocating a Pending order increments QuantityReserved on the inventory item.")]
        public void Allocate_PendingOrder_IncrementsQuantityReserved()
        {
            int qty = 3;
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, qty);
            var svc = new OrderService(ctx);

            svc.Allocate(orderId);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);

            Assert.AreEqual(qty, item.QuantityReserved,
                "QuantityReserved should increase by the ordered quantity.");
        }

        [TestMethod]
        [Description("Allocating a Pending order does NOT decrement QuantityOnHand (stock is only reserved, not consumed).")]
        public void Allocate_PendingOrder_DoesNotDecrementQuantityOnHand()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, 5);
            var svc = new OrderService(ctx);
            var before = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId).QuantityOnHand;

            svc.Allocate(orderId);

            var after = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId).QuantityOnHand;
            Assert.AreEqual(before, after,
                "QuantityOnHand must not change during Allocate — only QuantityReserved increases.");
        }

        [TestMethod]
        [Description("Allocating an order that exactly exhausts available stock succeeds.")]
        public void Allocate_WithExactAvailableStock_Succeeds()
        {
            // Pump: QuantityOnHand=25, QuantityReserved=0 → available=25
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, 25);
            var svc = new OrderService(ctx);

            var result = svc.Allocate(orderId);

            Assert.AreEqual("Allocated", result.Status);
        }

        // =========================================================================
        // Allocate — invalid transitions
        // =========================================================================

        [TestMethod]
        [Description("Allocating an already-Allocated order throws an Exception with specific message.")]
        public void Allocate_AlreadyAllocatedOrder_ThrowsException()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId); // first allocation succeeds

            var ex = Assert.ThrowsException<Exception>(() => svc.Allocate(orderId));

            Assert.AreEqual("Only Pending orders can be allocated.", ex.Message);
        }

        [TestMethod]
        [Description("Allocating a Shipped order throws an Exception.")]
        public void Allocate_ShippedOrder_ThrowsException()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);
            svc.Ship(orderId, "DHL", "TRK-001");

            var ex = Assert.ThrowsException<Exception>(() => svc.Allocate(orderId));

            Assert.AreEqual("Only Pending orders can be allocated.", ex.Message);
        }

        [TestMethod]
        [Description("Allocating an order with a manually-set Cancelled status throws an Exception.")]
        public void Allocate_CancelledOrder_ThrowsException()
        {
            // KNOWN ISSUE: There is no Cancel() method on OrderService.  Cancelled status
            //   can only be reached by directly manipulating the data.  This test
            //   documents the guard behaviour for that status value.
            var ctx = DbContextFactory.CreateSeeded();
            var order = new Order
            {
                OrderNumber        = "ORD-CANCELLED",
                CustomerName       = "X",
                DestinationCity    = "X",
                DestinationCountry = "X",
                Status             = "Cancelled",
                CreatedUtc         = DateTime.UtcNow,
                Lines              = new List<OrderLine>
                {
                    new OrderLine
                    {
                        ProductId        = DbContextFactory.ProductPumpId,
                        Quantity         = 1,
                        UnitPriceAtOrder = 1240.00m
                    }
                }
            };
            ctx.Orders.Add(order);
            ctx.SaveChanges();

            var svc = new OrderService(ctx);
            var ex = Assert.ThrowsException<Exception>(() => svc.Allocate(order.OrderId));

            Assert.AreEqual("Only Pending orders can be allocated.", ex.Message);
        }

        [TestMethod]
        [Description("Allocating an order when stock is insufficient throws an Exception naming the product.")]
        public void Allocate_InsufficientStock_ThrowsException()
        {
            // Pump: available=25; request 26.
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, 26);
            var svc = new OrderService(ctx);

            var ex = Assert.ThrowsException<Exception>(() => svc.Allocate(orderId));

            StringAssert.Contains(ex.Message, "Insufficient stock for product");
            StringAssert.Contains(ex.Message, DbContextFactory.ProductPumpId.ToString());
        }

        [TestMethod]
        [Description("Available stock calculation is OnHand minus Reserved; two sequential allocations deplete correctly.")]
        public void Allocate_TwoSequentialOrders_ReservationsAreAdditive()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            // First order: reserve 20 of 25 available pumps
            var o1 = svc.CreateOrder("C1", "Berlin", "DE",
                new List<OrderLine> { new OrderLine { ProductId = DbContextFactory.ProductPumpId, Quantity = 20 } });
            svc.Allocate(o1.OrderId);

            // Second order: reserve 5 (exactly the remaining 5)
            var o2 = svc.CreateOrder("C2", "Berlin", "DE",
                new List<OrderLine> { new OrderLine { ProductId = DbContextFactory.ProductPumpId, Quantity = 5 } });
            svc.Allocate(o2.OrderId);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);

            Assert.AreEqual(25, item.QuantityReserved,
                "Total reserved should equal the sum of both allocated orders.");
        }

        [TestMethod]
        [Description("Allocating order for a product absent from the default warehouse throws InvalidOperationException (not Exception).")]
        public void Allocate_ProductNotInDefaultWarehouse_ThrowsInvalidOperationException()
        {
            // KNOWN ISSUE: Allocate calls InventoryItems.First(...) which throws
            //   InvalidOperationException (Sequence contains no elements) when the
            //   product is not stocked in the default warehouse — not a friendly
            //   Exception with a descriptive message.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            // ProductBearingId (3) is in WH-CENTRAL but NOT in WH-WEST.
            // Insert an order that references a product with no inventory in WH-CENTRAL.
            // We simulate this by removing the bearing inventory row.
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductBearingId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            ctx.InventoryItems.Remove(item);
            ctx.SaveChanges();

            var order = svc.CreateOrder("C", "City", "DE",
                new List<OrderLine>
                {
                    new OrderLine { ProductId = DbContextFactory.ProductBearingId, Quantity = 1 }
                });

            Assert.ThrowsException<InvalidOperationException>(() => svc.Allocate(order.OrderId));
        }

        // =========================================================================
        // Ship — valid transition
        // =========================================================================

        [TestMethod]
        [Description("Shipping an Allocated order moves it to Shipped status and returns a Shipment.")]
        public void Ship_AllocatedOrder_ChangesStatusToShipped()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);

            var shipment = svc.Ship(orderId, "DHL", "TRK-001");

            var order = ctx.Orders.Find(orderId);
            Assert.AreEqual("Shipped", order.Status);
            Assert.IsNotNull(shipment);
        }

        [TestMethod]
        [Description("Shipping decrements both QuantityOnHand and QuantityReserved by the ordered quantity.")]
        public void Ship_AllocatedOrder_DecrementsOnHandAndReserved()
        {
            int qty = 4;
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, qty);
            var svc = new OrderService(ctx);

            var itemBefore = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            int onHandBefore   = itemBefore.QuantityOnHand;
            int reservedBefore = itemBefore.QuantityReserved; // 0 before Allocate

            svc.Allocate(orderId);
            svc.Ship(orderId, "FedEx", "TRK-002");

            var itemAfter = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);

            Assert.AreEqual(onHandBefore - qty, itemAfter.QuantityOnHand,
                "QuantityOnHand should decrease by the shipped quantity.");
            Assert.AreEqual(reservedBefore, itemAfter.QuantityReserved,
                "QuantityReserved should return to its pre-allocation value after shipment.");
        }

        [TestMethod]
        [Description("The returned Shipment record carries the supplied carrier and tracking number.")]
        public void Ship_ReturnsShipmentWithCarrierAndTrackingNumber()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);

            var shipment = svc.Ship(orderId, "UPS", "TRK-UPS-999");

            Assert.AreEqual("UPS", shipment.Carrier);
            Assert.AreEqual("TRK-UPS-999", shipment.TrackingNumber);
        }

        [TestMethod]
        [Description("The Shipment's FromWarehouseCode equals the DefaultWarehouseCode appSetting.")]
        public void Ship_SetsFromWarehouseCode_ToDefaultWarehouse()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);

            var shipment = svc.Ship(orderId, "DHL", "TRK-X");

            Assert.AreEqual(DbContextFactory.DefaultWarehouseCode, shipment.FromWarehouseCode);
        }

        [TestMethod]
        [Description("Shipment TotalWeightKg equals UnitWeightKg * Quantity for a single-line order.")]
        public void Ship_CalculatesTotalWeight_CorrectlyForSingleLineOrder()
        {
            // Pump: UnitWeightKg=18.500, Quantity=3 → expected=55.500
            int qty = 3;
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, qty);
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);

            var shipment = svc.Ship(orderId, "DHL", "TRK-W");

            Assert.AreEqual(18.500m * qty, shipment.TotalWeightKg,
                "TotalWeightKg should be UnitWeightKg * Quantity.");
        }

        [TestMethod]
        [Description("Shipment TotalWeightKg is the sum across all lines for a multi-line order.")]
        public void Ship_CalculatesTotalWeight_CorrectlySumsMultipleLines()
        {
            // Pump (18.5kg × 2=37) + Valve (0.75kg × 4=3) = 40kg
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var order = svc.CreateOrder("C", "City", "DE", new List<OrderLine>
            {
                new OrderLine { ProductId = DbContextFactory.ProductPumpId,  Quantity = 2 },
                new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 4 }
            });
            svc.Allocate(order.OrderId);

            var shipment = svc.Ship(order.OrderId, "DHL", "TRK-MULTI");

            decimal expected = (18.500m * 2) + (0.750m * 4); // 37.0 + 3.0 = 40.0
            Assert.AreEqual(expected, shipment.TotalWeightKg);
        }

        [TestMethod]
        [Description("A Shipment row is persisted in the database after Ship completes.")]
        public void Ship_PersistsShipmentRow_InDatabase()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);

            var shipment = svc.Ship(orderId, "DHL", "TRK-DB");

            var persisted = ctx.Shipments.Find(shipment.ShipmentId);
            Assert.IsNotNull(persisted, "Shipment row should be saved to the database.");
            Assert.AreEqual(orderId, persisted.OrderId);
        }

        // =========================================================================
        // Ship — invalid transitions
        // =========================================================================

        [TestMethod]
        [Description("Shipping a Pending (non-Allocated) order throws an Exception.")]
        public void Ship_PendingOrder_ThrowsException()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);

            var ex = Assert.ThrowsException<Exception>(() => svc.Ship(orderId, "DHL", "TRK"));

            Assert.AreEqual("Only Allocated orders can be shipped.", ex.Message);
        }

        [TestMethod]
        [Description("Shipping an already-Shipped order throws an Exception.")]
        public void Ship_AlreadyShippedOrder_ThrowsException()
        {
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder();
            var svc = new OrderService(ctx);
            svc.Allocate(orderId);
            svc.Ship(orderId, "DHL", "TRK-1");

            var ex = Assert.ThrowsException<Exception>(() => svc.Ship(orderId, "DHL", "TRK-2"));

            Assert.AreEqual("Only Allocated orders can be shipped.", ex.Message);
        }

        // =========================================================================
        // Cancellation — no Cancel method exists
        // =========================================================================

        [TestMethod]
        [Description("OrderService has no Cancel method — the Cancelled status documented in Order.cs is unreachable via the service API.")]
        public void Cancel_NoPublicMethodExists_OnOrderService()
        {
            // KNOWN ISSUE: The Order model documents a Cancelled status in its
            //   comment (Pending -> Allocated -> Shipped -> Cancelled) but OrderService
            //   has no Cancel() method.  An order can only reach Cancelled status by
            //   direct database manipulation.  This test ensures no silent cancel API
            //   exists, pinning the absence of that feature.
            var serviceType = typeof(OrderService);
            var cancelMethod = serviceType.GetMethod("Cancel",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            Assert.IsNull(cancelMethod,
                "OrderService must not have a public Cancel method in the current codebase.");
        }

        // =========================================================================
        // Full happy-path lifecycle
        // =========================================================================

        [TestMethod]
        [Description("Full lifecycle: CreateOrder -> Allocate -> Ship produces consistent stock and status.")]
        public void FullLifecycle_PendingToAllocatedToShipped_IsConsistent()
        {
            int qty = 5;
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            var order = svc.CreateOrder("Lifecycle Corp", "London", "UK",
                new List<OrderLine>
                {
                    new OrderLine { ProductId = DbContextFactory.ProductPumpId, Quantity = qty }
                });

            Assert.AreEqual("Pending", order.Status);

            svc.Allocate(order.OrderId);
            Assert.AreEqual("Allocated", ctx.Orders.Find(order.OrderId).Status);

            var shipment = svc.Ship(order.OrderId, "FedEx", "TRK-LIFE");
            Assert.AreEqual("Shipped", ctx.Orders.Find(order.OrderId).Status);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);

            Assert.AreEqual(25 - qty, item.QuantityOnHand,
                "OnHand should drop by the shipped quantity.");
            Assert.AreEqual(0, item.QuantityReserved,
                "Reserved should return to zero after shipment.");
        }
    }
}
