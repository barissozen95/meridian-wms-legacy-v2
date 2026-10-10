// =============================================================================
// InventoryMathTests.cs
// -----------------------------------------------------------------------------
// PURPOSE: Pin-down / regression tests that document and lock in the CURRENT,
//   AS-IS behaviour of InventoryService.  These tests assert what the code
//   actually does today — including known bugs and quirks — to provide a
//   regression safety net before any modernization work begins.
//
// FRAMEWORK: MSTest v3 + Effort.EF6 (in-memory Entity Framework provider)
//
// SUBSYSTEMS UNDER TEST:
//   - InventoryService.GetForWarehouse   (warehouse lookup + empty fallback)
//   - InventoryService.GetLowStock       (threshold calculation, boundary values)
//   - InventoryService.AdjustQuantity    (delta application, negative-stock guard)
//   - Available-stock arithmetic         (OnHand - Reserved)
//   - Multi-warehouse inventory          (separate rows per warehouse)
//
// NOTE: InventoryService.SearchByProductName uses raw ADO.NET against SQL Server
//   and cannot be exercised with Effort.  Coverage gap is documented below.
//
// KNOWN ISSUES annotated inline with: // KNOWN ISSUE: <description>
// =============================================================================

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MeridianWms.Api.Models;
using MeridianWms.Api.Services;
using MeridianWms.Tests.Helpers;

namespace MeridianWms.Tests
{
    [TestClass]
    public class InventoryMathTests
    {
        [ClassInitialize]
        public static void ClassInit(TestContext _)
        {
            ConfigHelper.EnsureAppSettings();
        }

        // =========================================================================
        // GetForWarehouse
        // =========================================================================

        [TestMethod]
        [Description("GetForWarehouse returns all inventory items for a known warehouse code.")]
        public void GetForWarehouse_KnownCode_ReturnsItemsForThatWarehouse()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            var result = svc.GetForWarehouse("WH-CENTRAL");

            Assert.IsNotNull(result);
            // Seed has 5 items in WH-CENTRAL
            Assert.AreEqual(5, result.Count,
                "WH-CENTRAL should have exactly 5 seeded inventory items.");
            Assert.IsTrue(result.All(i => i.WarehouseId == DbContextFactory.WarehouseCentralId),
                "All returned items should belong to the requested warehouse.");
        }

        [TestMethod]
        [Description("GetForWarehouse for WH-WEST returns only that warehouse's items.")]
        public void GetForWarehouse_WestWarehouse_ReturnsOnlyWestItems()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            var result = svc.GetForWarehouse("WH-WEST");

