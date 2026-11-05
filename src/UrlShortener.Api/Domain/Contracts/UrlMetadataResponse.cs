namespace UrlShortener.Api.Domain.Contracts;

/// <summary>
/// Outbound metadata and analytics response contract for an existing shortened link.
/// </summary>
public sealed record UrlMetadataResponse
{
    /// <summary>
    /// The unique short code identifier (e.g., aB3x9Q1).
    /// </summary>
    public string ShortCode { get; init; } = string.Empty;

    /// <summary>
    /// Original validated destination URL.
    /// </summary>
    public string OriginalUrl { get; init; } = string.Empty;

    /// <summary>
    /// Total number of redirects executed for this link.
    /// </summary>
    public long TotalClicks { get; init; }

    /// <summary>
    /// Approximate count of distinct visitors based on privacy-safe hashed IPs.
    /// </summary>
    public long UniqueVisitors { get; init; }

    /// <summary>
    /// UTC timestamp of link creation.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional UTC timestamp when the link expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Indicates whether the link is active. If false (revoked), redirects return 410 Gone.
    /// </summary>
    public bool IsActive { get; init; }
}
