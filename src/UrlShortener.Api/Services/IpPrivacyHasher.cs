using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.Api.Services;

/// <summary>
/// Hashes visitor IP addresses with a cryptographic salt using SHA-256 for GDPR/CCPA privacy compliance (specs/tasks.md T017).
/// Prevents rainbow-table inversion and ensures raw client IP addresses are never persisted in plain text.
/// </summary>
public sealed class IpPrivacyHasher : IIpPrivacyHasher
{
    private const string DefaultSalt = "agentic-url-shortener-privacy-salt-2026";
    private readonly string _salt;

    public IpPrivacyHasher(IConfiguration configuration)
    {
        _salt = configuration["Privacy:IpHashSalt"] ?? DefaultSalt;
        if (string.IsNullOrWhiteSpace(_salt))
        {
            _salt = DefaultSalt;
        }
    }

    /// <summary>
    /// Computes a salted SHA-256 lowercase hex digest of the normalized client IP address.
    /// Handles null, empty, IPv4-mapped IPv6, and standard addresses consistently.
    /// </summary>
    /// <param name="ipAddress">Client IP address string.</param>
    /// <returns>64-character lowercase hexadecimal SHA-256 hash.</returns>
    public string HashIp(string? ipAddress)
    {
        var normalizedIp = NormalizeIp(ipAddress);
        var payload = $"{_salt}:{normalizedIp}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hashBytes);
    }

    private static string NormalizeIp(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return "unknown";
        }

        var trimmed = ipAddress.Trim();

        if (IPAddress.TryParse(trimmed, out var parsedIp))
        {
            if (parsedIp.IsIPv4MappedToIPv6)
            {
                parsedIp = parsedIp.MapToIPv4();
            }

            return parsedIp.ToString().ToLowerInvariant();
        }

        return trimmed.ToLowerInvariant();
    }
}
