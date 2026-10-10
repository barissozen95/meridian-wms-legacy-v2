using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            // FIX (Medium): validate required fields before processing so we return a
            // clear 400 without touching the database or leaking internal detail.
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }
            if (string.IsNullOrWhiteSpace(request.CustomerName))
            {
                return BadRequest("CustomerName is required.");
            }
            if (request.Lines == null || request.Lines.Count == 0)
            {
                return BadRequest("At least one order line is required.");
            }
            foreach (var l in request.Lines)
            {
                if (l.Quantity <= 0)
                {
                    return BadRequest("Each order line must have a positive Quantity.");
                }
            }

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
            catch (InvalidOperationException ex)
            {
                // FIX (High — error disclosure): return only a generic user-facing message;
                // log the full exception server-side so it is not lost.
                Trace.TraceError("CreateOrder failed: {0}", ex);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // FIX (High — error disclosure): swallow unexpected exceptions so internal
                // details (EF messages, stack traces, table/column names) are never sent to
                // the caller. The full exception is still written to the trace log.
                Trace.TraceError("CreateOrder unexpected error: {0}", ex);
                return InternalServerError(new Exception("An error occurred processing the order."));
            }
        }

        [HttpPost, Route("{id:int}/allocate")]
        public IHttpActionResult Allocate(int id)
        {
            try
            {
                return Ok(_service.Allocate(id));
            }
            catch (InvalidOperationException ex)
            {
                // FIX (High — error disclosure): business-rule violations (e.g. wrong status)
                // are safe to surface; log unexpected ones and return a generic message.
                Trace.TraceError("Allocate({0}) failed: {1}", id, ex);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Allocate({0}) unexpected error: {1}", id, ex);
                return InternalServerError(new Exception("An error occurred allocating the order."));
            }
        }

        [HttpPost, Route("{id:int}/ship")]
        public IHttpActionResult Ship(int id, ShipRequest request)
        {
            // FIX (Medium): validate shipping inputs before processing.
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }
            if (string.IsNullOrWhiteSpace(request.Carrier))
            {
                return BadRequest("Carrier is required.");
            }
            if (string.IsNullOrWhiteSpace(request.TrackingNumber))
            {
                return BadRequest("TrackingNumber is required.");
            }

            try
            {
                return Ok(_service.Ship(id, request.Carrier, request.TrackingNumber));
            }
            catch (InvalidOperationException ex)
            {
                Trace.TraceError("Ship({0}) failed: {1}", id, ex);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // FIX (High — error disclosure): unexpected exceptions (e.g. EF / DB errors)
                // must not leak internal details to callers.
                Trace.TraceError("Ship({0}) unexpected error: {1}", id, ex);
                return InternalServerError(new Exception("An error occurred shipping the order."));
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
