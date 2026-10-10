-- Meridian WMS - PostgreSQL post-load hardening (run AFTER data load + reconciliation).
-- Constraints are added NOT VALID then VALIDATEd so they never take a long exclusive lock
-- and so a violation is reported with the offending rows instead of aborting the load.
-- Idempotent: safe to re-run.

-- 0. Pre-flight: data that would violate the new constraints (must all return 0 rows) -------
--    The legacy schema enforced NONE of these; fix at source/target before continuing.
--    Duplicate checks use lower(btrim()) because the SQL Server source is (by default) case- and
--    trailing-space-insensitive: 'abc' and 'ABC ' were the SAME key there but are distinct in PostgreSQL.
SELECT 'dup Sku'         AS issue, lower(btrim("Sku"))  AS value, count(*) FROM "Product"   GROUP BY 2 HAVING count(*) > 1;
SELECT 'dup Warehouse'   AS issue, lower(btrim("Code")) AS value, count(*) FROM "Warehouse" GROUP BY 2 HAVING count(*) > 1;
SELECT 'dup OrderNumber' AS issue, "OrderNumber"        AS value, count(*) FROM "Order"     GROUP BY 2 HAVING count(*) > 1;
SELECT 'dup inventory (product,warehouse)' AS issue, "ProductId", "WarehouseId", count(*)
  FROM "InventoryItem" GROUP BY "ProductId", "WarehouseId" HAVING count(*) > 1;
SELECT 'orphan Shipment' AS issue, s."ShipmentId" FROM "Shipment" s LEFT JOIN "Order" o USING ("OrderId") WHERE o."OrderId" IS NULL;
SELECT 'negative on-hand' AS issue, "InventoryItemId" FROM "InventoryItem" WHERE "QuantityOnHand" < 0;
SELECT 'reserved > on-hand' AS issue, "InventoryItemId" FROM "InventoryItem" WHERE "QuantityReserved" > "QuantityOnHand";
SELECT 'non-positive qty' AS issue, "OrderLineId" FROM "OrderLine" WHERE "Quantity" <= 0;
SELECT 'unknown status' AS issue, "OrderId", "Status" FROM "Order"
  WHERE "Status" NOT IN ('Pending','Allocated','Shipped','Cancelled');

-- 1. Uniqueness (business keys the legacy app assumed but never enforced) -------------------
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Product_Sku"          ON "Product" ("Sku");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Warehouse_Code"       ON "Warehouse" ("Code");
-- GATE: OrderNumber = "ORD-" + yyyyMMddHHmmss has 1-second resolution, so two orders created in the same
-- second silently duplicated on SQL Server. This index turns that into a unique-violation (HTTP 500).
-- Create it ONLY after OrderService generates collision-free numbers (e.g. from a sequence).
CREATE UNIQUE INDEX IF NOT EXISTS "UX_Order_OrderNumber"    ON "Order" ("OrderNumber");
CREATE UNIQUE INDEX IF NOT EXISTS "UX_InventoryItem_Prod_Wh" ON "InventoryItem" ("ProductId", "WarehouseId");

-- 2. Missing relationship: Shipment.OrderId had no FK in SQL Server -------------------------
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Shipment_Order_OrderId') THEN
    ALTER TABLE "Shipment" ADD CONSTRAINT "FK_Shipment_Order_OrderId"
      FOREIGN KEY ("OrderId") REFERENCES "Order" ("OrderId") ON DELETE RESTRICT NOT VALID;
  END IF;
END $$;
ALTER TABLE "Shipment" VALIDATE CONSTRAINT "FK_Shipment_Order_OrderId";
CREATE INDEX IF NOT EXISTS "IX_Shipment_OrderId" ON "Shipment" ("OrderId");

