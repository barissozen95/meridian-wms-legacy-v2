# TESTS_README.md — Meridian WMS Regression Pin-Down Suite

## Purpose

This test suite was created **before any modernization or refactoring work begins**.  
Its goal is not to test correct or ideal behaviour, but to **document and lock in what the system actually does today** — including edge cases, boundary conditions, quirks, and known bugs.

If a future code change causes a test to fail, that is the safety net working: something observable changed.  Review whether the change was intentional before updating any test assertions.

---

## What Was Tested and Why

### `OrderLifecycleTests.cs`

Tests the `OrderService` class end-to-end through an in-memory Entity Framework 6 context (Effort).

| Area | Description |
|---|---|
| **CreateOrder — valid inputs** | Status defaults to `"Pending"`, `OrderNumber` has `ORD-` prefix, `UnitPriceAtOrder` is snapshotted from the product catalogue. |
| **CreateOrder — edge cases** | Unknown `ProductId`, empty lines list, zero-quantity lines, negative-quantity lines, `null` lines list. |
| **CreateOrder — quirks** | `CreatedUtc` is set via `DateTime.Now` (local time) despite the field name implying UTC; no duplicate-order guard exists. |
| **GetById / GetAll** | Null return for unknown ID; lines are eagerly loaded; `GetAll` orders by `CreatedUtc` descending. |
| **Allocate — valid** | `Pending → Allocated`; `QuantityReserved` increments; `QuantityOnHand` is not decremented during allocation; exact-stock boundary. |
| **Allocate — invalid** | Already-allocated; Shipped; Cancelled (status set directly via data); insufficient stock; product absent from default warehouse. |
| **Allocate — sequential** | Two sequential allocations deplete available stock additively. |
| **Ship — valid** | `Allocated → Shipped`; both `QuantityOnHand` and `QuantityReserved` decrement; carrier/tracking persisted; `FromWarehouseCode` is the default warehouse; weight calculation single-line and multi-line. |
| **Ship — invalid** | `Pending` order; already-Shipped order. |
| **Cancellation** | No `Cancel()` method exists on `OrderService`; test pins the absence. |
| **Full lifecycle** | Complete `Pending → Allocated → Shipped` flow with stock consistency assertions. |

### `InventoryMathTests.cs`

Tests the `InventoryService` class and inventory arithmetic semantics.

| Area | Description |
|---|---|
| **GetForWarehouse** | Known code returns correct items; unknown code returns empty list; `null` code returns empty list; per-warehouse isolation. |
| **Available stock arithmetic** | `OnHand - Reserved`; zero available; negative available is possible with no model guard. |
| **GetLowStock** | Items at or below threshold are included (≤, not <); boundary: exactly threshold included; threshold+1 excluded; reserved stock considered; all-above-threshold returns empty; result shape (Sku, Warehouse, Available, Bin); evaluates all warehouses. |
| **AdjustQuantity** | Positive delta; negative delta; zero delta; brings to exactly zero (allowed); brings below zero (throws); strict `< 0` guard; persisted to DB; `QuantityReserved` not modified. |
| **AdjustQuantity — error paths** | Unknown warehouse code (`InvalidOperationException`); unknown product ID (`InvalidOperationException`). |
| **Oversell via Allocate** | Single-line oversell throws; no reservation leaks on failure; multi-line partial-state documented. |
| **Multi-warehouse** | Same product has separate rows per warehouse; adjustment in one does not affect the other; `Allocate` uses only the default warehouse. |
| **SearchByProductName** | Marked `[Ignore]` — see coverage gap below. |
| **Integer / decimal arithmetic** | Quantities are `int`; weight and price are `decimal`; no floating-point rounding. |

---

## How to Run

### Prerequisites

- .NET Framework 4.8 SDK (or Visual Studio 2019/2022)
- NuGet package restore (run once before first build)

### Restore packages

```
nuget restore MeridianWms.sln
```

Or in Visual Studio: **Build → Restore NuGet Packages**.

### Build

```
msbuild MeridianWms.sln /p:Configuration=Debug
```

### Run all tests (VS Test Console)

```
vstest.console.exe src\MeridianWms.Tests\bin\Debug\MeridianWms.Tests.dll
```

### Run via MSTest runner

```
mstest /testcontainer:src\MeridianWms.Tests\bin\Debug\MeridianWms.Tests.dll
```

### Run in Visual Studio

Open `MeridianWms.sln` → **Test → Run All Tests** (Ctrl+R, A).

### NuGet packages required by the test project

| Package | Version | Purpose |
|---|---|---|
| `Effort.EF6` | 2.1.12 | In-memory EF6 provider (transient SQL-less context) |
| `NMemory` | 2.1.12 | In-memory relational engine used by Effort |
| `MSTest.TestFramework` | 3.6.3 | Test framework |
| `MSTest.TestAdapter` | 3.6.3 | VS Test Explorer / vstest.console adapter |
| `EntityFramework` | 6.4.4 | Shared with the main project |

---

## KNOWN ISSUE Annotations

All known issues are marked in source with `// KNOWN ISSUE:` comments.  The complete list:

