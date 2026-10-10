-- Meridian WMS - PostgreSQL development/test seed. Same rows as the seed section of
-- scripts/schema-sqlserver.sql (explicit keys so ids match the legacy database).
-- Run AFTER `dotnet ef database update`. Production data comes from scripts/load_batch.py instead.
-- Legacy DATETIME values were local wall-clock; seed timestamps are written as UTC here.

INSERT INTO "Warehouse" ("WarehouseId", "Code", "Name", "City", "Country") VALUES
    (1, 'WH-CENTRAL', 'Central Distribution Center', 'Frankfurt', 'Germany'),
    (2, 'WH-WEST',    'Western Hub',                 'Rotterdam', 'Netherlands');

INSERT INTO "Product" ("ProductId", "Sku", "Name", "Category", "UnitWeightKg", "UnitPrice", "IsActive") VALUES
    (1, 'PMP-3000-A', 'Industrial Water Pump 3000', 'Pumps',    18.500, 1240.00, true),
    (2, 'VLV-12-SS',  'Stainless Steel Valve 1/2"', 'Valves',    0.750,   34.90, true),
    (3, 'BRG-6204',   'Ball Bearing 6204',          'Bearings',  0.110,    8.50, true),
    (4, 'MTR-7K5',    'Electric Motor 7.5kW',       'Motors',   42.000, 2110.00, true),
    (5, 'FLT-AIR-M',  'Air Filter Cartridge M',     'Filters',   0.300,   22.00, true);

INSERT INTO "InventoryItem" ("InventoryItemId", "ProductId", "WarehouseId", "QuantityOnHand", "QuantityReserved", "BinLocation") VALUES
    (1, 1, 1, 25,  0, 'A-01-1'),
    (2, 2, 1, 480, 0, 'B-04-2'),
    (3, 3, 1, 6,   0, 'C-09-3'),
    (4, 4, 1, 12,  0, 'A-02-1'),
    (5, 5, 1, 8,   0, 'D-11-4'),
    (6, 1, 2, 10,  0, 'A-01-1'),
    (7, 2, 2, 200, 0, 'B-02-1');

INSERT INTO "Order" ("OrderId", "OrderNumber", "CustomerName", "DestinationCity", "DestinationCountry", "Status", "CreatedUtc") VALUES
    (1, 'ORD-20240115093000', 'Bavaria Manufacturing GmbH', 'Munich',  'Germany',     'Shipped', '2024-01-15T09:30:00Z'),
    (2, 'ORD-20240220141500', 'Lowlands Industrial BV',     'Utrecht', 'Netherlands', 'Pending', '2024-02-20T14:15:00Z');

INSERT INTO "OrderLine" ("OrderLineId", "OrderId", "ProductId", "Quantity", "UnitPriceAtOrder") VALUES
    (1, 1, 2, 50,  34.90),
    (2, 1, 3, 100, 8.50),
    (3, 2, 1, 2,   1240.00);

INSERT INTO "Shipment" ("ShipmentId", "OrderId", "Carrier", "TrackingNumber", "FromWarehouseCode", "ShippedUtc", "TotalWeightKg") VALUES
    (1, 1, 'DHL Freight', '1Z-EXAMPLE-001', 'WH-CENTRAL', '2024-01-16T08:00:00Z', 48.500);

-- Explicit keys bypass the identity sequences; move them past the seeded rows.
SELECT setval(pg_get_serial_sequence('"Warehouse"', 'WarehouseId'),         (SELECT MAX("WarehouseId")     FROM "Warehouse"));
SELECT setval(pg_get_serial_sequence('"Product"', 'ProductId'),             (SELECT MAX("ProductId")       FROM "Product"));
SELECT setval(pg_get_serial_sequence('"InventoryItem"', 'InventoryItemId'), (SELECT MAX("InventoryItemId") FROM "InventoryItem"));
SELECT setval(pg_get_serial_sequence('"Order"', 'OrderId'),                 (SELECT MAX("OrderId")         FROM "Order"));
SELECT setval(pg_get_serial_sequence('"OrderLine"', 'OrderLineId'),         (SELECT MAX("OrderLineId")     FROM "OrderLine"));
SELECT setval(pg_get_serial_sequence('"Shipment"', 'ShipmentId'),           (SELECT MAX("ShipmentId")      FROM "Shipment"));
