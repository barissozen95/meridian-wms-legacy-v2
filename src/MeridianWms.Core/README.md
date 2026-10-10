# Meridian WMS — ASP.NET Core Port

This directory contains **`MeridianWms.Core`**, a full port of the legacy
`MeridianWms.Api` (.NET Framework 4.8 / ASP.NET Web API 2) to
**ASP.NET Core 8 (LTS)** with **Entity Framework Core 8** on SQL Server.

The existing database schema is **unchanged**. All business logic, routing,
request/response contracts, and status codes are preserved exactly.

---

## Project layout

```
src/
├── MeridianWms.Api/            Legacy project (.NET 4.8 — do not modify)
├── MeridianWms.Core/           New ASP.NET Core 8 project  ← main deliverable
│   ├── Controllers/
│   │   ├── InventoryController.cs
│   │   ├── OrdersController.cs
│   │   ├── ProductsController.cs
│   │   └── ShipmentsController.cs
│   ├── Data/
│   │   └── WmsDbContext.cs     EF Core DbContext (Fluent API)
│   ├── Middleware/
│   │   └── ExceptionHandlingMiddleware.cs
│   ├── Migrations/
│   │   └── 20240101000000_InitialCreate.*  (schema snapshot — no DDL executed)
│   ├── Models/                 Identical to legacy models
│   ├── Services/
│   │   ├── InventoryService.cs
│   │   └── OrderService.cs
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── Program.cs
├── MeridianWms.Core.Tests/     xUnit integration tests (in-memory EF provider)
│   ├── Helpers/
│   │   └── WmsWebApplicationFactory.cs
│   ├── InventoryControllerTests.cs
│   ├── OrdersControllerTests.cs
│   ├── ProductsControllerTests.cs
│   └── ShipmentsControllerTests.cs
scripts/
└── schema-sqlserver.sql        Authoritative SQL Server schema + seed data
```

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 8.x LTS |
| SQL Server | 2019+ or LocalDB (for local dev) |

Install the .NET SDK from <https://dotnet.microsoft.com/download>.

---

## Setup

### 1. Clone and restore

```bash
git clone <repo-url>
cd meridian-wms-legacy-v2
dotnet restore
```

### 2. Configure the connection string

Edit [`src/MeridianWms.Core/appsettings.json`](src/MeridianWms.Core/appsettings.json):

```json
{
  "ConnectionStrings": {
    "WmsDb": "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=MeridianWms;Integrated Security=True;MultipleActiveResultSets=True"
  },
  "LowStockThreshold": "10",
  "DefaultWarehouseCode": "WH-CENTRAL"
}
```

Replace the value of `WmsDb` with your SQL Server connection string. For local
development, the default LocalDB string works out of the box.

**Never commit credentials.** Use environment variables or a secrets manager
for non-development environments:

```bash
# Example: override via environment variable (takes precedence over appsettings.json)
export ConnectionStrings__WmsDb="Server=myserver;Database=MeridianWms;User Id=...;Password=...;"
```

---

## Database setup

### Connecting to the existing database (recommended)

If you are pointing at the **existing** SQL Server database that the legacy API
used, the schema is already in place. You only need to record the baseline
migration as applied so EF Core does not try to re-run it:

```bash
cd src/MeridianWms.Core

# Tell EF Core the database is already at the InitialCreate baseline.
# This inserts a row in __EFMigrationsHistory without executing any DDL.
dotnet ef database update InitialCreate
```

### Creating a fresh database

To create a brand-new database from scratch (e.g. for a staging environment),
apply the SQL Server schema script first, then register the migration baseline:

```bash
# Option A — use the authoritative SQL script (recommended for schema parity)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d MeridianWms -i scripts/schema-sqlserver.sql

# Then register the EF baseline (no DDL will be applied):
dotnet ef database update InitialCreate --project src/MeridianWms.Core

# Option B — let EF Core create the schema from scratch (fresh databases only)
dotnet ef database update --project src/MeridianWms.Core
```

### Migration commands reference

```bash
# Add a new migration after model changes
dotnet ef migrations add <MigrationName> --project src/MeridianWms.Core

# Apply pending migrations
dotnet ef database update --project src/MeridianWms.Core

# Generate the SQL script for a migration (review before applying)
dotnet ef migrations script --project src/MeridianWms.Core --output migration.sql

# Revert the last migration (before it has been applied to the DB)
dotnet ef migrations remove --project src/MeridianWms.Core
```

---

## Running the API

```bash
cd src/MeridianWms.Core
dotnet run
```

The API will start on `https://localhost:5001` / `http://localhost:5000` by default.

