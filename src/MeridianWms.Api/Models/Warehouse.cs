using System.Collections.Generic;

namespace MeridianWms.Api.Models
{
    public class Warehouse
    {
        public int WarehouseId { get; set; }

        // Short code, e.g. "WH-CENTRAL", "WH-WEST"
        public string Code { get; set; }

        public string Name { get; set; }

        public string City { get; set; }

        public string Country { get; set; }

        public virtual ICollection<InventoryItem> InventoryItems { get; set; }
    }
}
