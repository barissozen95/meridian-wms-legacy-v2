using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MeridianWms.Core.Models;
using MeridianWms.Core.Services;

namespace MeridianWms.Core.Controllers
{
    [ApiController]
    [Route("api/orders")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderService _service;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(OrderService service, ILogger<OrdersController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("")]
        public IActionResult GetAll()
        {
            return Ok(_service.GetAll());
        }

        [HttpGet("{id:int}")]
        public IActionResult GetById(int id)
        {
            var order = _service.GetById(id);
            if (order == null)
            {
                return NotFound();
            }
            return Ok(order);
        }

        [HttpPost("")]
        public IActionResult Create([FromBody] CreateOrderRequest request)
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
                _logger.LogError(ex, "CreateOrder failed");
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // FIX (High — error disclosure): swallow unexpected exceptions so internal
                // details (EF messages, stack traces, table/column names) are never sent to
                // the caller. The full exception is still written to the log.
                _logger.LogError(ex, "CreateOrder unexpected error");
                return StatusCode(500, "An error occurred processing the order.");
            }
        }

        [HttpPost("{id:int}/allocate")]
        public IActionResult Allocate(int id)
        {
            try
            {
                return Ok(_service.Allocate(id));
            }
            catch (InvalidOperationException ex)
            {
                // FIX (High — error disclosure): business-rule violations (e.g. wrong status)
                // are safe to surface; log unexpected ones and return a generic message.
                _logger.LogError(ex, "Allocate({Id}) failed", id);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Allocate({Id}) unexpected error", id);
                return StatusCode(500, "An error occurred allocating the order.");
            }
        }

        [HttpPost("{id:int}/ship")]
        public IActionResult Ship(int id, [FromBody] ShipRequest request)
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
                _logger.LogError(ex, "Ship({Id}) failed", id);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                // FIX (High — error disclosure): unexpected exceptions (e.g. EF / DB errors)
                // must not leak internal details to callers.
                _logger.LogError(ex, "Ship({Id}) unexpected error", id);
                return StatusCode(500, "An error occurred shipping the order.");
            }
        }
    }

    public class CreateOrderRequest
    {
        public string CustomerName { get; set; } = string.Empty;
        public string? DestinationCity { get; set; }
        public string? DestinationCountry { get; set; }
        public List<CreateOrderLine> Lines { get; set; } = new();
    }

    public class CreateOrderLine
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class ShipRequest
    {
        public string Carrier { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
    }
}
