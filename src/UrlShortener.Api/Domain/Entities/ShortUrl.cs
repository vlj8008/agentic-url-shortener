namespace UrlShortener.Api.Domain.Entities;

/// <summary>
/// Represents a shortened URL mapping with metadata and soft-delete/revocation status.
/// </summary>
public class ShortUrl
{
    public long Id { get; set; }

    /// <summary>
    /// Unique 7-character Base62 or custom slug identifier.
    /// Indexed uniquely in DbContext.
    /// </summary>
    public string ShortCode { get; set; } = string.Empty;

    /// <summary>
    /// Validated HTTPS destination URL (max 2,048 chars).
    /// </summary>
    public string OriginalUrl { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of link creation.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional UTC timestamp after which the link returns 410 Gone.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Indicates whether the link is currently active. Revoked links set this to false.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Concurrency token for optimistic locking on updates/revocations.
    /// </summary>
    public byte[] ConcurrencyToken { get; set; } = [];

    /// <summary>
    /// Navigation property to recorded click analytics.
    /// </summary>
    public ICollection<ClickEvent> ClickEvents { get; set; } = new List<ClickEvent>();
}
