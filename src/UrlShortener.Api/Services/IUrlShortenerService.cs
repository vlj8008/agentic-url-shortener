using UrlShortener.Api.Domain.Contracts;

namespace UrlShortener.Api.Services;

/// <summary>
/// Core application service abstraction coordinating link shortening, cached redirection, metadata, and revocation.
/// </summary>
public interface IUrlShortenerService
{
    /// <summary>
    /// Creates a shortened URL from a validated destination URL or custom slug.
    /// </summary>
    Task<ShortUrlResponse> CreateAsync(CreateShortUrlRequest request, string baseUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the destination URL for a short code, verifying active status and queueing click telemetry.
    /// </summary>
    Task<string?> ResolveDestinationAsync(string shortCode, string? clientIp, string? userAgent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata and aggregated analytics for an existing short code.
    /// </summary>
    Task<UrlMetadataResponse?> GetMetadataAsync(string shortCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an existing short link (soft delete). Subsequent resolution returns 410 Gone.
    /// </summary>
    Task<bool> RevokeAsync(string shortCode, CancellationToken cancellationToken = default);
}
