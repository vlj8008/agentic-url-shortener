using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace UrlShortener.Api.Services;

/// <summary>
/// Enforces destination URL validation and Anti-SSRF security policies (specs/tasks.md T011).
/// Blocks private IPv4/IPv6 networks, cloud metadata endpoints, loopback addresses, and self-loops.
/// </summary>
public sealed class UrlSecurityValidator : IUrlSecurityValidator
{
    private const int MaxUrlLength = 2048;
    private const int DefaultDnsTimeoutSeconds = 3;

    private readonly IHostEnvironment _environment;
    private readonly string? _configuredShortenerDomain;
    private readonly TimeSpan _dnsTimeout;

    public UrlSecurityValidator(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuredShortenerDomain = configuration["Shortener:Domain"] ?? configuration["Shortener:BaseUrl"];

        var configuredTimeoutSeconds = int.TryParse(configuration["Security:DnsTimeoutSeconds"], out var parsedTimeout)
            ? parsedTimeout
            : DefaultDnsTimeoutSeconds;

        _dnsTimeout = TimeSpan.FromSeconds(configuredTimeoutSeconds);
    }

    /// <summary>
    /// Asynchronously validates destination URL against scheme, length, Anti-SSRF, and loopback rules.
    /// </summary>
    public async Task<UrlValidationResult> ValidateAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return UrlValidationResult.Failure("Destination URL cannot be empty.");
        }

        if (url.Length > MaxUrlLength)
        {
            return UrlValidationResult.Failure($"Destination URL exceeds maximum allowed length of {MaxUrlLength} characters.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return UrlValidationResult.Failure("Destination URL is not a valid absolute URI.");
        }

        // Scheme validation: HTTPS required in production; HTTP allowed only in Development
        var isDevelopment = _environment.IsDevelopment();
        var isAllowedScheme = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                              (isDevelopment && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

        if (!isAllowedScheme)
        {
            return UrlValidationResult.Failure(isDevelopment
                ? $"Scheme '{uri.Scheme}' is not allowed. Only HTTP and HTTPS are permitted in development."
                : $"Scheme '{uri.Scheme}' is not allowed. Only HTTPS is permitted.");
        }

        // Self-loop prevention: block redirection loops back to the shortener itself
        if (!string.IsNullOrEmpty(_configuredShortenerDomain))
        {
            var configuredHost = Uri.TryCreate(_configuredShortenerDomain, UriKind.Absolute, out var baseUri)
                ? baseUri.Host
                : _configuredShortenerDomain;

            if (string.Equals(uri.Host, configuredHost, StringComparison.OrdinalIgnoreCase))
            {
                return UrlValidationResult.Failure("Self-referencing destination URLs are not permitted.");
            }
        }

        // Hostname check: block localhost addresses
        if (string.Equals(uri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return UrlValidationResult.Failure("Localhost destination addresses are blocked by Anti-SSRF security policy.");
        }

        // IP Address & DNS Resolution Anti-SSRF filtering
        try
        {
            if (IPAddress.TryParse(uri.DnsSafeHost, out var directIp))
            {
                if (IsRestrictedIp(directIp))
                {
                    return UrlValidationResult.Failure("Destination URL specifies a restricted IP address.");
                }

                // Return safe direct IP to prevent downstream DNS re-resolution
                return UrlValidationResult.Success(directIp);
            }

            // Enforce bounded timeout on DNS lookups to protect against slow-DNS / Asymmetric DoS attacks
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_dnsTimeout);

            var resolvedAddresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, timeoutCts.Token);

            if (resolvedAddresses.Length == 0)
            {
                return UrlValidationResult.Failure($"Destination host '{uri.DnsSafeHost}' could not be resolved.");
            }

            foreach (var ip in resolvedAddresses)
            {
                if (IsRestrictedIp(ip))
                {
                    return UrlValidationResult.Failure($"Destination host '{uri.DnsSafeHost}' resolves to a restricted IP address.");
                }
            }

            // Capture first validated safe IP to prevent TOCTOU DNS Rebinding attacks
            return UrlValidationResult.Success(resolvedAddresses[0]);
        }
        catch (OperationCanceledException)
        {
            return UrlValidationResult.Failure($"DNS resolution for '{uri.DnsSafeHost}' timed out.");
        }
        catch (SocketException ex)
        {
            return UrlValidationResult.Failure($"Unable to resolve destination host '{uri.DnsSafeHost}': {ex.Message}");
        }
    }

    /// <summary>
    /// Checks whether an IP address belongs to loopback, private RFC 1918, link-local, or cloud metadata ranges.
    /// Evaluated using C# list pattern matching for concise, performant byte inspection.
    /// </summary>
    private static bool IsRestrictedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return ip.GetAddressBytes() switch
            {
                [0, ..] => true,                                     // 0.0.0.0/8 (Current network)
                [10, ..] => true,                                    // RFC 1918: 10.0.0.0/8
                [127, ..] => true,                                   // Loopback: 127.0.0.0/8
                [169, 254, ..] => true,                              // Link-Local & Cloud Metadata (169.254.169.254)
                [172, var b, ..] when b is >= 16 and <= 31 => true,  // RFC 1918: 172.16.0.0/12
                [192, 168, ..] => true,                              // RFC 1918: 192.168.0.0/16
                [var b, ..] when b >= 224 => true,                   // Multicast & Reserved (224.0.0.0/4 and 240.0.0.0/4)
                _ => false
            };
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
            {
                return true;
            }

            return ip.GetAddressBytes() switch
            {
                [var b, ..] when (b & 0xfe) == 0xfc => true,         // Unique Local Address: fc00::/7 (RFC 4193)
                _ => false
            };
        }

        return false;
    }
}
