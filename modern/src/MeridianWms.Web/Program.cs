using System.Text.Json.Serialization;
using MeridianWms.Web.Data;
using MeridianWms.Web.Infrastructure;
using MeridianWms.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    // Keep the legacy Web API 2 / Newtonsoft wire format: PascalCase property names,
    // Status as a string ("Pending", ...), reference loops ignored.
    o.JsonSerializerOptions.PropertyNamingPolicy = null;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Replaces Web.config <connectionStrings>. Supply via env var ConnectionStrings__WmsDb in non-dev environments.
builder.Services.AddDbContext<WmsDbContext>(o =>
{
    string? cs = builder.Configuration.GetConnectionString("WmsDb");
    if (string.IsNullOrWhiteSpace(cs))
    {
        throw new InvalidOperationException("ConnectionStrings:WmsDb is not configured.");
    }
    o.UseNpgsql(cs);
});

builder.Services.Configure<WmsOptions>(builder.Configuration.GetSection(WmsOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddHealthChecks().AddDbContextCheck<WmsDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