| # | File | Description |
|---|---|---|
| 1 | `OrderLifecycleTests.cs` — `CreateOrder_SetsCreatedUtc_ToApproximatelyNow` | `Order.CreatedUtc` is set via `DateTime.Now` (local time) despite the property name implying UTC. |
| 2 | `OrderLifecycleTests.cs` — `CreateOrder_AllowsDuplicateOrders_NoUniquenessConstraint` | No duplicate-order guard exists. Identical `CreateOrder` calls produce separate Order rows without error. |
| 3 | `OrderLifecycleTests.cs` — `CreateOrder_WithNullLinesList_ThrowsNullReferenceException` | Passing `null` for the `lines` parameter causes an unguarded `NullReferenceException`. |
| 4 | `OrderLifecycleTests.cs` — `CreateOrder_WithZeroQuantityLine_IsAcceptedWithoutError` | Zero-quantity lines are silently accepted and persisted — no validation. |
| 5 | `OrderLifecycleTests.cs` — `CreateOrder_WithNegativeQuantityLine_IsAcceptedWithoutError` | Negative-quantity lines are silently accepted — could corrupt reservations. |
| 6 | `OrderLifecycleTests.cs` — `Allocate_CancelledOrder_ThrowsException` | The `Cancelled` status is documented in `Order.cs` comments but `OrderService` has no `Cancel()` method. The status is unreachable via the service API. |
| 7 | `OrderLifecycleTests.cs` — `Allocate_ProductNotInDefaultWarehouse_ThrowsInvalidOperationException` | `Allocate` calls `InventoryItems.First(...)` which throws `InvalidOperationException` (Sequence contains no elements) — not a friendly, descriptive `Exception`. |
| 8 | `OrderLifecycleTests.cs` — `Cancel_NoPublicMethodExists_OnOrderService` | `OrderService` has no `Cancel()` method, making the `Cancelled` lifecycle state effectively unreachable from application code. |
| 9 | `InventoryMathTests.cs` — `AvailableStock_CanGoNegative_NoModelGuardExists` | `InventoryItem` allows `QuantityReserved > QuantityOnHand`, producing a negative "available" figure — no domain-level guard. |
| 10 | `InventoryMathTests.cs` — `AdjustQuantity_WouldMakeOnHandNegative_ThrowsException` | `AdjustQuantity` applies the delta **before** checking for negativity. The value is transiently set to a negative number in the EF change tracker before the exception is thrown. `SaveChanges` is not called so the value is not persisted, but the ordering is a quirk. |
| 11 | `InventoryMathTests.cs` — `AdjustQuantity_UnknownWarehouseCode_ThrowsInvalidOperationException` | `AdjustQuantity` calls `Warehouses.First(...)` which throws `InvalidOperationException` for unknown warehouse codes — no existence check or friendly error. |
| 12 | `InventoryMathTests.cs` — `AdjustQuantity_UnknownProductId_ThrowsInvalidOperationException` | Same as above for unknown product/warehouse combinations. |
| 13 | `InventoryMathTests.cs` — `Allocate_OversellAttempt_DoesNotModifyReservedStock` | On a multi-line order, the first line's `QuantityReserved` increment is applied in the EF change tracker before the second line fails. `SaveChanges` is not reached so it is not persisted, but partial in-process state exists. |
| 14 | `InventoryMathTests.cs` — `Allocate_MultiLineOrder_LaterLineOversell_FirstLineReservationLeaks` | Documents the above multi-line partial-state behaviour in detail. |
| 15 | `InventoryMathTests.cs` — `SearchByProductName_CannotBeTestedWithEffort_RequiresIntegrationTest` | `InventoryService.SearchByProductName` concatenates `nameFragment` directly into a SQL string — **SQL injection vulnerability**. Cannot be exercised with Effort; requires integration tests against a real database. |

---

## Coverage Gaps

| Area | Reason |
|---|---|
| `InventoryService.SearchByProductName` | Uses `new SqlConnection(...)` with a raw ADO.NET query. Effort provides an in-memory EF context but cannot intercept raw ADO.NET connections. Full coverage requires an integration test that targets a real SQL Server or SQL Server LocalDB instance. |
| `OrderService.CreateOrder` — `OrderNumber` uniqueness under concurrent calls | `OrderNumber = "ORD-" + DateTime.Now.ToString("yyyyMMddHHmmss")` has second-level granularity. Two simultaneous orders in the same second would produce the same `OrderNumber`. Testing concurrency reliably requires a load test harness, not a unit test. |
| Controllers (`OrdersController`, `InventoryController`) | The controllers are thin wrappers that catch `Exception` and return `BadRequest`. They were not included in this suite because all logic lives in the services. HTTP-level integration tests (using `Microsoft.AspNet.WebApi.Testing` or similar) would provide additional coverage but are out of scope for this pin-down suite. |
| Entity Framework migrations / schema | The project uses Code-First EF6 without migrations. The schema is maintained via `scripts/schema-sqlserver.sql`. No migration tests are included. |

---

## Source Changes Made to Support Testing

The following **minimal** changes were made to production source to enable the injected-context test pattern.  All changes are backward-compatible: the public API and runtime behaviour of the application are unchanged.

| File | Change |
|---|---|
| `src/MeridianWms.Api/Properties/AssemblyInfo.cs` | Added `[assembly: InternalsVisibleTo("MeridianWms.Tests")]` |
| `src/MeridianWms.Api/Data/WmsDbContext.cs` | Added `internal WmsDbContext(DbConnection, bool)` constructor (used by Effort) |
| `src/MeridianWms.Api/Services/OrderService.cs` | Added `internal OrderService(WmsDbContext db)` constructor (test seam) |
| `src/MeridianWms.Api/Services/InventoryService.cs` | Added `internal InventoryService(WmsDbContext db)` constructor (test seam) |
| `MeridianWms.sln` | Added `MeridianWms.Tests` project entry |
