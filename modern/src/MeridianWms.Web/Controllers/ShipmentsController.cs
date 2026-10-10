using MeridianWms.Web.Data;
using MeridianWms.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeridianWms.Web.Controllers;

[ApiController]
[Route("api/shipments")]
public class ShipmentsController(WmsDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<List<Shipment>> GetAll(CancellationToken ct) =>
        db.Shipments.AsNoTracking().OrderByDescending(s => s.ShippedUtc).Take(500).ToListAsync(ct);

    [HttpGet("order/{orderId:int}")]
    public Task<List<Shipment>> GetForOrder(int orderId, CancellationToken ct) =>
        db.Shipments.AsNoTracking().Where(s => s.OrderId == orderId).OrderBy(s => s.ShipmentId).ToListAsync(ct);
}
