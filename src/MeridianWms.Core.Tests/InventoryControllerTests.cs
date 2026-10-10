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
    /// Integration tests for InventoryController.
    /// Note: SearchByProductName uses raw ADO.NET against a real SQL Server database
    /// and cannot be exercised with the in-memory provider — that endpoint is documented
    /// as a coverage gap here (matching the legacy test suite's behaviour).
    /// </summary>
    public class InventoryControllerTests : IDisposable
    {
        private readonly WmsWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public InventoryControllerTests()
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
        // GET api/inventory/warehouse/{code}
        // =====================================================================

        [Fact(DisplayName = "GET /api/inventory/warehouse/WH-CENTRAL returns 200 with 5 items")]
        public async Task GetForWarehouse_KnownCode_Returns200_With5Items()
        {
            var response = await _client.GetAsync("/api/inventory/warehouse/WH-CENTRAL");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(5, doc.RootElement.GetArrayLength());
        }

        [Fact(DisplayName = "GET /api/inventory/warehouse/WH-WEST returns 200 with 2 items")]
        public async Task GetForWarehouse_WestCode_Returns200_With2Items()
        {
            var response = await _client.GetAsync("/api/inventory/warehouse/WH-WEST");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(2, doc.RootElement.GetArrayLength());
        }

        [Fact(DisplayName = "GET /api/inventory/warehouse/{code} for unknown code returns 200 with empty array")]
        public async Task GetForWarehouse_UnknownCode_Returns200_EmptyArray()
        {
            var response = await _client.GetAsync("/api/inventory/warehouse/WH-UNKNOWN");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(0, doc.RootElement.GetArrayLength());
        }

        // =====================================================================
        // GET api/inventory/low-stock
        // =====================================================================

        [Fact(DisplayName = "GET /api/inventory/low-stock returns 200 with JSON array")]
        public async Task GetLowStock_Returns200_WithArray()
        {
            var response = await _client.GetAsync("/api/inventory/low-stock");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        }

        [Fact(DisplayName = "GET /api/inventory/low-stock result objects have Sku, Warehouse, Available, Bin properties")]
        public async Task GetLowStock_ResultShape_HasExpectedProperties()
        {
            var response = await _client.GetAsync("/api/inventory/low-stock");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            // Seed has BRG-6204 (available=6) and FLT-AIR-M (available=8) — both below threshold 10
            Assert.True(doc.RootElement.GetArrayLength() > 0, "Expected at least one low-stock item");

            var first = doc.RootElement[0];
            Assert.True(first.TryGetProperty("sku", out _), "Expected 'sku' property");
            Assert.True(first.TryGetProperty("warehouse", out _), "Expected 'warehouse' property");
            Assert.True(first.TryGetProperty("available", out _), "Expected 'available' property");
            Assert.True(first.TryGetProperty("bin", out _), "Expected 'bin' property");
        }

        [Fact(DisplayName = "GET /api/inventory/low-stock includes BRG-6204 and FLT-AIR-M from seed")]
        public async Task GetLowStock_ContainsExpectedSeedItems()
        {
            var response = await _client.GetAsync("/api/inventory/low-stock");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            var skus = new System.Collections.Generic.List<string?>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                skus.Add(item.GetProperty("sku").GetString());
            }

            Assert.Contains("BRG-6204", skus);
            Assert.Contains("FLT-AIR-M", skus);
            Assert.DoesNotContain("PMP-3000-A", skus); // OnHand=25 > threshold=10
        }

        // =====================================================================
        // POST api/inventory/adjust
        // =====================================================================

        [Fact(DisplayName = "POST /api/inventory/adjust with positive delta returns 200 OK")]
        public async Task Adjust_PositiveDelta_Returns200()
        {
            var response = await _client.PostAsync(
                "/api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=5", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/inventory/adjust with negative delta that would go negative returns 400")]
        public async Task Adjust_WouldGoNegative_Returns400()
        {
            // Pump OnHand=25; delta=-999 would result in -974
            var response = await _client.PostAsync(
                "/api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=-999", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact(DisplayName = "POST /api/inventory/adjust with zero delta returns 200 OK (no-op)")]
        public async Task Adjust_ZeroDelta_Returns200()
        {
            var response = await _client.PostAsync(
                "/api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=0", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // =====================================================================
        // GET api/inventory/search — coverage gap documentation
        // =====================================================================

        // NOTE: SearchByProductName uses raw ADO.NET NpgsqlConnection against a real
        // PostgreSQL database and cannot be tested with the EF Core in-memory provider.
        // This mirrors the Ignored test in the legacy MSTest suite.
        // Integration tests against a real database are required to cover this endpoint.
    }
}
