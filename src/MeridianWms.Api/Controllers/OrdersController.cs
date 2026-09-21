using System;
using System.Collections.Generic;
using System.Web.Http;
using MeridianWms.Api.Models;
using MeridianWms.Api.Services;

namespace MeridianWms.Api.Controllers
{
    [RoutePrefix("api/orders")]
    public class OrdersController : ApiController
    {
        private readonly OrderService _service = new OrderService();

        [HttpGet, Route("")]
        public IHttpActionResult GetAll()
        {
            return Ok(_service.GetAll());
        }

        [HttpGet, Route("{id:int}")]
        public IHttpActionResult GetById(int id)
        {
            var order = _service.GetById(id);
            if (order == null)
            {
                return NotFound();
            }
            return Ok(order);
        }

        [HttpPost, Route("")]
        public IHttpActionResult Create(CreateOrderRequest request)
        {
            try
            {
                var lines = new List<OrderLine>();
                foreach (var l in request.Lines)
                {
                    lines.Add(new OrderLine { ProductId = l.ProductId, Quantity = l.Quantity });
                }

                var order = _service.CreateOrder(
                    request.CustomerName, request.DestinationCity,
                    request.DestinationCountry, lines);

                return Ok(order);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost, Route("{id:int}/allocate")]
        public IHttpActionResult Allocate(int id)
        {
            try
            {
                return Ok(_service.Allocate(id));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost, Route("{id:int}/ship")]
        public IHttpActionResult Ship(int id, ShipRequest request)
        {
            try
            {
                return Ok(_service.Ship(id, request.Carrier, request.TrackingNumber));
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }

    public class CreateOrderRequest
    {
        public string CustomerName { get; set; }
        public string DestinationCity { get; set; }
        public string DestinationCountry { get; set; }
        public List<CreateOrderLine> Lines { get; set; }
    }

    public class CreateOrderLine
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class ShipRequest
    {
        public string Carrier { get; set; }
        public string TrackingNumber { get; set; }
    }
}
