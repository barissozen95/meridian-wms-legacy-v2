namespace MeridianWms.Web.Models;

public class Product
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public decimal UnitWeightKg { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; }
    public List<InventoryItem> InventoryItems { get; set; } = [];
}

public class Warehouse
{
    public int WarehouseId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? City { get; set; }
    public string? Country { get; set; }
    public List<InventoryItem> InventoryItems { get; set; } = [];
}

// Quantity of a Product in a Warehouse. Available = QuantityOnHand - QuantityReserved.
public class InventoryItem
{
    public int InventoryItemId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public string? BinLocation { get; set; }
}

// Stored as text ("Pending", ...) exactly like the legacy column, so data loads/reconciles unchanged.
public enum OrderStatus { Pending, Allocated, Shipped, Cancelled }

public class Order
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? DestinationCity { get; set; }
    public string? DestinationCountry { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public int OrderLineId { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceAtOrder { get; set; }
}

public class Shipment
{
    public int ShipmentId { get; set; }
    public int OrderId { get; set; }
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? FromWarehouseCode { get; set; }
    public DateTime ShippedUtc { get; set; }
    public decimal TotalWeightKg { get; set; }
}
