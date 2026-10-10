using System.Collections.Generic;

namespace MeridianWms.Core.Models
{
    public class Warehouse
    {
        public int WarehouseId { get; set; }

        // Short code, e.g. "WH-CENTRAL", "WH-WEST"
        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? City { get; set; }

        public string? Country { get; set; }

        public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
    }
}