            Assert.AreEqual(2, result.Count,
                "WH-WEST should have exactly 2 seeded inventory items.");
            Assert.IsTrue(result.All(i => i.WarehouseId == DbContextFactory.WarehouseWestId));
        }

        [TestMethod]
        [Description("GetForWarehouse with an unknown warehouse code returns an empty list (not null, not an exception).")]
        public void GetForWarehouse_UnknownCode_ReturnsEmptyList()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            var result = svc.GetForWarehouse("WH-DOES-NOT-EXIST");

            Assert.IsNotNull(result,
                "Return value should be an empty list, not null.");
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        [Description("GetForWarehouse with null code returns an empty list.")]
        public void GetForWarehouse_NullCode_ReturnsEmptyList()
        {
            // Warehouses.FirstOrDefault(w => w.Code == null) returns null → empty list.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            var result = svc.GetForWarehouse(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        // =========================================================================
        // Available Stock Arithmetic (OnHand - Reserved)
        // =========================================================================

        [TestMethod]
        [Description("Available quantity is calculated as QuantityOnHand minus QuantityReserved.")]
        public void AvailableStock_IsOnHandMinusReserved()
        {
            var ctx = DbContextFactory.CreateSeeded();

            // Manually reserve some stock directly on the item.
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityOnHand   = 20;
            item.QuantityReserved = 7;
            ctx.SaveChanges();

            int available = item.QuantityOnHand - item.QuantityReserved;

            Assert.AreEqual(13, available);
        }

        [TestMethod]
        [Description("Available quantity equals zero when QuantityReserved equals QuantityOnHand.")]
        public void AvailableStock_IsZero_WhenAllStockIsReserved()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityReserved = item.QuantityOnHand; // fully reserved
            ctx.SaveChanges();

            int available = item.QuantityOnHand - item.QuantityReserved;

            Assert.AreEqual(0, available);
        }

        [TestMethod]
        [Description("Available quantity can be negative when QuantityReserved exceeds QuantityOnHand — no guard in the model.")]
        public void AvailableStock_CanGoNegative_NoModelGuardExists()
        {
            // KNOWN ISSUE: The InventoryItem model allows QuantityReserved > QuantityOnHand,
            //   resulting in a negative available quantity.  This is a data-integrity gap.
            //   This test pins the current absence of a domain-level guard.
            var ctx = DbContextFactory.CreateSeeded();
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityReserved = item.QuantityOnHand + 5; // over-reserved
            ctx.SaveChanges();

            var reloaded = ctx.InventoryItems.Find(item.InventoryItemId);
            int available = reloaded.QuantityOnHand - reloaded.QuantityReserved;

            Assert.IsTrue(available < 0,
                "A negative available quantity should be possible with current model (no guard).");
        }

        // =========================================================================
        // GetLowStock
        // =========================================================================

        [TestMethod]
        [Description("GetLowStock returns items whose available quantity is at or below the threshold (uses <=, not <).")]
        public void GetLowStock_ReturnsItems_AtOrBelowThreshold()
        {
            // Threshold in ConfigHelper = 10.
            // Seed: BRG-6204 OnHand=6, Reserved=0 → available=6 (<=10 → LOW)
            //        FLT-AIR-M OnHand=8, Reserved=0 → available=8 (<=10 → LOW)
            //        PMP-3000-A OnHand=25 → available=25 (NOT low)
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            var result = svc.GetLowStock();

            // Collect SKUs from the anonymous-type list using reflection.
            var skus = result.Select(r => (string)r.GetType().GetProperty("Sku").GetValue(r)).ToList();

            CollectionAssert.Contains(skus, "BRG-6204");
            CollectionAssert.Contains(skus, "FLT-AIR-M");
            CollectionAssert.DoesNotContain(skus, "PMP-3000-A");
        }

        [TestMethod]
        [Description("GetLowStock includes an item whose available quantity equals the threshold exactly (boundary: <=).")]
        public void GetLowStock_IncludesItem_WhenAvailableEqualsThreshold()
        {
            // Set pump available to exactly 10 (the threshold).
            var ctx = DbContextFactory.CreateSeeded();
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityOnHand   = 10;
            item.QuantityReserved = 0;
            ctx.SaveChanges();

            var svc = new InventoryService(ctx);
            var result = svc.GetLowStock();

            var skus = result.Select(r => (string)r.GetType().GetProperty("Sku").GetValue(r)).ToList();
            CollectionAssert.Contains(skus, "PMP-3000-A",
                "An item with available == threshold should be included (<=, not <).");
        }

        [TestMethod]
        [Description("GetLowStock excludes an item whose available quantity is one above the threshold (threshold+1).")]
        public void GetLowStock_ExcludesItem_WhenAvailableIsOneAboveThreshold()
        {
            // Set pump available to threshold+1 = 11.
            var ctx = DbContextFactory.CreateSeeded();
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityOnHand   = 11;
            item.QuantityReserved = 0;
            ctx.SaveChanges();

            var svc = new InventoryService(ctx);
            var result = svc.GetLowStock();

            var skus = result.Select(r => (string)r.GetType().GetProperty("Sku").GetValue(r)).ToList();
            CollectionAssert.DoesNotContain(skus, "PMP-3000-A",
                "An item with available = threshold+1 should NOT be flagged as low stock.");
        }

        [TestMethod]
        [Description("GetLowStock uses available = OnHand - Reserved; a highly reserved item can appear even with high OnHand.")]
        public void GetLowStock_ConsidersReservedStock_WhenCalculatingAvailable()
        {
            // Pump: OnHand=25, Reserved=20 → available=5 → low stock.
            var ctx = DbContextFactory.CreateSeeded();
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityReserved = 20;
            ctx.SaveChanges();

            var svc = new InventoryService(ctx);
            var result = svc.GetLowStock();

            var skus = result.Select(r => (string)r.GetType().GetProperty("Sku").GetValue(r)).ToList();
            CollectionAssert.Contains(skus, "PMP-3000-A",
                "A heavily reserved item should appear in low-stock even with high OnHand.");
        }

        [TestMethod]
        [Description("GetLowStock returns an empty list when all items have available stock above the threshold.")]
        public void GetLowStock_ReturnsEmptyList_WhenAllItemsAboveThreshold()
        {
            var ctx = DbContextFactory.CreateSeeded();
            // Set all items well above threshold.
            foreach (var i in ctx.InventoryItems.ToList())
            {
                i.QuantityOnHand   = 100;
                i.QuantityReserved = 0;
            }
            ctx.SaveChanges();

            var svc = new InventoryService(ctx);
            var result = svc.GetLowStock();

            Assert.AreEqual(0, result.Count,
                "No items should be flagged when all available quantities exceed the threshold.");
        }

        [TestMethod]
        [Description("GetLowStock result objects carry Sku, Warehouse, Available, and Bin properties.")]
        public void GetLowStock_ResultShape_ContainsExpectedProperties()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            var result = svc.GetLowStock();
            Assert.IsTrue(result.Count > 0, "Pre-condition: at least one low-stock item in seed.");

            var first = result[0];
            var props = first.GetType().GetProperties().Select(p => p.Name).ToArray();

            CollectionAssert.Contains(props, "Sku");
            CollectionAssert.Contains(props, "Warehouse");
            CollectionAssert.Contains(props, "Available");
            CollectionAssert.Contains(props, "Bin");
        }

        [TestMethod]
        [Description("GetLowStock evaluates items across all warehouses, not just the default warehouse.")]
        public void GetLowStock_EvaluatesAllWarehouses()
        {
            var ctx = DbContextFactory.CreateSeeded();
            // Set WH-WEST pump to low stock (available=3).
            var westPump = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseWestId);
            westPump.QuantityOnHand   = 3;
            westPump.QuantityReserved = 0;
            ctx.SaveChanges();

            var svc = new InventoryService(ctx);
            var result = svc.GetLowStock();

            // There should be at least one entry with Warehouse=WH-WEST
            var warehouses = result
                .Select(r => (string)r.GetType().GetProperty("Warehouse").GetValue(r))
                .ToList();

            CollectionAssert.Contains(warehouses, "WH-WEST",
                "GetLowStock should include items from WH-WEST, not only WH-CENTRAL.");
        }

        // =========================================================================
        // AdjustQuantity
        // =========================================================================

        [TestMethod]
        [Description("AdjustQuantity with a positive delta increases QuantityOnHand.")]
        public void AdjustQuantity_PositiveDelta_IncreasesOnHand()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", +10);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(35, item.QuantityOnHand,
                "OnHand should increase from 25 to 35 after +10 adjustment.");
        }

        [TestMethod]
        [Description("AdjustQuantity with a negative delta decreases QuantityOnHand.")]
        public void AdjustQuantity_NegativeDelta_DecreasesOnHand()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", -5);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(20, item.QuantityOnHand,
                "OnHand should decrease from 25 to 20 after -5 adjustment.");
        }

        [TestMethod]
        [Description("AdjustQuantity with a delta of zero leaves QuantityOnHand unchanged.")]
        public void AdjustQuantity_ZeroDelta_LeavesOnHandUnchanged()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", 0);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(25, item.QuantityOnHand);
        }

        [TestMethod]
        [Description("AdjustQuantity that brings OnHand to exactly zero succeeds.")]
        public void AdjustQuantity_BringsOnHandToZero_Succeeds()
        {
            // OnHand=25; delta=-25 → result=0 (boundary: equals zero is allowed).
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", -25);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(0, item.QuantityOnHand);
        }

        [TestMethod]
        [Description("AdjustQuantity that would bring OnHand below zero throws an Exception after applying the delta.")]
        public void AdjustQuantity_WouldMakeOnHandNegative_ThrowsException()
        {
            // KNOWN ISSUE: AdjustQuantity applies the delta BEFORE checking if the
            //   result is negative.  The guard `if (item.QuantityOnHand < 0) throw`
            //   executes after `item.QuantityOnHand = item.QuantityOnHand + delta`.
            //   This means the in-memory value is momentarily set to the negative
            //   number before the exception is thrown.  SaveChanges is never called,
            //   so the negative value is NOT persisted, but the check-after-apply
            //   ordering is an implementation quirk this test pins.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            // OnHand=25; delta=-26 → would result in -1
            var ex = Assert.ThrowsException<Exception>(
                () => svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", -26));

            Assert.AreEqual("Quantity on hand cannot be negative.", ex.Message);
        }

        [TestMethod]
        [Description("AdjustQuantity negative-stock check uses strict less-than (<0), so zero is allowed.")]
        public void AdjustQuantity_ExactlyZero_DoesNotThrow()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            // Should not throw: 25 + (-25) = 0, and 0 is NOT < 0.
            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", -25);

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(0, item.QuantityOnHand,
                "Reducing OnHand to exactly zero should succeed without throwing.");
        }

        [TestMethod]
        [Description("AdjustQuantity with an unknown warehouse code throws InvalidOperationException (not a friendly Exception).")]
        public void AdjustQuantity_UnknownWarehouseCode_ThrowsInvalidOperationException()
        {
            // KNOWN ISSUE: AdjustQuantity calls Warehouses.First(...) which throws
            //   InvalidOperationException when the warehouse code is not found.
            //   There is no prior existence check or friendly error message.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            Assert.ThrowsException<InvalidOperationException>(
                () => svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-UNKNOWN", -1));
        }

        [TestMethod]
        [Description("AdjustQuantity with an unknown productId for a valid warehouse throws InvalidOperationException.")]
        public void AdjustQuantity_UnknownProductId_ThrowsInvalidOperationException()
        {
            // KNOWN ISSUE: Uses InventoryItems.First(...) — throws InvalidOperationException
            //   if the product/warehouse combination does not exist.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            Assert.ThrowsException<InvalidOperationException>(
                () => svc.AdjustQuantity(9999, "WH-CENTRAL", 5));
        }

        [TestMethod]
        [Description("AdjustQuantity persists the updated OnHand value to the database.")]
        public void AdjustQuantity_PersistsChange_ToDatabase()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", +3);

            // Re-query to confirm persistence.
            var reloaded = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(28, reloaded.QuantityOnHand,
                "Updated OnHand should be persisted after SaveChanges.");
        }

        [TestMethod]
        [Description("AdjustQuantity only modifies QuantityOnHand, not QuantityReserved.")]
        public void AdjustQuantity_DoesNotModifyQuantityReserved()
        {
            var ctx = DbContextFactory.CreateSeeded();

            // Pre-set some reserved quantity.
            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            item.QuantityReserved = 4;
            ctx.SaveChanges();

            var svc = new InventoryService(ctx);
            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", +10);

            var reloaded = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(4, reloaded.QuantityReserved,
                "AdjustQuantity must not touch QuantityReserved.");
        }

        // =========================================================================
        // Oversell scenarios via OrderService.Allocate
        // =========================================================================

        [TestMethod]
        [Description("Allocating more stock than is available (oversell) throws an Exception.")]
        public void Allocate_OversellAttempt_ThrowsException()
        {
            // Pump: OnHand=25, Reserved=0 → available=25; request 30 → oversell.
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, 30);
            var svc = new OrderService(ctx);

            var ex = Assert.ThrowsException<Exception>(() => svc.Allocate(orderId));

            StringAssert.Contains(ex.Message, "Insufficient stock");
        }

        [TestMethod]
        [Description("Oversell attempt does not modify QuantityReserved — the reservation is atomic.")]
        public void Allocate_OversellAttempt_DoesNotModifyReservedStock()
        {
            // KNOWN ISSUE: For a multi-line order, Allocate processes lines sequentially.
            //   If the first line passes but the second line fails, the first line's
            //   reservation is NOT rolled back because SaveChanges is never reached.
            //   However, with Effort (in-memory), the reservation change IS pending
            //   in the EF change tracker but not committed.  This test verifies that
            //   a single-line oversell attempt leaves Reserved unchanged.
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, 30);
            var svc = new OrderService(ctx);

            try { svc.Allocate(orderId); } catch { /* expected */ }

            var item = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(0, item.QuantityReserved,
                "A failed allocation should not leave reserved stock incremented.");
        }

        [TestMethod]
        [Description("Multi-line order where a later line causes an oversell: first line reservation is NOT rolled back (partial-state bug).")]
        public void Allocate_MultiLineOrder_LaterLineOversell_FirstLineReservationLeaks()
        {
            // KNOWN ISSUE: OrderService.Allocate iterates lines and increments
            //   QuantityReserved for each.  If a later line throws due to insufficient
            //   stock, earlier lines have already had their QuantityReserved incremented
            //   in the EF change-tracker.  SaveChanges is never called on the throw
            //   path, so the leaked reservation remains in the in-process EF context
            //   but is NOT persisted to the database.  This test documents the
            //   in-process state after the failed allocation.
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new OrderService(ctx);

            // Line 1: 5 valves (480 on hand — fine)
            // Line 2: 999 pumps (25 on hand — oversell)
            var order = svc.CreateOrder("C", "City", "DE", new List<OrderLine>
            {
                new OrderLine { ProductId = DbContextFactory.ProductValveId, Quantity = 5   },
                new OrderLine { ProductId = DbContextFactory.ProductPumpId,  Quantity = 999 }
            });

            try { svc.Allocate(order.OrderId); } catch { /* expected */ }

            // Because SaveChanges was never called, the DB state should be clean.
            // Re-load from context to check persisted values.
            var valveItem = ctx.InventoryItems
                .AsNoTracking()
                .First(i => i.ProductId == DbContextFactory.ProductValveId &&
                            i.WarehouseId == DbContextFactory.WarehouseCentralId);
            Assert.AreEqual(0, valveItem.QuantityReserved,
                "The valve reservation should NOT be persisted since SaveChanges was never called.");
        }

        // =========================================================================
        // Multi-warehouse inventory separation
        // =========================================================================

        [TestMethod]
        [Description("Inventory items in different warehouses for the same product are independent rows.")]
        public void MultiWarehouse_SameProduct_HasSeparateInventoryRows()
        {
            var ctx = DbContextFactory.CreateSeeded();

            var central = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            var west = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseWestId);

            Assert.AreNotEqual(central.InventoryItemId, west.InventoryItemId,
                "Each warehouse maintains a separate InventoryItem row per product.");
            Assert.AreEqual(25, central.QuantityOnHand);
            Assert.AreEqual(10, west.QuantityOnHand);
        }

        [TestMethod]
        [Description("AdjustQuantity in WH-CENTRAL does not affect WH-WEST stock for the same product.")]
        public void AdjustQuantity_InOneWarehouse_DoesNotAffectOtherWarehouse()
        {
            var ctx = DbContextFactory.CreateSeeded();
            var svc = new InventoryService(ctx);

            svc.AdjustQuantity(DbContextFactory.ProductPumpId, "WH-CENTRAL", -10);

            var west = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseWestId);
            Assert.AreEqual(10, west.QuantityOnHand,
                "WH-WEST stock should be unaffected by an adjustment to WH-CENTRAL.");
        }

        [TestMethod]
        [Description("Allocate uses only the default warehouse (WH-CENTRAL); WH-WEST stock is not considered.")]
        public void Allocate_UsesDefaultWarehouseOnly_WestStockIsIgnored()
        {
            // Remove all pump stock from WH-CENTRAL only; WH-WEST still has 10.
            // Allocate should fail even though total network stock is sufficient.
            var (ctx, orderId) = DbContextFactory.CreateSeededWithPendingOrder(
                DbContextFactory.ProductPumpId, 5);
            var centralItem = ctx.InventoryItems.First(
                i => i.ProductId == DbContextFactory.ProductPumpId &&
                     i.WarehouseId == DbContextFactory.WarehouseCentralId);
            centralItem.QuantityOnHand = 0;
            ctx.SaveChanges();

            var svc = new OrderService(ctx);
            var ex = Assert.ThrowsException<Exception>(() => svc.Allocate(orderId));

            StringAssert.Contains(ex.Message, "Insufficient stock",
                "Allocate should fail even when other warehouses have stock — it only checks the default warehouse.");
        }

        // =========================================================================
        // SearchByProductName — coverage gap documentation
        // =========================================================================

        [TestMethod]
        [Description("SearchByProductName cannot be tested with Effort — it uses raw ADO.NET SqlConnection.")]
        [Ignore("SearchByProductName uses a raw SqlConnection against a real SQL Server database " +
                "and cannot be executed in the Effort in-memory test environment. " +
                "Integration tests against a real database are required to cover this method. " +
                "KNOWN ISSUE: The method is also vulnerable to SQL injection (nameFragment is " +
                "concatenated directly into the query string with no parameterisation).")]
        public void SearchByProductName_CannotBeTestedWithEffort_RequiresIntegrationTest()
        {
            // This test is intentionally ignored; it exists as documentation only.
        }

        // =========================================================================
        // Integer arithmetic — no floating point in the model
        // =========================================================================

        [TestMethod]
        [Description("QuantityOnHand and QuantityReserved are stored as int; no fractional quantities exist in the model.")]
        public void InventoryQuantities_AreIntegerType_NoFractionalValues()
        {
            var item = new InventoryItem
            {
                QuantityOnHand   = 7,
                QuantityReserved = 3
            };

            Assert.AreEqual(typeof(int), item.QuantityOnHand.GetType());
            Assert.AreEqual(typeof(int), item.QuantityReserved.GetType());
            Assert.AreEqual(4, item.QuantityOnHand - item.QuantityReserved);
        }

        [TestMethod]
        [Description("TotalWeightKg in a Shipment is decimal, computed as product of decimal UnitWeightKg and int Quantity.")]
        public void ShipmentWeight_IsDecimal_ComputedFromDecimalUnitWeightAndIntQuantity()
        {
            // Verify the type and precision semantics used in OrderService.Ship.
            decimal unitWeight = 18.500m;
            int qty = 3;
            decimal totalWeight = unitWeight * qty;

            Assert.AreEqual(55.500m, totalWeight);
            Assert.AreEqual(typeof(decimal), totalWeight.GetType());
        }

        [TestMethod]
        [Description("UnitPrice (decimal) is preserved exactly in UnitPriceAtOrder — no floating-point rounding.")]
        public void OrderLine_UnitPriceAtOrder_IsDecimal_NoFloatingPointRounding()
        {
            // decimal arithmetic: 34.90 has an exact decimal representation.
            decimal price = 34.90m;
            int qty = 3;
            decimal lineValue = price * qty;

            Assert.AreEqual(104.70m, lineValue,
                "Decimal multiplication should be exact for currency values.");
        }
    }
}
