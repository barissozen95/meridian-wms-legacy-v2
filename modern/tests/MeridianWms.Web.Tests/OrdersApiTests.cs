using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeridianWms.Web.Tests;

public class OrdersApiTests(PostgresFixture fx) : ApiTestBase(fx)
{
    private async Task<JsonElement> CreateOrderAsync(params (int ProductId, int Quantity)[] lines)
    {
        var res = await Client.PostAsJsonAsync("/api/orders", new
        {
            CustomerName = "Acme Pumps Ltd",
            DestinationCity = "Leeds",
            DestinationCountry = "UK",
            Lines = lines.Select(l => new { l.ProductId, l.Quantity }),
        });
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> ShipAsync(int orderId) =>
        Client.PostAsJsonAsync($"/api/orders/{orderId}/ship", new { Carrier = "DHL Freight", TrackingNumber = "TRK-1" });

    private Task<string> StockAsync(int inventoryItemId) => Db.ScalarAsync<string>(
        $@"SELECT ""QuantityOnHand"" || '/' || ""QuantityReserved"" FROM ""InventoryItem"" WHERE ""InventoryItemId"" = {inventoryItemId}");

    [Fact]
    public async Task GetAll_OrdersByCreatedUtcDescending_WithStringStatus()
    {
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/orders");
        Assert.Equal(["ORD-20240220141500", "ORD-20240115093000"],
            json.EnumerateArray().Select(o => o.GetProperty("OrderNumber").GetString()));
        Assert.Equal("Pending", json[0].GetProperty("Status").GetString());
    }

    [Fact]
    public async Task GetById_IncludesLines_UnknownIs404()
    {
        var order = await Client.GetFromJsonAsync<JsonElement>("/api/orders/1");
        Assert.Equal(2, order.GetProperty("Lines").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync("/api/orders/999")).StatusCode);
    }

