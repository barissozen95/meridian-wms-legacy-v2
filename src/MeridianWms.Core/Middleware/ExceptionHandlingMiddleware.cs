using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MeridianWms.Core.Middleware
{
    /// <summary>
    /// Global exception-handling middleware.  Returns the same error response shapes
    /// as the legacy API: business-rule violations (InvalidOperationException) surface
    /// as 400 Bad Request; all other unhandled exceptions become 500 Internal Server
    /// Error with a generic message so internal details are never disclosed to callers.
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Business rule violation: {Message}", ex.Message);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "application/json";
                var body = JsonSerializer.Serialize(new { message = ex.Message });
                await context.Response.WriteAsync(body);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Argument error: {Message}", ex.Message);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "application/json";
                var body = JsonSerializer.Serialize(new { message = ex.Message });
                await context.Response.WriteAsync(body);
            }
            catch (Exception ex)
            {
                // FIX (High — error disclosure): never expose internal detail to callers.
                _logger.LogError(ex, "Unhandled exception");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";
                var body = JsonSerializer.Serialize(new { message = "An unexpected error occurred." });
                await context.Response.WriteAsync(body);
            }
        }
    }
}
