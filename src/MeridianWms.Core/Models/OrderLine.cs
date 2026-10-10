namespace MeridianWms.Core.Models
{
    public class OrderLine
    {
        public int OrderLineId { get; set; }

        public int OrderId { get; set; }
        public virtual Order? Order { get; set; }

        public int ProductId { get; set; }
        public virtual Product? Product { get; set; }

        public int Quantity { get; set; }

        // Price captured at the time the order was placed.
        public decimal UnitPriceAtOrder { get; set; }
    }
}
