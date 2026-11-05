namespace UrlShortener.Api.Domain.Entities;

/// <summary>
/// Records an individual redirection click event with privacy-hashed IP and user agent.
/// </summary>
public class ClickEvent
{
    public long Id { get; set; }

    /// <summary>
    /// Correlated short code. Indexed in DbContext for aggregate queries.
    /// </summary>
    public string ShortCode { get; set; } = string.Empty;

    /// <summary>
    /// Salted SHA-256 hash of client IP address for privacy-safe unique visitor counts.
    /// Never stores raw IP addresses (GDPR / CCPA privacy safeguard).
    /// </summary>
    public string HashedIp { get; set; } = string.Empty;

    /// <summary>
    /// Client browser User-Agent header value (truncated to 512 chars).
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// UTC timestamp of the redirect click.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Foreign key to parent ShortUrl entity.
    /// </summary>
    public long ShortUrlId { get; set; }

    /// <summary>
    /// Navigation property to parent ShortUrl.
    /// </summary>
    public ShortUrl? ShortUrl { get; set; }
}
