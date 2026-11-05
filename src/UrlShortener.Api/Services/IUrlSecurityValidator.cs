namespace UrlShortener.Api.Services;

/// <summary>
/// Encapsulates the outcome of destination URL security validation.
/// </summary>
public sealed record UrlValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static UrlValidationResult Success() => new(true);
    public static UrlValidationResult Failure(string message) => new(false, message);
}

/// <summary>
/// Validates destination URLs against Anti-SSRF policies, schemes, and boundary constraints.
/// </summary>
public interface IUrlSecurityValidator
{
    /// <summary>
    /// Asynchronously validates destination URL against scheme, length, Anti-SSRF, and loopback rules.
    /// </summary>
    Task<UrlValidationResult> ValidateAsync(string url, CancellationToken cancellationToken = default);
}
