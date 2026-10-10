using System.ComponentModel.DataAnnotations;
using MeridianWms.Web.Data;
using MeridianWms.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MeridianWms.Web.Controllers;

public record CreateProductRequest(
    [Required, StringLength(40)] string Sku,
    [Required, StringLength(200)] string Name,
    [StringLength(100)] string? Category,
    [Range(0, 9_999_999.999)] decimal UnitWeightKg,
    [Range(0, 9_999_999_999.99)] decimal UnitPrice);

[ApiController]
[Route("api/products")]
public class ProductsController(WmsDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<Product>> GetAll(CancellationToken ct) =>
        await db.Products.AsNoTracking().Include(p => p.InventoryItems)
            .Where(p => p.IsActive).OrderBy(p => p.ProductId).ToListAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Product>> GetById(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Include(p => p.InventoryItems)
            .FirstOrDefaultAsync(p => p.ProductId == id, ct);
        return product is null ? NotFound() : product;
    }

    // Legacy bound the EF entity directly (over-posting of ProductId/IsActive/InventoryItems); now a DTO.
    [HttpPost]
    public async Task<ActionResult<Product>> Create(CreateProductRequest request, CancellationToken ct)
    {
        var product = new Product
        {
            Sku = request.Sku,
            Name = request.Name,
            Category = request.Category,
            UnitWeightKg = request.UnitWeightKg,
            UnitPrice = request.UnitPrice,
            IsActive = true,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return Ok(product);
    }
}
