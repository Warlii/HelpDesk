using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HelpDesk.Api.Infrastructure;

public class ApiException(int status, string message) : Exception(message)
{
    public int Status
    {
        get;
    } = status;
}
public class ExceptionHandler(IProblemDetailsService problems, ILogger<ExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var status = exception switch
        {
            ApiException e => e.Status,
            DbUpdateConcurrencyException => 409,
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } => 409,
            _ => 500
        };
        if (status == 500)
            logger.LogError("Unhandled API exception type {Type}", exception.GetType().Name);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = "Request failed",
                Detail = exception is ApiException ? exception.Message : status == 409 ? "Resource changed or already exists." : "Unexpected server error."
            }
        });
    }
}
