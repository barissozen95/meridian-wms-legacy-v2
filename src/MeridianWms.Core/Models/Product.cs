using System.Collections.Generic;

namespace MeridianWms.Core.Models
{
    public class Product
    {
        public int ProductId { get; set; }

        // Stock Keeping Unit, e.g. "PMP-3000-A"
        public string Sku { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Category { get; set; }

        // Weight of a single unit in kilograms
        public decimal UnitWeightKg { get; set; }

        // Price the product is sold at
        public decimal UnitPrice { get; set; }

        public bool IsActive { get; set; }

        public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
    }
}
