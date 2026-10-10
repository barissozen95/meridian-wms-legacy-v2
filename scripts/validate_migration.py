#!/usr/bin/env python3
"""
Meridian WMS - SQL Server -> PostgreSQL reconciliation.

Compares, per table: row counts, MIN/MAX/SUM of the primary key, and a
row-level SHA-256 over a canonical text form (keyset-paged, constant memory).
Exit code 0 = identical, 1 = differences found, 2 = usage/connection error.

Requires:  pip install "psycopg[binary]" pyodbc
Secrets are read from the environment only (never from argv / files in git):
    MSSQL_CONN   e.g. "Driver={ODBC Driver 18 for SQL Server};Server=...;Database=MeridianWms;
                 UID=...;PWD=...;Encrypt=yes;TrustServerCertificate=no"
    PG_CONN      e.g. "host=... dbname=wms_db user=... password=... sslmode=verify-full"
Optional:
    PAGE_SIZE    rows per page (default 5000)
    MAX_REPORT   max differing rows printed per table (default 20)
    SOURCE_TZ    IANA zone the legacy DATETIME values were written in (e.g. Europe/Berlin).
                 Unset  -> timestamptz is rendered in UTC (use BEFORE the timezone correction).
                 Set    -> timestamptz is rendered in SOURCE_TZ (use AFTER the correction).
"""
import datetime
import decimal
import hashlib
import os
import sys

from zoneinfo import ZoneInfo

import psycopg
import pyodbc

_DISPLAY_TZ = ZoneInfo(os.environ.get("SOURCE_TZ") or "UTC")

# table -> (pk column, ordered column list). Order matters: it defines the hash input.
TABLES = {
    "Product": ("ProductId", ["ProductId", "Sku", "Name", "Category",
                              "UnitWeightKg", "UnitPrice", "IsActive"]),
    "Warehouse": ("WarehouseId", ["WarehouseId", "Code", "Name", "City", "Country"]),
    "InventoryItem": ("InventoryItemId", ["InventoryItemId", "ProductId", "WarehouseId",
                                          "QuantityOnHand", "QuantityReserved", "BinLocation"]),
    "Order": ("OrderId", ["OrderId", "OrderNumber", "CustomerName", "DestinationCity",
                          "DestinationCountry", "Status", "CreatedUtc"]),
    "OrderLine": ("OrderLineId", ["OrderLineId", "OrderId", "ProductId", "Quantity",
                                  "UnitPriceAtOrder"]),
    "Shipment": ("ShipmentId", ["ShipmentId", "OrderId", "Carrier", "TrackingNumber",
                                "FromWarehouseCode", "ShippedUtc", "TotalWeightKg"]),
}


def canon(value):
    """Canonical, engine-independent text for one value."""
    if value is None:
        return "\x00NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, decimal.Decimal):
        return format(value, "f")  # keeps scale: 18.500 stays 18.500 on both sides
    if isinstance(value, datetime.datetime):
        if value.tzinfo is not None:  # timestamptz -> naive wall-clock in _DISPLAY_TZ
            value = value.astimezone(_DISPLAY_TZ).replace(tzinfo=None)
        # SQL Server DATETIME has ~3.33 ms resolution; compare at millisecond level.
        return value.strftime("%Y-%m-%d %H:%M:%S.") + f"{value.microsecond // 1000:03d}"
    return str(value)


def row_hash(row):
    payload = "\x1f".join(canon(v) for v in row).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def mssql_page(cur, table, pk, cols, after, size):
    col_sql = ", ".join(f"[{c}]" for c in cols)
    cur.execute(
        f"SELECT TOP ({int(size)}) {col_sql} FROM dbo.[{table}] "
        f"WHERE [{pk}] > ? ORDER BY [{pk}]", after)
    return cur.fetchall()


def pg_page(cur, table, pk, cols, after, size):
    col_sql = ", ".join(f'"{c}"' for c in cols)
    cur.execute(
        f'SELECT {col_sql} FROM public."{table}" WHERE "{pk}" > %s '
        f'ORDER BY "{pk}" LIMIT %s', (after, int(size)))
    return cur.fetchall()


def aggregate(ms, pg, table, pk):
    ms.execute(f"SELECT COUNT_BIG(*), MIN([{pk}]), MAX([{pk}]), SUM(CAST([{pk}] AS BIGINT)) "
               f"FROM dbo.[{table}]")
    a = tuple(ms.fetchone())
    pg.execute(f'SELECT COUNT(*), MIN("{pk}"), MAX("{pk}"), SUM("{pk}"::bigint) '
               f'FROM public."{table}"')
    b = tuple(pg.fetchone())
    return a, b


def compare_table(ms, pg, table, pk, cols, page_size, max_report):
    ok = True
    a, b = aggregate(ms, pg, table, pk)
    print(f"[{table}] count/min/max/sum(pk)  source={a}  target={b}")
    if a != b:
        ok = False
        print(f"[{table}] MISMATCH in aggregates")

    last_ms = last_pg = -2**31
    reported = diffs = 0
    while True:
        src = mssql_page(ms, table, pk, cols, last_ms, page_size)
        tgt = pg_page(pg, table, pk, cols, last_pg, page_size)
        if not src and not tgt:
            break
        s = {r[0]: row_hash(r) for r in src}
        t = {r[0]: row_hash(r) for r in tgt}
        # Only judge keys both pages must cover (up to the smaller page's max key),
        # unless a side is exhausted.
        limit = None
        if src and tgt and len(src) == page_size and len(tgt) == page_size:
            limit = min(src[-1][0], tgt[-1][0])
        elif src and len(src) == page_size and not tgt:
            limit = src[-1][0]
        elif tgt and len(tgt) == page_size and not src:
            limit = tgt[-1][0]
        keys = sorted(set(s) | set(t))
        if limit is not None:
            keys = [k for k in keys if k <= limit]
        for k in keys:
            if s.get(k) != t.get(k):
                diffs += 1
                ok = False
                if reported < max_report:
                    state = ("missing in target" if k not in t else
                             "missing in source" if k not in s else "content differs")
                    print(f"[{table}] {pk}={k}: {state}")
                    reported += 1
        if not keys:
            break
        last_ms = last_pg = keys[-1]
    print(f"[{table}] row-hash differences: {diffs}")
    return ok


def main():
    ms_conn, pg_conn = os.environ.get("MSSQL_CONN"), os.environ.get("PG_CONN")
    if not ms_conn or not pg_conn:
        print("MSSQL_CONN and PG_CONN must be set in the environment", file=sys.stderr)
        return 2
    page_size = int(os.environ.get("PAGE_SIZE", "5000"))
    max_report = int(os.environ.get("MAX_REPORT", "20"))

    all_ok = True
    with pyodbc.connect(ms_conn, timeout=30, readonly=True) as mc, \
            psycopg.connect(pg_conn, connect_timeout=30) as pc:
        pc.read_only = True
        ms, pg = mc.cursor(), pc.cursor()
        for table, (pk, cols) in TABLES.items():
            all_ok &= compare_table(ms, pg, table, pk, cols, page_size, max_report)
    print("RESULT:", "IDENTICAL" if all_ok else "DIFFERENCES FOUND")
    return 0 if all_ok else 1


if __name__ == "__main__":
    sys.exit(main())
