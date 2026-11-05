namespace UrlShortener.Api.Domain.Contracts;

/// <summary>
/// Outbound response contract returned upon successful creation of a shortened link.
/// </summary>
public sealed record ShortUrlResponse
{
    /// <summary>
    /// The unique short code identifier (e.g., aB3x9Q1).
    /// </summary>
    public string ShortCode { get; init; } = string.Empty;

    /// <summary>
    /// Fully qualified short URL (e.g., https://go.enterprise-finance.example.com/aB3x9Q1).
    /// </summary>
    public string ShortUrl { get; init; } = string.Empty;

    /// <summary>
    /// Original validated destination URL.
    /// </summary>
    public string OriginalUrl { get; init; } = string.Empty;

    /// <summary>
    /// UTC timestamp of link creation.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Optional UTC timestamp when the link expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Indicates whether the link is currently active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
