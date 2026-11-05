namespace UrlShortener.Api.Services;

/// <summary>
/// Hashes visitor IP addresses with a cryptographic salt for GDPR/CCPA privacy compliance.
/// </summary>
public interface IIpPrivacyHasher
{
    /// <summary>
    /// Computes a salted SHA-256 hash of the client IP address. Raw IPs are never stored.
    /// </summary>
    /// <param name="ipAddress">Client IP string.</param>
    /// <returns>Hex-encoded SHA-256 hash.</returns>
    string HashIp(string? ipAddress);
}