    [Fact]
    public async Task Create_IsPending_SnapshotsPrice_UsesUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);
        var order = await CreateOrderAsync((1, 2), (3, 4));

        Assert.Equal("Pending", order.GetProperty("Status").GetString());
        Assert.StartsWith("ORD-", order.GetProperty("OrderNumber").GetString());
        var created = order.GetProperty("CreatedUtc").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, created.Kind);
        Assert.InRange(created, before, DateTime.UtcNow.AddSeconds(5));

        var lines = order.GetProperty("Lines");
        Assert.Equal(1240.00m, lines[0].GetProperty("UnitPriceAtOrder").GetDecimal());
        Assert.Equal(8.50m, lines[1].GetProperty("UnitPriceAtOrder").GetDecimal());

        // Price is a snapshot: changing the catalogue afterwards does not change the line.
        await Db.ExecuteAsync(@"UPDATE ""Product"" SET ""UnitPrice"" = 9999 WHERE ""ProductId"" = 1");
        var reloaded = await Client.GetFromJsonAsync<JsonElement>($"/api/orders/{order.GetProperty("OrderId").GetInt32()}");
        Assert.Equal(1240.00m, reloaded.GetProperty("Lines")[0].GetProperty("UnitPriceAtOrder").GetDecimal());
    }

    [Fact]
    public async Task Create_ManyInSameSecond_GetsUniqueOrderNumbers()
    {
        var orders = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CreateOrderAsync((2, 1))));
        Assert.Equal(10, orders.Select(o => o.GetProperty("OrderNumber").GetString()).Distinct().Count());
    }

    [Theory]
    [InlineData("""{"CustomerName":"A","Lines":[]}""")]
    [InlineData("""{"CustomerName":"A","Lines":[{"ProductId":1,"Quantity":0}]}""")]
    [InlineData("""{"CustomerName":"A","Lines":[{"ProductId":1,"Quantity":-3}]}""")]
    [InlineData("""{"CustomerName":"","Lines":[{"ProductId":1,"Quantity":1}]}""")]
    [InlineData("""{"Lines":[{"ProductId":1,"Quantity":1}]}""")]
    [InlineData("""{"CustomerName":"A"}""")]
    public async Task Create_InvalidRequest_Returns400_AndWritesNothing(string body)
    {
        var res = await Client.PostAsync("/api/orders", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(2L, await Db.ScalarAsync<long>(@"SELECT count(*) FROM ""Order"""));
    }

    [Fact]
    public async Task Create_UnknownProduct_Returns400()
    {
        var res = await Client.PostAsJsonAsync("/api/orders", new { CustomerName = "A", Lines = new[] { new { ProductId = 999, Quantity = 1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Unknown product: 999", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Allocate_ReservesStock_WithoutTouchingOnHand()
    {
        var res = await Client.PostAsync("/api/orders/2/allocate", null); // seeded: 2 x product 1
        res.EnsureSuccessStatusCode();

        Assert.Equal("Allocated", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Status").GetString());
        Assert.Equal("25/2", await StockAsync(1));
        Assert.Equal("10/0", await StockAsync(6)); // WH-WEST untouched: allocation uses default warehouse only
    }

    [Fact]
    public async Task Allocate_ExactAvailableStock_Succeeds()
    {
        var order = await CreateOrderAsync((3, 6));
        (await Client.PostAsync($"/api/orders/{order.GetProperty("OrderId").GetInt32()}/allocate", null)).EnsureSuccessStatusCode();
        Assert.Equal("6/6", await StockAsync(3));
    }

    [Fact]
    public async Task Allocate_Twice_Returns400()
    {
        (await Client.PostAsync("/api/orders/2/allocate", null)).EnsureSuccessStatusCode();
        var res = await Client.PostAsync("/api/orders/2/allocate", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Only Pending orders can be allocated.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Allocate_MultiLineOversell_IsAllOrNothing()
    {
        var order = await CreateOrderAsync((1, 5), (3, 7)); // line 2 exceeds the 6 available
        var res = await Client.PostAsync($"/api/orders/{order.GetProperty("OrderId").GetInt32()}/allocate", null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Insufficient stock for product 3", await res.Content.ReadAsStringAsync());
        Assert.Equal("25/0", await StockAsync(1)); // first line's reservation not persisted
        Assert.Equal("Pending", await Db.ScalarAsync<string>($@"SELECT ""Status"" FROM ""Order"" WHERE ""OrderId"" = {order.GetProperty("OrderId").GetInt32()}"));
    }

    [Fact]
    public async Task Allocate_ProductNotInDefaultWarehouse_Returns400()
    {
        await Db.ExecuteAsync(@"DELETE FROM ""InventoryItem"" WHERE ""InventoryItemId"" = 5");
        var order = await CreateOrderAsync((5, 1));
        var res = await Client.PostAsync($"/api/orders/{order.GetProperty("OrderId").GetInt32()}/allocate", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not stocked in warehouse WH-CENTRAL", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Allocate_UnknownOrder_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.PostAsync("/api/orders/999/allocate", null)).StatusCode);
    }

    [Fact]
    public async Task Allocate_ConcurrentRequestsForSameStock_NeverOversell()
    {
        // Motor stock: 12. Two orders of 8 each - only one can be allocated.
        var a = (await CreateOrderAsync((4, 8))).GetProperty("OrderId").GetInt32();
        var b = (await CreateOrderAsync((4, 8))).GetProperty("OrderId").GetInt32();

        var results = await Task.WhenAll(
            Client.PostAsync($"/api/orders/{a}/allocate", null),
            Client.PostAsync($"/api/orders/{b}/allocate", null));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest);
        Assert.Equal("12/8", await StockAsync(4));
    }

    [Fact]
    public async Task Ship_PendingOrder_Returns400()
    {
        var res = await ShipAsync(2);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("Only Allocated orders can be shipped.", await res.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{"Carrier":"","TrackingNumber":"T"}""")]
    [InlineData("""{"Carrier":"DHL"}""")]
    public async Task Ship_InvalidRequest_Returns400(string body)
    {
        (await Client.PostAsync("/api/orders/2/allocate", null)).EnsureSuccessStatusCode();
        var res = await Client.PostAsync("/api/orders/2/ship", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_PendingAllocatedShipped_KeepsStockConsistent()
    {
        var order = await CreateOrderAsync((1, 3), (2, 10), (3, 2));
        int id = order.GetProperty("OrderId").GetInt32();

        (await Client.PostAsync($"/api/orders/{id}/allocate", null)).EnsureSuccessStatusCode();
        Assert.Equal("25/3", await StockAsync(1));

        var shipRes = await ShipAsync(id);
        shipRes.EnsureSuccessStatusCode();
        var shipment = await shipRes.Content.ReadFromJsonAsync<JsonElement>();

        // 3*18.500 + 10*0.750 + 2*0.110 = 63.220 (exact decimal arithmetic end to end)
        Assert.Equal(63.220m, shipment.GetProperty("TotalWeightKg").GetDecimal());
        Assert.Equal("WH-CENTRAL", shipment.GetProperty("FromWarehouseCode").GetString());
        Assert.Equal(DateTimeKind.Utc, shipment.GetProperty("ShippedUtc").GetDateTime().Kind);

        Assert.Equal("22/0", await StockAsync(1));
        Assert.Equal("470/0", await StockAsync(2));
        Assert.Equal("4/0", await StockAsync(3));

        var final = await Client.GetFromJsonAsync<JsonElement>($"/api/orders/{id}");
        Assert.Equal("Shipped", final.GetProperty("Status").GetString());

        var forOrder = await Client.GetFromJsonAsync<JsonElement>($"/api/shipments/order/{id}");
        Assert.Equal("TRK-1", Assert.Single(forOrder.EnumerateArray()).GetProperty("TrackingNumber").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await ShipAsync(id)).StatusCode); // cannot ship twice
    }

    [Fact]
    public async Task Shipments_GetAll_ReturnsNewestFirst()
    {
        (await Client.PostAsync("/api/orders/2/allocate", null)).EnsureSuccessStatusCode();
        (await ShipAsync(2)).EnsureSuccessStatusCode();

        var json = await Client.GetFromJsonAsync<JsonElement>("/api/shipments");
        Assert.Equal([2, 1], json.EnumerateArray().Select(s => s.GetProperty("OrderId").GetInt32()));
        Assert.Equal(48.500m, json[1].GetProperty("TotalWeightKg").GetDecimal());
    }
}
