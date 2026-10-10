using System.ComponentModel.DataAnnotations;
using MeridianWms.Web.Models;
using MeridianWms.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace MeridianWms.Web.Controllers;

public record CreateOrderLineRequest(int ProductId, [Range(1, int.MaxValue)] int Quantity);

public record CreateOrderRequest(
    [Required(AllowEmptyStrings = false), StringLength(200)] string CustomerName,
    [StringLength(100)] string? DestinationCity,
    [StringLength(100)] string? DestinationCountry,
    [Required, MinLength(1)] List<CreateOrderLineRequest> Lines);

public record ShipRequest(
    [Required(AllowEmptyStrings = false), StringLength(100)] string Carrier,
    [Required(AllowEmptyStrings = false), StringLength(100)] string TrackingNumber);

[ApiController]
[Route("api/orders")]
public class OrdersController(OrderService service) : ControllerBase
{
    [HttpGet]
    public Task<List<Order>> GetAll(CancellationToken ct) => service.GetAllAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Order>> GetById(int id, CancellationToken ct) =>
        await service.GetByIdAsync(id, ct) is { } order ? order : NotFound();

    [HttpPost]
    public async Task<ActionResult<Order>> Create(CreateOrderRequest request, CancellationToken ct) =>
        Ok(await service.CreateOrderAsync(request.CustomerName, request.DestinationCity, request.DestinationCountry,
            request.Lines.Select(l => new NewOrderLine(l.ProductId, l.Quantity)).ToList(), ct));

    [HttpPost("{id:int}/allocate")]
    public async Task<ActionResult<Order>> Allocate(int id, CancellationToken ct) =>
        Ok(await service.AllocateAsync(id, ct));

    [HttpPost("{id:int}/ship")]
    public async Task<ActionResult<Shipment>> Ship(int id, ShipRequest request, CancellationToken ct) =>
        Ok(await service.ShipAsync(id, request.Carrier, request.TrackingNumber, ct));
}
