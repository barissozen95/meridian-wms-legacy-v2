# Meridian WMS (Legacy) — .NET Framework 4.8 Web API

A small but realistic **Warehouse Management System** REST API for a logistics /
manufacturing distributor. This is the **"before"** codebase used in the IBM Bob
.NET Modernization workshop — the thing we modernize *from*.

It is deliberately built the way a 2016-era enterprise .NET app looks today:

| Concern            | This app uses (legacy)                                  |
|--------------------|---------------------------------------------------------|
| Runtime            | .NET Framework 4.8 (Windows-only)                       |
| Web stack          | ASP.NET Web API 2 (`System.Web`, `Global.asax`)         |
| Project format     | Old-style `.csproj` + `packages.config`                 |
| Data access        | Entity Framework 6 (synchronous) + raw `SqlClient`      |
| Database           | SQL Server (`System.Data.SqlClient`)                    |
| Configuration      | `Web.config` (`<connectionStrings>`, `<appSettings>`)   |
| Dependency wiring  | `new` everywhere — no dependency injection              |
| JSON               | Newtonsoft.Json 12                                      |

## Domain

The system tracks stock across warehouses and fulfils customer orders:

- **Product** — a SKU the company sells (pumps, valves, bearings, motors…).
- **Warehouse** — a physical site holding stock.
- **InventoryItem** — quantity of a Product in a Warehouse (on-hand + reserved).
- **Order** / **OrderLine** — a customer order and its lines.
- **Shipment** — an outbound shipment created when an order is shipped.

Order lifecycle: `Pending → Allocated → Shipped` (or `Cancelled`).

## Key API endpoints

```
GET  /api/products
GET  /api/products/{id}
POST /api/products

GET  /api/inventory/warehouse/{code}
GET  /api/inventory/low-stock
GET  /api/inventory/search?name=pump
POST /api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=-5

GET  /api/orders
GET  /api/orders/{id}
POST /api/orders
POST /api/orders/{id}/allocate
POST /api/orders/{id}/ship

GET  /api/shipments
GET  /api/shipments/order/{orderId}
```

## Running it (Windows + Visual Studio)

> This is a .NET Framework app. It builds and runs on **Windows** with Visual
> Studio 2019/2022 and IIS Express. It will **not** build on macOS/Linux — which
> is exactly part of the modernization story.

1. Create a SQL Server database named `MeridianWms`.
2. Run `scripts/schema-sqlserver.sql` against it.
3. Confirm the `WmsDb` connection string in `src/MeridianWms.Api/Web.config`.
4. Restore NuGet packages and run with IIS Express (F5).
5. Browse to e.g. `http://localhost:<port>/api/products`.

## Why this is a good modernization candidate (for the workshop)

The app intentionally contains issues an agent can find and fix:

- Synchronous EF6 calls on every request (no `async`/`await`).
- A raw SQL string-concatenation query (SQL injection risk) in
  `InventoryService.SearchByProductName`.
- Business logic and `DbContext` lifetime managed inside controllers/services
  via `new` — no DI, hard to test.
- `DateTime.Now` (local time) used for timestamps instead of UTC.
- Order `Status` is a magic string instead of an enum.
- Configuration read ad-hoc via `ConfigurationManager`.
- Windows/IIS/`System.Web` coupling that blocks cross-platform hosting.

See `../WORKSHOP-NET-part1-IDE.md` and `../WORKSHOP-NET-part2-CLI.md`.
