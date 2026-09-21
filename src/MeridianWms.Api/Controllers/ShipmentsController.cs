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
            return Ok(_db.Shipments.OrderByDescending(s => s.ShippedUtc).ToList());
        }

        [HttpGet, Route("order/{orderId:int}")]
        public IHttpActionResult GetForOrder(int orderId)
        {
            return Ok(_db.Shipments.Where(s => s.OrderId == orderId).ToList());
        }
    }
}
