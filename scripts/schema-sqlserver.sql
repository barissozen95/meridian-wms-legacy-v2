-- Meridian WMS - SQL Server schema + seed data
-- Run against a database named "MeridianWms".
-- This is the LEGACY database the .NET Framework 4.8 app talks to.

IF OBJECT_ID('dbo.Shipment', 'U') IS NOT NULL DROP TABLE dbo.Shipment;
IF OBJECT_ID('dbo.OrderLine', 'U') IS NOT NULL DROP TABLE dbo.OrderLine;
IF OBJECT_ID('dbo.[Order]', 'U') IS NOT NULL DROP TABLE dbo.[Order];
IF OBJECT_ID('dbo.InventoryItem', 'U') IS NOT NULL DROP TABLE dbo.InventoryItem;
IF OBJECT_ID('dbo.Product', 'U') IS NOT NULL DROP TABLE dbo.Product;
IF OBJECT_ID('dbo.Warehouse', 'U') IS NOT NULL DROP TABLE dbo.Warehouse;
GO

CREATE TABLE dbo.Product (
    ProductId      INT IDENTITY(1,1) PRIMARY KEY,
    Sku            NVARCHAR(40)  NOT NULL,
    Name           NVARCHAR(200) NOT NULL,
    Category       NVARCHAR(100) NULL,
    UnitWeightKg   DECIMAL(10,3) NOT NULL,
    UnitPrice      DECIMAL(12,2) NOT NULL,
    IsActive       BIT           NOT NULL DEFAULT(1)
);

CREATE TABLE dbo.Warehouse (
    WarehouseId    INT IDENTITY(1,1) PRIMARY KEY,
    Code           NVARCHAR(20)  NOT NULL,
    Name           NVARCHAR(200) NOT NULL,
    City           NVARCHAR(100) NULL,
    Country        NVARCHAR(100) NULL
);

CREATE TABLE dbo.InventoryItem (
    InventoryItemId   INT IDENTITY(1,1) PRIMARY KEY,
    ProductId         INT NOT NULL REFERENCES dbo.Product(ProductId),
    WarehouseId       INT NOT NULL REFERENCES dbo.Warehouse(WarehouseId),
    QuantityOnHand    INT NOT NULL DEFAULT(0),
    QuantityReserved  INT NOT NULL DEFAULT(0),
    BinLocation       NVARCHAR(40) NULL
);

CREATE TABLE dbo.[Order] (
    OrderId             INT IDENTITY(1,1) PRIMARY KEY,
    OrderNumber         NVARCHAR(40)  NOT NULL,
    CustomerName        NVARCHAR(200) NOT NULL,
    DestinationCity     NVARCHAR(100) NULL,
    DestinationCountry  NVARCHAR(100) NULL,
    Status              NVARCHAR(20)  NOT NULL,
    CreatedUtc          DATETIME      NOT NULL
);

CREATE TABLE dbo.OrderLine (
    OrderLineId        INT IDENTITY(1,1) PRIMARY KEY,
    OrderId            INT NOT NULL REFERENCES dbo.[Order](OrderId),
    ProductId          INT NOT NULL REFERENCES dbo.Product(ProductId),
    Quantity           INT NOT NULL,
    UnitPriceAtOrder   DECIMAL(12,2) NOT NULL
);

CREATE TABLE dbo.Shipment (
    ShipmentId         INT IDENTITY(1,1) PRIMARY KEY,
    OrderId            INT NOT NULL,
    Carrier            NVARCHAR(100) NULL,
    TrackingNumber     NVARCHAR(100) NULL,
    FromWarehouseCode  NVARCHAR(20)  NULL,
    ShippedUtc         DATETIME      NOT NULL,
    TotalWeightKg      DECIMAL(10,3) NOT NULL
);
GO

-- ---- Seed data -------------------------------------------------------------

INSERT INTO dbo.Warehouse (Code, Name, City, Country) VALUES
    ('WH-CENTRAL', 'Central Distribution Center', 'Frankfurt', 'Germany'),
    ('WH-WEST',    'Western Hub',                 'Rotterdam', 'Netherlands');

INSERT INTO dbo.Product (Sku, Name, Category, UnitWeightKg, UnitPrice, IsActive) VALUES
    ('PMP-3000-A', 'Industrial Water Pump 3000', 'Pumps',      18.500, 1240.00, 1),
    ('VLV-12-SS',  'Stainless Steel Valve 1/2"', 'Valves',      0.750,   34.90, 1),
    ('BRG-6204',   'Ball Bearing 6204',          'Bearings',    0.110,    8.50, 1),
    ('MTR-7K5',    'Electric Motor 7.5kW',       'Motors',     42.000, 2110.00, 1),
    ('FLT-AIR-M',  'Air Filter Cartridge M',     'Filters',     0.300,   22.00, 1);

INSERT INTO dbo.InventoryItem (ProductId, WarehouseId, QuantityOnHand, QuantityReserved, BinLocation) VALUES
    (1, 1, 25,  0, 'A-01-1'),
    (2, 1, 480, 0, 'B-04-2'),
    (3, 1, 6,   0, 'C-09-3'),   -- low stock
    (4, 1, 12,  0, 'A-02-1'),
    (5, 1, 8,   0, 'D-11-4'),   -- low stock
    (1, 2, 10,  0, 'A-01-1'),
    (2, 2, 200, 0, 'B-02-1');

INSERT INTO dbo.[Order] (OrderNumber, CustomerName, DestinationCity, DestinationCountry, Status, CreatedUtc) VALUES
    ('ORD-20240115093000', 'Bavaria Manufacturing GmbH', 'Munich', 'Germany', 'Shipped',   '2024-01-15T09:30:00'),
    ('ORD-20240220141500', 'Lowlands Industrial BV',     'Utrecht','Netherlands','Pending', '2024-02-20T14:15:00');

INSERT INTO dbo.OrderLine (OrderId, ProductId, Quantity, UnitPriceAtOrder) VALUES
    (1, 2, 50, 34.90),
    (1, 3, 100, 8.50),
    (2, 1, 2, 1240.00);

INSERT INTO dbo.Shipment (OrderId, Carrier, TrackingNumber, FromWarehouseCode, ShippedUtc, TotalWeightKg) VALUES
    (1, 'DHL Freight', '1Z-EXAMPLE-001', 'WH-CENTRAL', '2024-01-16T08:00:00', 48.500);
GO
