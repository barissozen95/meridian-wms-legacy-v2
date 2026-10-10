namespace MeridianWms.Core.Models
{
    // Quantity of a given Product physically present in a given Warehouse.
    public class InventoryItem
    {
        public int InventoryItemId { get; set; }

        public int ProductId { get; set; }
        public virtual Product? Product { get; set; }

        public int WarehouseId { get; set; }
        public virtual Warehouse? Warehouse { get; set; }

        // Units physically on the shelf.
        public int QuantityOnHand { get; set; }

        // Units already promised to orders but not yet shipped.
        public int QuantityReserved { get; set; }

        // Bin / aisle location inside the warehouse, e.g. "A-12-3"
        public string? BinLocation { get; set; }
    }
}
