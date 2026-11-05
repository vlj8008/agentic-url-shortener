using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UrlShortener.Api.Domain.Exceptions;

namespace UrlShortener.Api.Infrastructure.Middleware;

/// <summary>
/// Global exception handler enforcing RFC 7807 ProblemDetails responses with trace correlation (specs/tasks.md T007).
/// </summary>
public sealed class GlobalProblemDetailsExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalProblemDetailsExceptionHandler> _logger;

    public GlobalProblemDetailsExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalProblemDetailsExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception occurred during request processing: {Message}", exception.Message);

        var traceId = httpContext.TraceIdentifier;

        var statusCode = exception switch
        {
            BadHttpRequestException or ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status409Conflict,
            UrlGoneException => StatusCodes.Status410Gone,
            ShortCodeCollisionException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode switch
            {
                StatusCodes.Status400BadRequest => "Bad Request",
                StatusCodes.Status404NotFound => "Not Found",
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status410Gone => "Gone",
                StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
                _ => "Internal Server Error"
            },
            Type = statusCode switch
            {
                StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                StatusCodes.Status410Gone => "https://tools.ietf.org/html/rfc7231#section-6.5.9",
                StatusCodes.Status503ServiceUnavailable => "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            },
            Detail = statusCode == StatusCodes.Status500InternalServerError
                ? "An unexpected internal server error occurred."
                : exception.Message,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["traceId"] = traceId;
        httpContext.Response.StatusCode = statusCode;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }
}
