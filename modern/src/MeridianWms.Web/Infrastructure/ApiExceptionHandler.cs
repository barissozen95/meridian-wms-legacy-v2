using MeridianWms.Web.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeridianWms.Web.Infrastructure;

// Single place mapping exceptions to RFC 7807 responses. Business messages are surfaced;
// anything unexpected becomes a generic 500 (logged server-side, never leaked).
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        (int status, string title, string? detail) = exception switch
        {
            DomainException e => (StatusCodes.Status400BadRequest, "Business rule violation", e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, "Not found", e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict",
                "The data was modified by another request. Retry the operation."),
            // Unique indexes from scripts/postgres/post-load-hardening.sql (e.g. UX_Product_Sku).
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Duplicate", "A record with the same unique key already exists."),
            _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogInformation("{Title}: {Detail}", title, detail);
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
