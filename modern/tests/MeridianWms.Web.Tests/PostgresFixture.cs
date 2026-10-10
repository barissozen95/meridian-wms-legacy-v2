using MeridianWms.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MeridianWms.Web.Tests;

// One real PostgreSQL container for the whole run. Startup mirrors the production cut-over:
// 1) EF Core migrations  2) data load (dev seed)  3) scripts/postgres/post-load-hardening.sql.
public sealed class PostgresFixture : IAsyncLifetime
{
    private static readonly string[] Tables = ["Shipment", "OrderLine", "Order", "InventoryItem", "Product", "Warehouse"];

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:WmsDb", ConnectionString);
        });

        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<WmsDbContext>().Database.MigrateAsync();
        }
        await ExecuteSqlFileAsync("seed-dev.sql");
        await ExecuteSqlFileAsync("post-load-hardening.sql");
    }

    // Restores the seed state; keeps schema, constraints and indexes from InitializeAsync.
    public async Task ResetAsync()
    {
        await ExecuteAsync($"TRUNCATE {string.Join(", ", Tables.Select(t => $"\"{t}\""))} RESTART IDENTITY;");
        await ExecuteSqlFileAsync("seed-dev.sql");
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private Task ExecuteSqlFileAsync(string name) =>
        ExecuteAsync(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", name)));

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

// Base class: every test starts from the seed data and gets a client for the real app pipeline.
[Collection(PostgresCollection.Name)]
public abstract class ApiTestBase(PostgresFixture fx) : IAsyncLifetime
{
    protected PostgresFixture Db { get; } = fx;
    protected HttpClient Client { get; } = fx.Factory.CreateClient();

    public Task InitializeAsync() => Db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}
