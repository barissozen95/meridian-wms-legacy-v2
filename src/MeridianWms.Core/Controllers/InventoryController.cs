using Microsoft.AspNetCore.Mvc;
using MeridianWms.Core.Services;

namespace MeridianWms.Core.Controllers
{
    [ApiController]
    [Route("api/inventory")]
    public class InventoryController : ControllerBase
    {
        private readonly InventoryService _service;

        public InventoryController(InventoryService service)
        {
            _service = service;
        }

        [HttpGet("warehouse/{code}")]
        public IActionResult GetForWarehouse(string code)
        {
            return Ok(_service.GetForWarehouse(code));
        }

        [HttpGet("low-stock")]
        public IActionResult GetLowStock()
        {
            return Ok(_service.GetLowStock());
        }

        // GET api/inventory/search?name=pump
        [HttpGet("search")]
        public IActionResult Search([FromQuery] string name)
        {
            return Ok(_service.SearchByProductName(name));
        }

        // POST api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=-5
        [HttpPost("adjust")]
        public IActionResult Adjust([FromQuery] int productId, [FromQuery] string warehouseCode, [FromQuery] int delta)
        {
            _service.AdjustQuantity(productId, warehouseCode, delta);
            return Ok();
        }
    }
}
