using MeridianWms.Web.Data;
using MeridianWms.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeridianWms.Web.Services;

public record NewOrderLine(int ProductId, int Quantity);

public class OrderService(WmsDbContext db, IOptions<WmsOptions> options, TimeProvider clock)
{
    public Task<Order?> GetByIdAsync(int orderId, CancellationToken ct = default) =>
        db.Orders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.OrderId == orderId, ct);

    public Task<List<Order>> GetAllAsync(CancellationToken ct = default) =>
        db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedUtc).Take(500).ToListAsync(ct);

    // Creates a Pending order, snapshotting each product's current UnitPrice.
    public async Task<Order> CreateOrderAsync(string customerName, string? destinationCity,
        string? destinationCountry, IReadOnlyList<NewOrderLine> lines, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new DomainException("At least one order line is required.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new DomainException("Each order line must have a positive Quantity.");
        }

        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId, ct);

        var now = clock.GetUtcNow().UtcDateTime;
        var order = new Order
        {
            OrderNumber = await NextOrderNumberAsync(now, ct),
            CustomerName = customerName,
            DestinationCity = destinationCity,
            DestinationCountry = destinationCountry,
            Status = OrderStatus.Pending,
            CreatedUtc = now,
        };

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                throw new DomainException("Unknown product: " + line.ProductId);
            }
            order.Lines.Add(new OrderLine
            {
                ProductId = product.ProductId,
                Quantity = line.Quantity,
                UnitPriceAtOrder = product.UnitPrice,
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return order;
    }

    // Reserves stock for every line in the default warehouse and moves the order to Allocated.
    // All-or-nothing: nothing is saved unless every line fits. Concurrent allocations of the same
    // stock are caught by the xmin concurrency token (-> DbUpdateConcurrencyException -> 409).
    public async Task<Order> AllocateAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Status != OrderStatus.Pending)
        {
            throw new DomainException("Only Pending orders can be allocated.");
        }

        var stock = await LoadDefaultWarehouseStockAsync(order, ct);
        foreach (var line in order.Lines)
        {
            var item = stock[line.ProductId];
            if (item.QuantityOnHand - item.QuantityReserved < line.Quantity)
            {
                throw new DomainException("Insufficient stock for product " + line.ProductId);
            }
            item.QuantityReserved += line.Quantity;
        }

        order.Status = OrderStatus.Allocated;
        await db.SaveChangesAsync(ct);
        return order;
    }

    // Ships an Allocated order: decrements on-hand and reserved stock, writes a Shipment.
    public async Task<Shipment> ShipAsync(int orderId, string carrier, string trackingNumber, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Status != OrderStatus.Allocated)
        {
            throw new DomainException("Only Allocated orders can be shipped.");
        }

        var stock = await LoadDefaultWarehouseStockAsync(order, ct);
        decimal totalWeight = 0;
        foreach (var line in order.Lines)
        {
            var item = stock[line.ProductId];
            item.QuantityOnHand -= line.Quantity;
            item.QuantityReserved -= line.Quantity;
            totalWeight += item.Product!.UnitWeightKg * line.Quantity;
        }

        var shipment = new Shipment
        {
            OrderId = order.OrderId,
            Carrier = carrier,
            TrackingNumber = trackingNumber,
            FromWarehouseCode = options.Value.DefaultWarehouseCode,
            ShippedUtc = clock.GetUtcNow().UtcDateTime,
            TotalWeightKg = totalWeight,
        };
        db.Shipments.Add(shipment);
        order.Status = OrderStatus.Shipped;
        await db.SaveChangesAsync(ct);
        return shipment;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await db.Orders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.OrderId == orderId, ct)
        ?? throw new NotFoundException($"Order {orderId} not found.");

    private async Task<Dictionary<int, InventoryItem>> LoadDefaultWarehouseStockAsync(Order order, CancellationToken ct)
    {
        string code = options.Value.DefaultWarehouseCode;
        var productIds = order.Lines.Select(l => l.ProductId).Distinct().ToList();
        var stock = await db.InventoryItems.Include(i => i.Product)
            .Where(i => i.Warehouse!.Code == code && productIds.Contains(i.ProductId))
            .ToDictionaryAsync(i => i.ProductId, ct);

        var missing = productIds.FirstOrDefault(id => !stock.ContainsKey(id), -1);
        if (missing != -1)
        {
            throw new DomainException($"Product {missing} is not stocked in warehouse {code}.");
        }
        return stock;
    }

    // Legacy: "ORD-" + yyyyMMddHHmmss (1-second resolution => duplicates under load).
    // Keeps the readable prefix/timestamp and appends a DB sequence value, so it is unique.
    private async Task<string> NextOrderNumberAsync(DateTime nowUtc, CancellationToken ct)
    {
        long seq = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('\"{WmsDbContext.OrderNumberSequence}\"') AS \"Value\"")
            .SingleAsync(ct);
        return $"ORD-{nowUtc:yyyyMMddHHmmss}-{seq:D6}";
    }
}
