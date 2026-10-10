using System.Linq;
using System.Web.Http;
using MeridianWms.Api.Data;

namespace MeridianWms.Api.Controllers
{
    [RoutePrefix("api/shipments")]
    public class ShipmentsController : ApiController
    {
        private readonly WmsDbContext _db = new WmsDbContext();

        [HttpGet, Route("")]
        public IHttpActionResult GetAll()
        {
            // FIX (Medium): limit result set to prevent full-table scans from being
            // returned to callers, which could exhaust server memory or bandwidth.
            return Ok(_db.Shipments.OrderByDescending(s => s.ShippedUtc).Take(500).ToList());
        }

        [HttpGet, Route("order/{orderId:int}")]
        public IHttpActionResult GetForOrder(int orderId)
        {
            return Ok(_db.Shipments.Where(s => s.OrderId == orderId).ToList());
        }
    }
}
