using System;

namespace MeridianWms.Core.Models
{
    public class Shipment
    {
        public int ShipmentId { get; set; }

        public int OrderId { get; set; }

        public string? Carrier { get; set; }

        public string? TrackingNumber { get; set; }

        public string? FromWarehouseCode { get; set; }

        public DateTime ShippedUtc { get; set; }

        // Total weight of the shipment in kilograms.
        public decimal TotalWeightKg { get; set; }
    }
}