### Endpoints (identical to legacy API)

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/products` | List all active products |
| GET | `/api/products/{id}` | Get product by ID |
| POST | `/api/products` | Create a product |
| GET | `/api/orders` | List recent orders (newest 500) |
| GET | `/api/orders/{id}` | Get order by ID with lines |
| POST | `/api/orders` | Create a new order |
| POST | `/api/orders/{id}/allocate` | Allocate stock for an order |
| POST | `/api/orders/{id}/ship` | Ship an allocated order |
| GET | `/api/shipments` | List recent shipments (newest 500) |
| GET | `/api/shipments/order/{orderId}` | List shipments for an order |
| GET | `/api/inventory/warehouse/{code}` | Inventory for a warehouse |
| GET | `/api/inventory/low-stock` | Items at or below low-stock threshold |
| GET | `/api/inventory/search?name=...` | Search inventory by product name |
| POST | `/api/inventory/adjust?productId=&warehouseCode=&delta=` | Adjust on-hand quantity |

---

## Running tests

The integration tests use the **EF Core in-memory provider**, so no SQL Server
connection is needed to run them:

```bash
dotnet test src/MeridianWms.Core.Tests
```

To run with verbose output:

```bash
dotnet test src/MeridianWms.Core.Tests --logger "console;verbosity=detailed"
```

### Test coverage

| File | What is tested |
|------|---------------|
| `OrdersControllerTests.cs` | All 5 order endpoints: GET all, GET by ID, POST create, POST allocate, POST ship. Happy paths and all documented error cases (400/404/500). Full lifecycle test. |
| `ShipmentsControllerTests.cs` | GET all shipments, GET by order ID (empty and populated). |
| `InventoryControllerTests.cs` | GET warehouse inventory, GET low-stock (shape + seed values), POST adjust (positive, negative-to-negative, zero). |
| `ProductsControllerTests.cs` | GET all (count, isActive filter), GET by ID (200 + InventoryItems included, 404), POST create (isActive override, round-trip). |

> **Coverage gap:** `GET /api/inventory/search` uses raw ADO.NET `SqlConnection`
> and cannot be exercised with the in-memory provider. A real SQL Server
> integration test is required for this endpoint. This matches the documented
> coverage gap in the legacy test suite.

---

## Key migration decisions

| Area | Legacy | Core |
|------|--------|------|
| Framework | .NET Framework 4.8 | .NET 8 LTS |
| Web framework | ASP.NET Web API 2 (`ApiController`) | ASP.NET Core (`ControllerBase` + `[ApiController]`) |
| ORM | Entity Framework 6 (`System.Data.Entity`) | EF Core 8 (`Microsoft.EntityFrameworkCore`) |
| Configuration | `Web.config` / `ConfigurationManager` | `appsettings.json` / `IConfiguration` |
| DI container | Manual `new` construction | ASP.NET Core built-in DI (`AddScoped`) |
| Routing | `[RoutePrefix]` + attribute routes | `[Route]` on controller + `[HttpGet/Post]("{...}")` |
| Return types | `IHttpActionResult` | `IActionResult` |
| JSON | Newtonsoft.Json, XML removed | `System.Text.Json`, reference cycles ignored |
| Error handling | Per-controller try/catch + `Trace.TraceError` | Global `ExceptionHandlingMiddleware` + `ILogger` |
| Startup | `Global.asax` + `WebApiConfig.Register` | `Program.cs` (minimal hosting model) |
| Tests | MSTest v3 + Effort.EF6 (in-memory) | xUnit + EF Core InMemory + `WebApplicationFactory` |

### Schema parity notes

- All six table names are **singular** and unchanged (`Product`, `Warehouse`,
  `InventoryItem`, `Order`, `OrderLine`, `Shipment`).
- All column names, data types, precision/scale, and nullability match the
  schema in [`scripts/schema-sqlserver.sql`](scripts/schema-sqlserver.sql).
- `Order` is a reserved SQL keyword; EF Core quotes it automatically when
  generating SQL.
- The `InitialCreate` migration in `src/MeridianWms.Core/Migrations/` is a
  baseline snapshot. It describes the existing schema; it does not alter it
  when applied to a database that already has the tables.

---

## Schema unchanged confirmation

The SQL DDL produced by the EF Core model matches `scripts/schema-sqlserver.sql`
column-for-column. To verify independently:

```bash
dotnet ef migrations script --project src/MeridianWms.Core \
  --from 0 --output /tmp/efcore-schema.sql

diff scripts/schema-sqlserver.sql /tmp/efcore-schema.sql
```

Minor cosmetic differences (e.g. `__EFMigrationsHistory` table, index names,
constraint names) are expected and do not affect runtime behaviour.
