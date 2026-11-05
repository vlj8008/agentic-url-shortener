using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Services;

namespace UrlShortener.SecurityTests;

/// <summary>
/// Isolated test host fixture for SSRF and scheme security tests.
/// Boots the API once in the "Testing" environment using an in-memory database.
/// </summary>
public class SecurityTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ShortenerDomain = "shortener.enterprise-finance.example.com";
    private readonly string _testDbName = $"SecurityTestsDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shortener:Domain"] = ShortenerDomain,
                ["Shortener:BaseUrl"] = $"https://{ShortenerDomain}",
                ["TestDatabaseName"] = _testDbName
            });
        });
    }
}

/// <summary>
/// Security test suite enforcing 100% rejection of Anti-SSRF and dangerous scheme vectors (specs/tasks.md T016 and SC-005).
/// </summary>
public class SsrfAndSchemeSecurityTests : IClassFixture<SecurityTestWebApplicationFactory>, IDisposable
{
    private const string ShortenerDomain = SecurityTestWebApplicationFactory.ShortenerDomain;
    private readonly HttpClient _apiClient;

    public SsrfAndSchemeSecurityTests(SecurityTestWebApplicationFactory factory)
    {
        _apiClient = factory.CreateClient();
    }

    // =========================================================================
    // 1. Loopback Evasion Vectors (IPv4 & IPv6)
    // =========================================================================

    [Theory]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("https://127.0.0.1:8443/internal")]
    [InlineData("http://127.0.0.2")]
    [InlineData("http://127.255.255.254")]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://localhost:5001")]
    [InlineData("http://[::1]")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://[0000:0000:0000:0000:0000:0000:0000:0001]")]
    public async Task ValidateAsync_LoopbackAddresses_AreBlocked(string loopbackUrl)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(loopbackUrl);

        Assert.False(result.IsValid, $"Loopback target '{loopbackUrl}' was not rejected!");
    }

    // =========================================================================
    // 2. RFC 1918 Private IP Subnets
    // =========================================================================

    [Theory]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://10.255.255.254")]
    [InlineData("http://10.50.20.1:8080")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.24.1.1")]
    [InlineData("http://172.31.255.254")]
    [InlineData("http://192.168.0.1")]
    [InlineData("http://192.168.1.1")]
    [InlineData("http://192.168.254.254")]
    public async Task ValidateAsync_Rfc1918PrivateNetworks_AreBlocked(string privateUrl)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(privateUrl);

        Assert.False(result.IsValid, $"RFC 1918 target '{privateUrl}' was not rejected!");
    }

    // =========================================================================
    // 3. Cloud Instance Metadata Service (IMDS) & Link-Local
    // =========================================================================

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://169.254.169.254/computeMetadata/v1/")]
    [InlineData("http://169.254.1.1")]
    [InlineData("http://169.254.254.254")]
    [InlineData("http://[fe80::1]")]
    [InlineData("http://[fc00::1]")]
    [InlineData("http://[fd12:3456:789a::1]")]
    public async Task ValidateAsync_CloudMetadataAndLinkLocal_AreBlocked(string metadataUrl)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(metadataUrl);

        Assert.False(result.IsValid, $"Cloud metadata/link-local target '{metadataUrl}' was not rejected!");
    }

    // =========================================================================
    // 4. Current Network, Multicast & Reserved IP Ranges
    // =========================================================================

    [Theory]
    [InlineData("http://0.0.0.0")]
    [InlineData("http://0.0.0.1")]
    [InlineData("http://224.0.0.1")]
    [InlineData("http://239.255.255.250")]
    [InlineData("http://240.0.0.1")]
    [InlineData("http://255.255.255.255")]
    public async Task ValidateAsync_CurrentAndReservedNetworks_AreBlocked(string reservedUrl)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(reservedUrl);

        Assert.False(result.IsValid, $"Reserved target '{reservedUrl}' was not rejected!");
    }

    // =========================================================================
    // 5. Dangerous URI Schemes & Injection Vectors
    // =========================================================================

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("javascript://%0aalert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("ftp://internal.vault/secret.key")]
    [InlineData("gopher://127.0.0.1:6379/_INFO")]
    [InlineData("dict://127.0.0.1:11211/stats")]
    [InlineData("ldap://127.0.0.1:389/dc=example,dc=com")]
    [InlineData("mailto:admin@internal.vault")]
    public async Task ValidateAsync_DangerousSchemes_AreBlocked(string dangerousScheme)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(dangerousScheme);

        Assert.False(result.IsValid, $"Dangerous scheme '{dangerousScheme}' was not rejected!");
    }

    // =========================================================================
    // 6. Self-Loop Redirection Evasion
    // =========================================================================

    [Theory]
    [InlineData("https://shortener.enterprise-finance.example.com")]
    [InlineData("https://shortener.enterprise-finance.example.com/api/v1/urls")]
    [InlineData("https://shortener.enterprise-finance.example.com/aB3x9Q1")]
    public async Task ValidateAsync_SelfReferencingDomain_IsBlocked(string selfLoopUrl)
    {
        var validator = CreateValidator(isDevelopment: true, shortenerDomain: ShortenerDomain);

        var result = await validator.ValidateAsync(selfLoopUrl);

        Assert.False(result.IsValid);
        Assert.Contains("Self-referencing", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 7. End-to-End API Perimeter Rejection (POST /api/v1/urls -> 400 Bad Request)
    // =========================================================================

    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/vault")]
    [InlineData("http://192.168.1.1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://shortener.enterprise-finance.example.com/self")]
    public async Task PostUrls_MaliciousSsrfTarget_Returns400BadRequestWithProblemDetails(string targetUrl)
    {
        var request = new CreateShortUrlRequest { OriginalUrl = targetUrl };

        var response = await _apiClient.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Bad Request", problem.Title);
        Assert.NotNull(problem.Detail);
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private static UrlSecurityValidator CreateValidator(bool isDevelopment, string? shortenerDomain = null)
    {
        var env = new SecurityTestHostEnvironment
        {
            EnvironmentName = isDevelopment ? Environments.Development : Environments.Production
        };

        var inMemorySettings = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(shortenerDomain))
        {
            inMemorySettings["Shortener:Domain"] = shortenerDomain;
            inMemorySettings["Shortener:BaseUrl"] = $"https://{shortenerDomain}";
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new UrlSecurityValidator(env, config);
    }

    private sealed class SecurityTestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "UrlShortener.SecurityTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public void Dispose()
    {
        _apiClient.Dispose();
    }
}
