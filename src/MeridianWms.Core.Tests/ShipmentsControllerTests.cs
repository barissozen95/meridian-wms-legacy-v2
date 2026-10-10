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
    /// Integration tests for ShipmentsController.
    /// </summary>
    public class ShipmentsControllerTests : IDisposable
    {
        private readonly WmsWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public ShipmentsControllerTests()
        {
            _factory = new WmsWebApplicationFactory();
            _client = _factory.CreateSeededClient();
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
        }

        [Fact(DisplayName = "GET /api/shipments returns 200 OK with a JSON array")]
        public async Task GetAll_Returns200_WithArray()
        {
            var response = await _client.GetAsync("/api/shipments");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        }

        [Fact(DisplayName = "GET /api/shipments/order/{orderId} returns 200 OK with empty array for non-existent order")]
        public async Task GetForOrder_NonExistent_Returns200_EmptyArray()
        {
            var response = await _client.GetAsync("/api/shipments/order/99999");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        [Fact(DisplayName = "GET /api/shipments/order/{orderId} returns the shipment after a full order lifecycle")]
        public async Task GetForOrder_AfterShipment_ReturnsShipmentRecord()
        {
            // Create -> Allocate -> Ship
            var createPayload = new
            {
                customerName = "Shipment Test Co",
                destinationCity = "Paris",
                destinationCountry = "France",
                lines = new[] { new { productId = 1, quantity = 1 } }
            };
            var createResp = await _client.PostAsJsonAsync("/api/orders", createPayload);
            var createDoc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
            int orderId = createDoc.RootElement.GetProperty("orderId").GetInt32();

            await _client.PostAsync($"/api/orders/{orderId}/allocate", null);
            var shipPayload = new { carrier = "UPS", trackingNumber = "TRK-SHIP-01" };
            await _client.PostAsJsonAsync($"/api/orders/{orderId}/ship", shipPayload);

            var response = await _client.GetAsync($"/api/shipments/order/{orderId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(1, doc.RootElement.GetArrayLength());
            Assert.Equal("UPS", doc.RootElement[0].GetProperty("carrier").GetString());
        }
    }
}
