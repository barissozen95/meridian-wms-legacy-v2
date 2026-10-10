using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeridianWms.Web.Tests;

public class InventoryApiTests(PostgresFixture fx) : ApiTestBase(fx)
{
    private Task<int> OnHandAsync(int productId, int warehouseId) => Db.ScalarAsync<int>(
        $@"SELECT ""QuantityOnHand"" FROM ""InventoryItem"" WHERE ""ProductId"" = {productId} AND ""WarehouseId"" = {warehouseId}");

    [Fact]
    public async Task GetForWarehouse_ReturnsOnlyThatWarehouse()
    {
        var central = await Client.GetFromJsonAsync<JsonElement>("/api/inventory/warehouse/WH-CENTRAL");
        var west = await Client.GetFromJsonAsync<JsonElement>("/api/inventory/warehouse/WH-WEST");

        Assert.Equal(5, central.GetArrayLength());
        Assert.Equal(2, west.GetArrayLength());
        Assert.All(west.EnumerateArray(), i => Assert.Equal(2, i.GetProperty("WarehouseId").GetInt32()));
    }

    [Fact]
    public async Task GetForWarehouse_UnknownCode_ReturnsEmpty()
    {
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/inventory/warehouse/NOPE");
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task LowStock_UsesAtOrBelowThreshold_AcrossWarehouses()
    {
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/inventory/low-stock");
        var rows = json.EnumerateArray()
            .Select(r => $"{r.GetProperty("Sku").GetString()}@{r.GetProperty("Warehouse").GetString()}={r.GetProperty("Available").GetInt32()}/{r.GetProperty("Bin").GetString()}")
            .ToList();

        // 6 and 8 are below 10; WH-WEST pump is exactly 10 (boundary is inclusive).
        Assert.Equal(["BRG-6204@WH-CENTRAL=6/C-09-3", "FLT-AIR-M@WH-CENTRAL=8/D-11-4", "PMP-3000-A@WH-WEST=10/A-01-1"], rows);
    }

    [Fact]
    public async Task LowStock_ConsidersReservedQuantity()
    {
        await Db.ExecuteAsync(@"UPDATE ""InventoryItem"" SET ""QuantityReserved"" = 15 WHERE ""InventoryItemId"" = 1"); // 25-15 = 10
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/inventory/low-stock");
        Assert.Contains(json.EnumerateArray(), r => r.GetProperty("Sku").GetString() == "PMP-3000-A" && r.GetProperty("Warehouse").GetString() == "WH-CENTRAL");
    }

    [Theory]
    [InlineData("pump", 2)]   // case-insensitive, like the SQL Server default collation
    [InlineData("VALVE", 2)]
    [InlineData("bearing", 1)]
    [InlineData("zzz", 0)]
    public async Task Search_MatchesNameFragment_CaseInsensitive(string name, int expected)
    {
        var json = await Client.GetFromJsonAsync<JsonElement>($"/api/inventory/search?name={Uri.EscapeDataString(name)}");
        Assert.Equal(expected, json.GetArrayLength());
    }

    [Fact]
    public async Task Search_ReturnsLegacyRowShape()
    {
        var row = (await Client.GetFromJsonAsync<JsonElement>("/api/inventory/search?name=bearing"))[0];
        Assert.Equal("BRG-6204", row.GetProperty("Sku").GetString());
        Assert.Equal("Ball Bearing 6204", row.GetProperty("Name").GetString());
        Assert.Equal("WH-CENTRAL", row.GetProperty("Warehouse").GetString());
        Assert.Equal(6, row.GetProperty("QuantityOnHand").GetInt32());
    }

    [Theory]
    [InlineData("' OR '1'='1")]
    [InlineData("%'; DROP TABLE \"Product\"; --")]
    public async Task Search_SqlInjectionPayload_IsTreatedAsLiteral(string payload)
    {
        var json = await Client.GetFromJsonAsync<JsonElement>($"/api/inventory/search?name={Uri.EscapeDataString(payload)}");
        Assert.Equal(0, json.GetArrayLength());
        Assert.Equal(5L, await Db.ScalarAsync<long>(@"SELECT count(*) FROM ""Product"""));
    }

    [Fact]
    public async Task Search_EmptyName_ReturnsEmpty()
    {
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/inventory/search?name=");
        Assert.Equal(0, json.GetArrayLength());
    }

    [Fact]
    public async Task Adjust_PositiveAndNegativeDeltas_Persist()
    {
        (await Client.PostAsync("/api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=5", null)).EnsureSuccessStatusCode();
        Assert.Equal(30, await OnHandAsync(1, 1));

        (await Client.PostAsync("/api/inventory/adjust?productId=1&warehouseCode=WH-CENTRAL&delta=-30", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, await OnHandAsync(1, 1)); // exactly zero is allowed
        Assert.Equal(10, await OnHandAsync(1, 2)); // other warehouse untouched
    }

    [Fact]
    public async Task Adjust_BelowZero_Returns400_AndDoesNotPersist()
    {
        var res = await Client.PostAsync("/api/inventory/adjust?productId=3&warehouseCode=WH-CENTRAL&delta=-7", null);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("cannot be negative", await res.Content.ReadAsStringAsync());
        Assert.Equal(6, await OnHandAsync(3, 1));
    }

    [Theory]
    [InlineData("productId=1&warehouseCode=WH-NOPE&delta=1")]
    [InlineData("productId=3&warehouseCode=WH-WEST&delta=1")]
    public async Task Adjust_UnknownProductOrWarehouse_Returns404(string query)
    {
        var res = await Client.PostAsync($"/api/inventory/adjust?{query}", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
