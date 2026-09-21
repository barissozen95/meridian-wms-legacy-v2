using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Http;
using MeridianWms.Api.Data;
using MeridianWms.Api.Models;

namespace MeridianWms.Api.Controllers
{
    [RoutePrefix("api/products")]
    public class ProductsController : ApiController
    {
        private readonly WmsDbContext _db = new WmsDbContext();

        [HttpGet, Route("")]
        public IEnumerable<Product> GetAll()
        {
            return _db.Products
                .Include(p => p.InventoryItems)
                .Where(p => p.IsActive)
                .ToList();
        }

        [HttpGet, Route("{id:int}")]
        public IHttpActionResult GetById(int id)
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

        [HttpPost, Route("")]
        public IHttpActionResult Create(Product product)
        {
            product.IsActive = true;
            _db.Products.Add(product);
            _db.SaveChanges();
            return Ok(product);
        }
    }
}
