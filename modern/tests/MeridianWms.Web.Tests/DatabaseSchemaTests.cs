namespace MeridianWms.Web.Tests;

// Proves the migrated PostgreSQL schema matches the legacy SQL Server contract and that the
// hardening script applied cleanly on top of it.
public class DatabaseSchemaTests(PostgresFixture fx) : ApiTestBase(fx)
{
    [Fact]
    public async Task Migration_IsRecordedInHistory()
    {
        var applied = await Db.ScalarAsync<string>(@"SELECT string_agg(""MigrationId"", ',') FROM ""__EFMigrationsHistory""");
        Assert.EndsWith("_InitialPostgres", applied);
    }

    [Theory]
    [InlineData("Product", "UnitWeightKg", "numeric(10,3)")]
    [InlineData("Product", "UnitPrice", "numeric(12,2)")]
    [InlineData("Product", "Sku", "character varying(40)")]
    [InlineData("Product", "IsActive", "boolean")]
    [InlineData("OrderLine", "UnitPriceAtOrder", "numeric(12,2)")]
    [InlineData("Shipment", "TotalWeightKg", "numeric(10,3)")]
    [InlineData("Order", "Status", "character varying(20)")]
    [InlineData("Order", "CreatedUtc", "timestamp with time zone")]
    [InlineData("Shipment", "ShippedUtc", "timestamp with time zone")]
    public async Task Column_HasLegacyCompatibleType(string table, string column, string expected)
    {
        var actual = await Db.ScalarAsync<string>($@"
            SELECT format_type(a.atttypid, a.atttypmod) FROM pg_attribute a
            WHERE a.attrelid = '""{table}""'::regclass AND a.attname = '{column}'");
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("UX_Product_Sku")]
    [InlineData("UX_Warehouse_Code")]
    [InlineData("UX_Order_OrderNumber")]
    [InlineData("UX_InventoryItem_Prod_Wh")]
    [InlineData("IX_Product_Name_trgm")]
    public async Task HardeningIndex_Exists(string index) =>
        Assert.Equal(1L, await Db.ScalarAsync<long>($"SELECT count(*) FROM pg_indexes WHERE indexname = '{index}'"));

    [Theory]
    [InlineData("FK_Shipment_Order_OrderId")]
    [InlineData("CK_InventoryItem_OnHand_NonNegative")]
    [InlineData("CK_OrderLine_Quantity_Positive")]
    [InlineData("CK_Order_Status_Known")]
    public async Task HardeningConstraint_IsValidated(string name) =>
        Assert.True(await Db.ScalarAsync<bool>($"SELECT convalidated FROM pg_constraint WHERE conname = '{name}'"));

    [Fact]
    public async Task NoForeignKey_CascadesDeletes() =>
        Assert.Equal(0L, await Db.ScalarAsync<long>("SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND confdeltype = 'c'"));

    [Fact]
    public async Task Seed_MatchesLegacyRowCounts()
    {
        var counts = await Db.ScalarAsync<string>(@"SELECT concat_ws(',',
            (SELECT count(*) FROM ""Warehouse""), (SELECT count(*) FROM ""Product""), (SELECT count(*) FROM ""InventoryItem""),
            (SELECT count(*) FROM ""Order""), (SELECT count(*) FROM ""OrderLine""), (SELECT count(*) FROM ""Shipment""))");
        Assert.Equal("2,5,7,2,3,1", counts);
    }

    [Fact]
    public async Task Health_ReportsDatabaseReachable()
    {
        var res = await Client.GetAsync("/health");
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }
}
