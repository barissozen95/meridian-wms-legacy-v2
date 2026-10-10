using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MeridianWms.Core.Data;

namespace MeridianWms.Core.Controllers
{
    [ApiController]
    [Route("api/shipments")]
    public class ShipmentsController : ControllerBase
    {
        private readonly WmsDbContext _db;

        public ShipmentsController(WmsDbContext db)
        {
            _db = db;
        }

        [HttpGet("")]
        public IActionResult GetAll()
        {
            // FIX (Medium): limit result set to prevent full-table scans from being
            // returned to callers, which could exhaust server memory or bandwidth.
            return Ok(_db.Shipments.OrderByDescending(s => s.ShippedUtc).Take(500).ToList());
        }

        [HttpGet("order/{orderId:int}")]
        public IActionResult GetForOrder(int orderId)
        {
            return Ok(_db.Shipments.Where(s => s.OrderId == orderId).ToList());
        }
    }
}