-- 3. Referential actions: legacy SQL Server FKs were NO ACTION; the EF migration emitted
--    CASCADE. Restore legacy semantics - a warehouse system must not cascade-delete orders.
--    ALTER CONSTRAINT cannot change ON DELETE, so each FK is dropped and re-added in one
--    transaction (the DO block), NOT VALID first then VALIDATEd to keep the lock short.
DO $$
DECLARE r record; def text;
BEGIN
  FOR r IN SELECT c.conrelid::regclass AS tbl, c.conname, pg_get_constraintdef(c.oid) AS d
           FROM pg_constraint c
           WHERE c.contype = 'f' AND c.confdeltype = 'c' AND c.connamespace = 'public'::regnamespace
  LOOP
    def := replace(r.d, 'ON DELETE CASCADE', 'ON DELETE NO ACTION');
    EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', r.tbl, r.conname);
    EXECUTE format('ALTER TABLE %s ADD CONSTRAINT %I %s NOT VALID', r.tbl, r.conname, def);
    EXECUTE format('ALTER TABLE %s VALIDATE CONSTRAINT %I', r.tbl, r.conname);
  END LOOP;
END $$;

-- 4. Domain integrity the legacy code only half-enforced in C# ------------------------------
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_InventoryItem_OnHand_NonNegative') THEN
    ALTER TABLE "InventoryItem" ADD CONSTRAINT "CK_InventoryItem_OnHand_NonNegative"
      CHECK ("QuantityOnHand" >= 0) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_OrderLine_Quantity_Positive') THEN
    ALTER TABLE "OrderLine" ADD CONSTRAINT "CK_OrderLine_Quantity_Positive"
      CHECK ("Quantity" > 0) NOT VALID;
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'CK_Order_Status_Known') THEN
    ALTER TABLE "Order" ADD CONSTRAINT "CK_Order_Status_Known"
      CHECK ("Status" IN ('Pending','Allocated','Shipped','Cancelled')) NOT VALID;
  END IF;
END $$;
ALTER TABLE "InventoryItem" VALIDATE CONSTRAINT "CK_InventoryItem_OnHand_NonNegative";
ALTER TABLE "OrderLine"     VALIDATE CONSTRAINT "CK_OrderLine_Quantity_Positive";
ALTER TABLE "Order"         VALIDATE CONSTRAINT "CK_Order_Status_Known";
-- NOTE: no CHECK ("QuantityReserved" <= "QuantityOnHand"): unit tests pin that the legacy
-- app can produce Reserved > OnHand; add it only after a deliberate business decision.

-- 5. Query-path indexes (derived from the app's actual queries) -----------------------------
CREATE INDEX IF NOT EXISTS "IX_Order_CreatedUtc_Desc" ON "Order" ("CreatedUtc" DESC);       -- GetAll: ORDER BY CreatedUtc DESC LIMIT 500
CREATE INDEX IF NOT EXISTS "IX_Shipment_ShippedUtc"   ON "Shipment" ("ShippedUtc" DESC);
CREATE EXTENSION IF NOT EXISTS pg_trgm;                                                      -- SearchByProductName: ILIKE '%x%'
CREATE INDEX IF NOT EXISTS "IX_Product_Name_trgm" ON "Product" USING gin ("Name" gin_trgm_ops);

-- 6. Sequence safety net (idempotent; load_batch.py already does this) ----------------------
SELECT setval(pg_get_serial_sequence('"Product"','ProductId'),             COALESCE((SELECT MAX("ProductId")       FROM "Product"),1),       true);
SELECT setval(pg_get_serial_sequence('"Warehouse"','WarehouseId'),         COALESCE((SELECT MAX("WarehouseId")     FROM "Warehouse"),1),     true);
SELECT setval(pg_get_serial_sequence('"InventoryItem"','InventoryItemId'), COALESCE((SELECT MAX("InventoryItemId") FROM "InventoryItem"),1), true);
SELECT setval(pg_get_serial_sequence('"Order"','OrderId'),                 COALESCE((SELECT MAX("OrderId")         FROM "Order"),1),         true);
SELECT setval(pg_get_serial_sequence('"OrderLine"','OrderLineId'),         COALESCE((SELECT MAX("OrderLineId")     FROM "OrderLine"),1),     true);
SELECT setval(pg_get_serial_sequence('"Shipment"','ShipmentId'),           COALESCE((SELECT MAX("ShipmentId")      FROM "Shipment"),1),      true);

ANALYZE;
