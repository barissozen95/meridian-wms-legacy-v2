using MeridianWms.Web.Data;
using MeridianWms.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeridianWms.Web.Services;

public record LowStockRow(string Sku, string Warehouse, int Available, string? Bin);
public record SearchRow(string Sku, string Name, string Warehouse, int QuantityOnHand);

public class InventoryService(WmsDbContext db, IOptions<WmsOptions> options)
{
    private const int SearchMaxResults = 200;

    public async Task<List<InventoryItem>> GetForWarehouseAsync(string? warehouseCode, CancellationToken ct = default) =>
        await db.InventoryItems.AsNoTracking()
            .Where(i => i.Warehouse!.Code == warehouseCode)
            .OrderBy(i => i.InventoryItemId)
            .ToListAsync(ct);

    // Items whose available quantity is at or below the threshold (<=), across all warehouses.
    public async Task<List<LowStockRow>> GetLowStockAsync(CancellationToken ct = default)
    {
        int threshold = options.Value.LowStockThreshold;
        return await db.InventoryItems.AsNoTracking()
            .Where(i => i.QuantityOnHand - i.QuantityReserved <= threshold)
            .OrderBy(i => i.InventoryItemId)
            .Select(i => new LowStockRow(i.Product!.Sku, i.Warehouse!.Code,
                i.QuantityOnHand - i.QuantityReserved, i.BinLocation))
            .ToListAsync(ct);
    }

    // Legacy: raw ADO.NET "WHERE p.Name LIKE '%' + x + '%'" (SQL Server, case-insensitive collation).
    // Now: EF Core parameterised ILIKE — injection-safe and case-insensitive like the source.
    public async Task<List<SearchRow>> SearchByProductNameAsync(string? nameFragment, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nameFragment))
        {
            return [];
        }

        string pattern = "%" + nameFragment + "%";
        return await db.InventoryItems.AsNoTracking()
            .Where(i => EF.Functions.ILike(i.Product!.Name, pattern))
            .OrderBy(i => i.InventoryItemId)
            .Take(SearchMaxResults)
            .Select(i => new SearchRow(i.Product!.Sku, i.Product.Name, i.Warehouse!.Code, i.QuantityOnHand))
            .ToListAsync(ct);
    }

    public async Task AdjustQuantityAsync(int productId, string? warehouseCode, int delta, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(warehouseCode))
        {
            throw new DomainException("warehouseCode must not be empty.");
        }
        if (delta == 0)
        {
            return;
        }

        var item = await db.InventoryItems
            .FirstOrDefaultAsync(i => i.ProductId == productId && i.Warehouse!.Code == warehouseCode, ct)
            ?? throw new NotFoundException($"No inventory for product {productId} in warehouse {warehouseCode}.");

        // Check before mutating (legacy mutated first, then threw).
        if (item.QuantityOnHand + delta < 0)
        {
            throw new DomainException("Quantity on hand cannot be negative.");
        }

        item.QuantityOnHand += delta;
        await db.SaveChangesAsync(ct);
    }
}
