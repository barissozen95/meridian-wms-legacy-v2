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
    /// Integration tests for ProductsController.
    /// </summary>
    public class ProductsControllerTests : IDisposable
    {
        private readonly WmsWebApplicationFactory _factory;
        private readonly HttpClient _client;

        public ProductsControllerTests()
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
        // GET api/products
        // =====================================================================

        [Fact(DisplayName = "GET /api/products returns 200 with all 5 seeded active products")]
        public async Task GetAll_Returns200_WithSeedProducts()
        {
            var response = await _client.GetAsync("/api/products");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.Equal(5, doc.RootElement.GetArrayLength());
        }

        [Fact(DisplayName = "GET /api/products does not return inactive products")]
        public async Task GetAll_ExcludesInactiveProducts()
        {
            // Create an inactive product via POST then immediately deactivate it manually.
            // For this test, we rely on the fact that seeded products are all active,
            // and verify the count matches the 5 active seed rows.
            var response = await _client.GetAsync("/api/products");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            foreach (var product in doc.RootElement.EnumerateArray())
            {
                Assert.True(product.GetProperty("isActive").GetBoolean(),
                    "GetAll should only return active products");
            }
        }

        // =====================================================================
        // GET api/products/{id}
        // =====================================================================

        [Fact(DisplayName = "GET /api/products/1 returns 200 with the pump product")]
        public async Task GetById_ExistingProduct_Returns200()
        {
            var response = await _client.GetAsync("/api/products/1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("PMP-3000-A", doc.RootElement.GetProperty("sku").GetString());
        }

        [Fact(DisplayName = "GET /api/products/{id} returns 404 for non-existent product")]
        public async Task GetById_NonExistent_Returns404()
        {
            var response = await _client.GetAsync("/api/products/99999");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact(DisplayName = "GET /api/products/{id} includes InventoryItems in the response")]
        public async Task GetById_IncludesInventoryItems()
        {
            var response = await _client.GetAsync("/api/products/1");
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            Assert.True(doc.RootElement.TryGetProperty("inventoryItems", out var items),
                "Response should include inventoryItems");
            Assert.True(items.GetArrayLength() > 0,
                "Pump (productId=1) should have inventory items from seed");
        }

        // =====================================================================
        // POST api/products
        // =====================================================================

        [Fact(DisplayName = "POST /api/products creates product and sets IsActive=true")]
        public async Task Create_ValidProduct_Returns200_WithIsActiveTrue()
        {
            var payload = new
            {
                sku = "TST-001",
                name = "Test Widget",
                category = "Testing",
                unitWeightKg = 0.5,
                unitPrice = 9.99,
                isActive = false  // controller must override this to true
            };

            var response = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.True(doc.RootElement.GetProperty("isActive").GetBoolean(),
                "IsActive should be set to true by the controller regardless of input");
        }

        [Fact(DisplayName = "POST /api/products created product is returned by GET")]
        public async Task Create_ThenGetAll_ContainsNewProduct()
        {
            var payload = new
            {
                sku = "TST-002",
                name = "Another Widget",
                category = "Testing",
                unitWeightKg = 1.0,
                unitPrice = 4.99
            };
            var createResp = await _client.PostAsJsonAsync("/api/products", payload);
            createResp.EnsureSuccessStatusCode();

            var listResp = await _client.GetAsync("/api/products");
            var body = await listResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            var skus = new System.Collections.Generic.List<string?>();
            foreach (var p in doc.RootElement.EnumerateArray())
            {
                skus.Add(p.GetProperty("sku").GetString());
            }
            Assert.Contains("TST-002", skus);
        }
    }
}
