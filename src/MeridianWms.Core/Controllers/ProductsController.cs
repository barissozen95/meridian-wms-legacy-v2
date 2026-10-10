using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MeridianWms.Core.Data;
using MeridianWms.Core.Models;

namespace MeridianWms.Core.Controllers
{
    [ApiController]
    [Route("api/products")]
    public class ProductsController : ControllerBase
    {
        private readonly WmsDbContext _db;

        public ProductsController(WmsDbContext db)
        {
            _db = db;
        }

        [HttpGet("")]
        public IEnumerable<Product> GetAll()
        {
            return _db.Products
                .Include(p => p.InventoryItems)
                .Where(p => p.IsActive)
                .ToList();
        }

        [HttpGet("{id:int}")]
        public IActionResult GetById(int id)
        {
            var product = _db.Products
                .Include(p => p.InventoryItems)
                .FirstOrDefault(p => p.ProductId == id);
            if (product == null)
            {
                return NotFound();
            }
            return Ok(product);
        }

        [HttpPost("")]
        public IActionResult Create([FromBody] Product product)
        {
            product.IsActive = true;
            _db.Products.Add(product);
            _db.SaveChanges();
            return Ok(product);
        }
    }
}
