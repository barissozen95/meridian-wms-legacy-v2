using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MeridianWms.Core.Data;
using MeridianWms.Core.Middleware;
using MeridianWms.Core.Services;

// Opt into the modern (non-legacy) Npgsql timestamp behaviour so that DateTime
// values stored without a Kind are treated as UTC and mapped to 'timestamp with
// time zone' in PostgreSQL, matching the application's UTC-only convention.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", false);

var builder = WebApplication.CreateBuilder(args);

// ---- Services ---------------------------------------------------------------

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        // Ignore self-referencing loops when serializing EF entities with navigation properties
        opts.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// EF Core with PostgreSQL — connection string from appsettings.json
builder.Services.AddDbContext<WmsDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("WmsDb"),
        npgsqlOpts => npgsqlOpts.EnableRetryOnFailure()));

// Application services (scoped so each request gets a fresh instance)
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<InventoryService>();

builder.Services.AddEndpointsApiExplorer();

// ---- App pipeline -----------------------------------------------------------

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Global exception handler — must be first in the pipeline so it can catch
// exceptions thrown by any subsequent middleware or controller.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseRouting();

app.MapControllers();

app.Run();

// Make Program accessible to the integration test project.
public partial class Program { }
