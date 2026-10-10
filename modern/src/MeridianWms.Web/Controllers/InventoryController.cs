using MeridianWms.Web.Models;
using MeridianWms.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeridianWms.Web.Controllers;

[ApiController]
[Route("api/inventory")]
public class InventoryController(InventoryService service) : ControllerBase
{
    [HttpGet("warehouse/{code}")]
    public Task<List<InventoryItem>> GetForWarehouse(string code, CancellationToken ct) =>
        service.GetForWarehouseAsync(code, ct);

    [HttpGet("low-stock")]
    public Task<List<LowStockRow>> GetLowStock(CancellationToken ct) => service.GetLowStockAsync(ct);

    // GET api/inventory/search?name=pump
    [HttpGet("search")]
    public Task<List<SearchRow>> Search([FromQuery] string? name, CancellationToken ct) =>
        service.SearchByProductNameAsync(name, ct);

    // POST api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=-5
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust([FromQuery] int productId, [FromQuery] string warehouseCode,
        [FromQuery] int delta, CancellationToken ct)
    {
        await service.AdjustQuantityAsync(productId, warehouseCode, delta, ct);
        return Ok();
    }
}
