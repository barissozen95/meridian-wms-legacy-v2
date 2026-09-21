using System.Web.Http;
using MeridianWms.Api.Services;

namespace MeridianWms.Api.Controllers
{
    [RoutePrefix("api/inventory")]
    public class InventoryController : ApiController
    {
        private readonly InventoryService _service = new InventoryService();

        [HttpGet, Route("warehouse/{code}")]
        public IHttpActionResult GetForWarehouse(string code)
        {
            return Ok(_service.GetForWarehouse(code));
        }

        [HttpGet, Route("low-stock")]
        public IHttpActionResult GetLowStock()
        {
            return Ok(_service.GetLowStock());
        }

        // GET api/inventory/search?name=pump
        [HttpGet, Route("search")]
        public IHttpActionResult Search(string name)
        {
            return Ok(_service.SearchByProductName(name));
        }

        // POST api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=-5
        [HttpPost, Route("adjust")]
        public IHttpActionResult Adjust(int productId, string warehouseCode, int delta)
        {
            _service.AdjustQuantity(productId, warehouseCode, delta);
            return Ok();
        }
    }
}
