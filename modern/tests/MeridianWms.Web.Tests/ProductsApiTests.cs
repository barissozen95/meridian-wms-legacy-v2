using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeridianWms.Web.Tests;

public class ProductsApiTests(PostgresFixture fx) : ApiTestBase(fx)
{
    [Fact]
    public async Task GetAll_ReturnsActiveProducts_InLegacyPascalCaseShape()
    {
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/products");

        Assert.Equal(5, json.GetArrayLength());
        var pump = json[0];
        Assert.Equal("PMP-3000-A", pump.GetProperty("Sku").GetString());
        Assert.Equal(1240.00m, pump.GetProperty("UnitPrice").GetDecimal());
        Assert.Equal(18.500m, pump.GetProperty("UnitWeightKg").GetDecimal());
        Assert.Equal(2, pump.GetProperty("InventoryItems").GetArrayLength());
    }

    [Fact]
    public async Task GetAll_ExcludesInactiveProducts()
    {
        await Db.ExecuteAsync(@"UPDATE ""Product"" SET ""IsActive"" = false WHERE ""ProductId"" = 5");
        var json = await Client.GetFromJsonAsync<JsonElement>("/api/products");
        Assert.Equal(4, json.GetArrayLength());
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        var res = await Client.GetAsync("/api/products/999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Create_PersistsActiveProduct_WithNextIdentity()
    {
        var res = await Client.PostAsJsonAsync("/api/products", new
        {
            Sku = "GSK-100", Name = "Flange Gasket 100mm", Category = "Seals", UnitWeightKg = 0.045m, UnitPrice = 3.20m,
        });
        res.EnsureSuccessStatusCode();
        var created = await res.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(6, created.GetProperty("ProductId").GetInt32());
        Assert.True(created.GetProperty("IsActive").GetBoolean());
        Assert.Equal(0.045m, await Db.ScalarAsync<decimal>(@"SELECT ""UnitWeightKg"" FROM ""Product"" WHERE ""Sku"" = 'GSK-100'"));
    }

    [Fact]
    public async Task Create_DuplicateSku_Returns409_FromUniqueIndex()
    {
        var res = await Client.PostAsJsonAsync("/api/products", new { Sku = "BRG-6204", Name = "Dup", UnitWeightKg = 1m, UnitPrice = 1m });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Create_MissingName_Returns400()
    {
        var res = await Client.PostAsJsonAsync("/api/products", new { Sku = "X-1", UnitWeightKg = 1m, UnitPrice = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
