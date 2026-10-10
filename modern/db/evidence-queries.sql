-- Database evidence queries (run against the migrated + seeded + hardened database).
\echo '== Applied EF Core migrations'
SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory";

\echo '== Tables'
\dt

\echo '== Column types (legacy contract)'
SELECT table_name, column_name, data_type,
       COALESCE(character_maximum_length::text, numeric_precision || ',' || numeric_scale) AS size, is_nullable
FROM information_schema.columns
WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
ORDER BY table_name, ordinal_position;

\echo '== Constraints (FK / CHECK / validated, delete action)'
SELECT conrelid::regclass AS "table", conname, contype, convalidated,
       CASE confdeltype WHEN 'a' THEN 'NO ACTION' WHEN 'r' THEN 'RESTRICT' WHEN 'c' THEN 'CASCADE' END AS on_delete
FROM pg_constraint WHERE connamespace = 'public'::regnamespace AND contype IN ('f','c')
ORDER BY 1, 2;

\echo '== Indexes'
SELECT tablename, indexname FROM pg_indexes WHERE schemaname = 'public' ORDER BY 1, 2;

\echo '== Row counts'
SELECT 'Warehouse' t, count(*) FROM "Warehouse" UNION ALL SELECT 'Product', count(*) FROM "Product"
UNION ALL SELECT 'InventoryItem', count(*) FROM "InventoryItem" UNION ALL SELECT 'Order', count(*) FROM "Order"
UNION ALL SELECT 'OrderLine', count(*) FROM "OrderLine" UNION ALL SELECT 'Shipment', count(*) FROM "Shipment";

\echo '== Inventory'
SELECT i."InventoryItemId", p."Sku", w."Code", i."QuantityOnHand", i."QuantityReserved", i."BinLocation"
FROM "InventoryItem" i JOIN "Product" p USING ("ProductId") JOIN "Warehouse" w USING ("WarehouseId") ORDER BY 1;

\echo '== Orders / shipments'
SELECT "OrderId", "OrderNumber", "Status", "CreatedUtc" FROM "Order" ORDER BY 1;
SELECT "ShipmentId", "OrderId", "Carrier", "TrackingNumber", "FromWarehouseCode", "TotalWeightKg", "ShippedUtc" FROM "Shipment" ORDER BY 1;
