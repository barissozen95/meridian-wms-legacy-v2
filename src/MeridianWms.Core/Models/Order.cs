using System;
using System.Collections.Generic;

namespace MeridianWms.Core.Models
{
    // Order status flows: Pending -> Allocated -> Shipped -> Cancelled
    public class Order
    {
        public int OrderId { get; set; }

        public string OrderNumber { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string? DestinationCity { get; set; }

        public string? DestinationCountry { get; set; }

        // Status as a free-text string (legacy design - no enum).
        // Known values: "Pending", "Allocated", "Shipped", "Cancelled"
        public string Status { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; }

        public virtual ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    }
}
