using System.Net;

namespace UrlShortener.Api.Services;

/// <summary>
/// Represents the result of a URL security validation, including the resolved IP 
/// to prevent Time-of-Check to Time-of-Use (TOCTOU) DNS Rebinding attacks.
/// </summary>
public sealed class UrlValidationResult
{
    public bool IsValid { get; }
    public string? ErrorMessage { get; }
    
    /// <summary>
    /// Captured safe IP address to be used by downstream HTTP clients (anti-TOCTOU).
    /// </summary>
    public IPAddress? ResolvedSafeIp { get; } 

    private UrlValidationResult(bool isValid, string? errorMessage, IPAddress? resolvedSafeIp)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        ResolvedSafeIp = resolvedSafeIp;
    }

    public static UrlValidationResult Success(IPAddress? resolvedIp = null) => new(true, null, resolvedIp);
    public static UrlValidationResult Failure(string errorMessage) => new(false, errorMessage, null);
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
