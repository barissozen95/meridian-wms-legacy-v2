using System;
using System.Collections.Generic;

namespace MeridianWms.Api.Models
{
    // Order status flows: Pending -> Allocated -> Shipped -> Cancelled
    public class Order
    {
        public int OrderId { get; set; }

        public string OrderNumber { get; set; }

        public string CustomerName { get; set; }

        public string DestinationCity { get; set; }

        public string DestinationCountry { get; set; }

        // Status as a free-text string (legacy design - no enum).
        // Known values: "Pending", "Allocated", "Shipped", "Cancelled"
        public string Status { get; set; }

        public DateTime CreatedUtc { get; set; }

        public virtual ICollection<OrderLine> Lines { get; set; }
    }
}
