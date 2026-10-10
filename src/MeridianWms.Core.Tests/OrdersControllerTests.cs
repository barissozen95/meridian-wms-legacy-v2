using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using MeridianWms.Core.Tests.Helpers;

namespace MeridianWms.Core.Tests
{
    /// <summary>
    /// Integration tests for OrdersController.  Each test class gets its own
    /// WebApplicationFactory (and therefore its own isolated in-memory database)
    /// so order-mutating tests do not bleed state into each other.
    /// </summary>
    public class OrdersControllerTests : IDisposable
    {
        private readonly WmsWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public OrdersControllerTests()
        {
            _factory = new WmsWebApplicationFactory();
            _client = _factory.CreateSeededClient();
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
        }

        // =====================================================================
        // GET api/orders
        // =====================================================================

        [Fact(DisplayName = "GET /api/orders returns 200 OK with a JSON array")]
        public async Task GetAll_Returns200_WithArray()
        {
            var response = await _client.GetAsync("/api/orders");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        }

        // =====================================================================
        // GET api/orders/{id}
        // =====================================================================

        [Fact(DisplayName = "GET /api/orders/{id} returns 404 for a non-existent order")]
        public async Task GetById_NonExistent_Returns404()
        {
            var response = await _client.GetAsync("/api/orders/99999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact(DisplayName = "GET /api/orders/{id} returns 200 with the created order")]
        public async Task GetById_ExistingOrder_Returns200()
        {
            // Create an order first
            var created = await CreateValidOrderAsync();

            var response = await _client.GetAsync($"/api/orders/{created.GetProperty("orderId").GetInt32()}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // =====================================================================
        // POST api/orders
        // =====================================================================

        [Fact(DisplayName = "POST /api/orders with valid body returns 200 OK with Pending order")]
        public async Task Create_ValidRequest_Returns200WithPendingOrder()
        {
            var payload = new
            {
                customerName = "Acme Corp",
                destinationCity = "Berlin",
                destinationCountry = "Germany",
                lines = new[] { new { productId = 1, quantity = 2 } }
            };

            var response = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("Pending", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal("Acme Corp", doc.RootElement.GetProperty("customerName").GetString());
        }

        [Fact(DisplayName = "POST /api/orders with missing CustomerName returns 400")]
        public async Task Create_MissingCustomerName_Returns400()
        {
            var payload = new
            {
                customerName = "",
                lines = new[] { new { productId = 1, quantity = 1 } }
            };

            var response = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders with empty lines returns 400")]
        public async Task Create_EmptyLines_Returns400()
        {
            var payload = new
            {
                customerName = "Acme",
                lines = Array.Empty<object>()
            };

            var response = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders with zero quantity returns 400")]
        public async Task Create_ZeroQuantity_Returns400()
        {
            var payload = new
            {
                customerName = "Acme",
                lines = new[] { new { productId = 1, quantity = 0 } }
            };

            var response = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders with unknown productId returns 400")]
        public async Task Create_UnknownProduct_Returns400()
        {
            var payload = new
            {
                customerName = "Acme",
                lines = new[] { new { productId = 9999, quantity = 1 } }
            };

            var response = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders — created order number starts with ORD-")]
        public async Task Create_OrderNumber_StartsWithOrdPrefix()
        {
            var created = await CreateValidOrderAsync();

            Assert.StartsWith("ORD-", created.GetProperty("orderNumber").GetString());
        }

        [Fact(DisplayName = "POST /api/orders — UnitPriceAtOrder is captured from product catalogue")]
        public async Task Create_UnitPriceAtOrder_CapturedFromCatalogue()
        {
            var created = await CreateValidOrderAsync();

            var lines = created.GetProperty("lines");
            Assert.Equal(1, lines.GetArrayLength());
            var line = lines[0];
            Assert.Equal(1240.00m, line.GetProperty("unitPriceAtOrder").GetDecimal());
        }

        // =====================================================================
        // POST api/orders/{id}/allocate
        // =====================================================================

        [Fact(DisplayName = "POST /api/orders/{id}/allocate on Pending order returns 200 with Allocated status")]
        public async Task Allocate_PendingOrder_Returns200WithAllocatedStatus()
        {
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();

            var response = await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("Allocated", doc.RootElement.GetProperty("status").GetString());
        }

        [Fact(DisplayName = "POST /api/orders/{id}/allocate on already-Allocated order returns 400")]
        public async Task Allocate_AlreadyAllocated_Returns400()
        {
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();
            await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            var response = await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders/{id}/allocate when stock is insufficient returns 400")]
        public async Task Allocate_InsufficientStock_Returns400()
        {
            // Pump has 25 on hand; request 999.
            var payload = new
            {
                customerName = "Oversell Corp",
                lines = new[] { new { productId = 1, quantity = 999 } }
            };
            var created = await _client.PostAsJsonAsync("/api/orders", payload);
            var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            int orderId = createdDoc.RootElement.GetProperty("orderId").GetInt32();

            var response = await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // =====================================================================
        // POST api/orders/{id}/ship
        // =====================================================================

        [Fact(DisplayName = "POST /api/orders/{id}/ship on Allocated order returns 200 with Shipment")]
        public async Task Ship_AllocatedOrder_Returns200WithShipment()
        {
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();
            await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            var shipPayload = new { carrier = "DHL", trackingNumber = "TRK-001" };
            var response = await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", shipPayload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("DHL", doc.RootElement.GetProperty("carrier").GetString());
            Assert.Equal("TRK-001", doc.RootElement.GetProperty("trackingNumber").GetString());
        }

        [Fact(DisplayName = "POST /api/orders/{id}/ship on Pending order returns 400")]
        public async Task Ship_PendingOrder_Returns400()
        {
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();

            var shipPayload = new { carrier = "DHL", trackingNumber = "TRK-001" };
            var response = await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", shipPayload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders/{id}/ship with missing Carrier returns 400")]
        public async Task Ship_MissingCarrier_Returns400()
        {
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();
            await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            var shipPayload = new { carrier = "", trackingNumber = "TRK-001" };
            var response = await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", shipPayload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/orders/{id}/ship with missing TrackingNumber returns 400")]
        public async Task Ship_MissingTrackingNumber_Returns400()
        {
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();
            await _client.PostAsync($"/api/orders/{orderId}/allocate", null);

            var shipPayload = new { carrier = "DHL", trackingNumber = "" };
            var response = await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", shipPayload);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // =====================================================================
        // Full lifecycle
        // =====================================================================

        [Fact(DisplayName = "Full order lifecycle: Create -> Allocate -> Ship produces 200 at each step")]
        public async Task FullLifecycle_Returns200AtEachStep()
        {
            // Create
            var order = await CreateValidOrderAsync();
            int orderId = order.GetProperty("orderId").GetInt32();
            Assert.Equal("Pending", order.GetProperty("status").GetString());

            // Allocate
            var allocResp = await _client.PostAsync($"/api/orders/{orderId}/allocate", null);
            Assert.Equal(HttpStatusCode.OK, allocResp.StatusCode);

            // Ship
            var shipPayload = new { carrier = "FedEx", trackingNumber = "TRK-LIFE" };
            var shipResp = await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", shipPayload);
            Assert.Equal(HttpStatusCode.OK, shipResp.StatusCode);
        }

        // =====================================================================
        // Helper
        // =====================================================================

        private async Task<JsonElement> CreateValidOrderAsync()
        {
            var payload = new
            {
                customerName = "Test Customer",
                destinationCity = "Berlin",
                destinationCountry = "Germany",
                lines = new[] { new { productId = 1, quantity = 2 } }
            };
            var response = await _client.PostAsJsonAsync("/api/orders", payload);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body).RootElement.Clone();
        }
    }
}
