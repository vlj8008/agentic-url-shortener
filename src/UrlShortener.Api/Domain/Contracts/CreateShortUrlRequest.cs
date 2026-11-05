namespace UrlShortener.Api.Domain.Contracts;

/// <summary>
/// Inbound contract for creating a shortened URL (OWASP API3 decoupled from database entity).
/// </summary>
public sealed record CreateShortUrlRequest
{
    /// <summary>
    /// Absolute HTTPS destination URL to be shortened (max 2,048 chars).
    /// </summary>
    public string OriginalUrl { get; init; } = string.Empty;

    /// <summary>
    /// Optional custom alphanumeric/hyphen/underscore slug (4-32 characters).
    /// If null, a cryptographically random 7-character Base62 code is generated.
    /// </summary>
    public string? CustomSlug { get; init; }

    /// <summary>
    /// Optional UTC expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
