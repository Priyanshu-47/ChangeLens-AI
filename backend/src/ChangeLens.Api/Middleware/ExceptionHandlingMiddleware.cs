using ChangeLens.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace ChangeLens.Api.Middleware;

/// <summary>
/// Centralized error handling. Expected application failures map to their documented
/// status codes with the uniform error envelope; unexpected exceptions become a 500
/// with a trace id and no internal details outside Development.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ChangeLensException ex)
        {
            logger.LogInformation(
                "Request {Method} {Path} failed with {Code}: {Message}",
                context.Request.Method, context.Request.Path, ex.Code, ex.Message);
            await WriteProblemAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);

            var detail = environment.IsDevelopment() ? ex.ToString() : "An unexpected error occurred.";
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "internal_error", detail);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string code, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = $"https://api.changelens.dev/errors/{code}",
            Title = status switch
            {
                StatusCodes.Status400BadRequest => "Validation failed",
                StatusCodes.Status401Unauthorized => "Unauthorized",
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status404NotFound => "Not found",
                StatusCodes.Status409Conflict => "Conflict",
                _ => "An error occurred"
            },
            Status = status,
            Detail = detail,
            Extensions =
            {
                ["traceId"] = context.TraceIdentifier,
                ["code"] = code
            }
        };

        await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
    }
}
