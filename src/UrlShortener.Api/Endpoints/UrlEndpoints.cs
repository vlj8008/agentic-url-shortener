using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Services;

namespace UrlShortener.Api.Endpoints;

/// <summary>
/// Greenfield Minimal API endpoints for URL shortening, cached redirection, and health checks (specs/tasks.md T013).
/// </summary>
public static class UrlEndpoints
{
    public static IEndpointRouteBuilder MapUrlEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/urls - Create shortened link
        app.MapPost("/api/v1/urls", async (
            [FromBody] CreateShortUrlRequest request,
            HttpContext httpContext,
            IUrlShortenerService shortenerService,
            CancellationToken cancellationToken) =>
        {
            var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            var response = await shortenerService.CreateAsync(request, baseUrl, cancellationToken);
            return Results.Created($"/api/v1/urls/{response.ShortCode}", response);
        })
        .WithName("createShortUrl")
        .WithSummary("Create a shortened URL")
        .WithDescription("Validates destination URL, checks anti-SSRF policies, and generates a Base62/Base58 7-char short link or claims custom alias.")
        .Produces<ShortUrlResponse>(StatusCodes.Status201Created)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
        .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        // GET /{shortCode} - Sub-50ms cached redirection
        app.MapGet("/{shortCode}", async (
            string shortCode,
            HttpContext httpContext,
            IUrlShortenerService shortenerService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(shortCode))
            {
                return Results.BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Bad Request",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Detail = "Short code cannot be empty.",
                    Instance = httpContext.Request.Path
                });
            }

            var clientIp = httpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = httpContext.Request.Headers.UserAgent.ToString();

            var destinationUrl = await shortenerService.ResolveDestinationAsync(shortCode, clientIp, userAgent, cancellationToken);
            if (destinationUrl is null)
            {
                return Results.NotFound(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Not Found",
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    Detail = $"Short code '{shortCode}' does not exist.",
                    Instance = httpContext.Request.Path
                });
            }

            httpContext.Response.Headers.CacheControl = "no-store, no-cache";
            return Results.Redirect(destinationUrl, permanent: false);
        })
        .WithName("redirectToDestination")
        .WithSummary("Redirect to original destination URL")
        .WithDescription("Resolves short code and returns HTTP 302 Found temporary redirect with Cache-Control no-store.")
        .Produces(StatusCodes.Status302Found)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status410Gone);

        // GET /health - Service liveness/readiness probe
        app.MapGet("/health", () => Results.Ok(new HealthStatusResponse("Healthy")))
        .WithName("getHealth")
        .WithSummary("Service health check")
        .WithDescription("Liveness and readiness probe for cloud and container orchestration.")
        .Produces<HealthStatusResponse>(StatusCodes.Status200OK);

        return app;
    }
}

/// <summary>
/// Health check probe response payload.
/// </summary>
public sealed record HealthStatusResponse(string Status);
