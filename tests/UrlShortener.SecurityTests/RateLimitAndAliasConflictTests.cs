using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Domain.Entities;
using UrlShortener.Api.Infrastructure.Data;

namespace UrlShortener.SecurityTests;

/// <summary>
/// Isolated test host fixture for rate-limiting, custom alias conflict, and link expiration security tests (T024).
/// </summary>
public class RateLimitTestWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ShortenerDomain = "shortener.enterprise-finance.example.com";
    private readonly string _testDbName = $"RateLimitTestsDb_{Guid.NewGuid():N}";

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
/// Security test suite enforcing HTTP perimeter protections for rate limiting (429),
/// custom alias conflict (409), reserved word / invalid slug validation (400), and link expiration (410) (specs/tasks.md T024).
/// </summary>
public class RateLimitAndAliasConflictTests : IClassFixture<RateLimitTestWebApplicationFactory>
{
    private readonly RateLimitTestWebApplicationFactory _factory;
    private static int _ipCounter = 100;

    public RateLimitAndAliasConflictTests(RateLimitTestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithIp(string ip)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private static string GetNextUniqueIp() => $"198.51.100.{Interlocked.Increment(ref _ipCounter)}";

    // =========================================================================
    // 1. Custom Alias Conflict Tests (409 Conflict)
    // =========================================================================

    [Fact]
    public async Task PostUrls_DuplicateCustomAlias_Returns409ConflictWithProblemDetails()
    {
        var client = CreateClientWithIp(GetNextUniqueIp());
        var customSlug = $"promo-{Guid.NewGuid():N}"[..12];

        var request1 = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/initial-promo",
            CustomSlug = customSlug
        };

        var response1 = await client.PostAsJsonAsync("/api/v1/urls", request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Attempt to create second link with identical custom alias from a different IP
        var client2 = CreateClientWithIp(GetNextUniqueIp());
        var request2 = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/competing-promo",
            CustomSlug = customSlug
        };

        var response2 = await client2.PostAsJsonAsync("/api/v1/urls", request2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        var problem = await response2.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Contains("already taken", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 2. Reserved Words Tests (400 Bad Request)
    // =========================================================================

    [Theory]
    [InlineData("api")]
    [InlineData("API")]
    [InlineData("health")]
    [InlineData("Admin")]
    [InlineData("swagger")]
    [InlineData("METRICS")]
    public async Task PostUrls_ReservedWords_Returns400BadRequestWithProblemDetails(string reservedWord)
    {
        var client = CreateClientWithIp(GetNextUniqueIp());
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/target",
            CustomSlug = reservedWord
        };

        var response = await client.PostAsJsonAsync("/api/v1/urls", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Contains("reserved", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 3. Custom Alias Regex Boundary Cases (400 Bad Request)
    // =========================================================================

    [Theory]
    [InlineData("abc")]                                                             // Too short (< 4 chars)
    [InlineData("this-slug-is-way-too-long-and-exceeds-the-maximum-thirty-two-char")] // Too long (> 32 chars)
    [InlineData("bad alias")]                                                       // Space
    [InlineData("bad/alias")]                                                       // Path traversal slash
    [InlineData("bad@alias!")]                                                      // Special characters
    public async Task PostUrls_InvalidCustomSlugRegex_Returns400BadRequestWithProblemDetails(string invalidSlug)
    {
        var client = CreateClientWithIp(GetNextUniqueIp());
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/target",
            CustomSlug = invalidSlug
        };

        var response = await client.PostAsJsonAsync("/api/v1/urls", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.NotNull(problem.Detail);
    }

    // =========================================================================
    // 4. Past Expiration Date Tests (400 Bad Request)
    // =========================================================================

    [Fact]
    public async Task PostUrls_PastExpirationDate_Returns400BadRequestWithProblemDetails()
    {
        var client = CreateClientWithIp(GetNextUniqueIp());
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/target",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        var response = await client.PostAsJsonAsync("/api/v1/urls", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Contains("future", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 5. Expired Link Redirection (410 Gone)
    // =========================================================================

    [Fact]
    public async Task GetUrl_ExpiredLink_Returns410GoneWithProblemDetails()
    {
        var shortCode = $"exp-{Guid.NewGuid():N}"[..10];

        // Deterministic seeding directly via DbContext
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
            dbContext.ShortUrls.Add(new ShortUrl
            {
                ShortCode = shortCode,
                OriginalUrl = "https://93.184.216.34/expired-landing",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }

        var client = CreateClientWithIp(GetNextUniqueIp());
        var response = await client.GetAsync($"/{shortCode}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(410, problem.Status);
        Assert.Equal("Gone", problem.Title);
        Assert.Contains("expired", problem.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // 6. Creation Rate Limiting (create-url-policy: 10 req/min per IP -> 429)
    // =========================================================================

    [Fact]
    public async Task PostUrls_CreationRateLimitExceeded_Returns429WithRetryAfterHeader_AndAllowsOtherIps()
    {
        var throttledIp = GetNextUniqueIp();
        var clientThrottled = CreateClientWithIp(throttledIp);

        // Exhaust the 10-request permit limit
        for (var i = 1; i <= 10; i++)
        {
            var request = new CreateShortUrlRequest
            {
                OriginalUrl = $"https://93.184.216.34/item-{i}"
            };
            var res = await clientThrottled.PostAsJsonAsync("/api/v1/urls", request);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        // The 11th request from the same IP must be rejected with 429
        var overLimitRequest = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/item-11"
        };
        var response429 = await clientThrottled.PostAsJsonAsync("/api/v1/urls", overLimitRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, response429.StatusCode);

        // Verify Retry-After header
        Assert.True(response429.Headers.Contains("Retry-After"), "Missing Retry-After response header");
        var retryAfter = response429.Headers.GetValues("Retry-After");
        Assert.Contains("60", retryAfter);

        // Verify RFC 7807 ProblemDetails body
        var problem = await response429.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.Equal("Too Many Requests", problem.Title);

        // Verify a second, different IP is NOT throttled (Partition Independence)
        var secondIp = GetNextUniqueIp();
        var clientSecond = CreateClientWithIp(secondIp);
        var secondResponse = await clientSecond.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/fresh-ip-item"
        });
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    // =========================================================================
    // 7. Redirection Rate Limiting (redirect-url-policy: 100 req/min per IP -> 429)
    // =========================================================================

    [Fact]
    public async Task GetUrl_RedirectionRateLimitExceeded_Returns429WithRetryAfterHeader_AndAllowsOtherIps()
    {
        var shortCode = $"red-{Guid.NewGuid():N}"[..10];

        // Seed destination short link
        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
            dbContext.ShortUrls.Add(new ShortUrl
            {
                ShortCode = shortCode,
                OriginalUrl = "https://93.184.216.34/redirect-destination",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            });
            await dbContext.SaveChangesAsync();
        }

        var throttledIp = GetNextUniqueIp();
        var clientThrottled = CreateClientWithIp(throttledIp);

        // Exhaust the 100-request permit limit
        for (var i = 1; i <= 100; i++)
        {
            var res = await clientThrottled.GetAsync($"/{shortCode}");
            Assert.Equal(HttpStatusCode.Found, res.StatusCode);
        }

        // The 101st request from the same IP must be rejected with 429
        var response429 = await clientThrottled.GetAsync($"/{shortCode}");
        Assert.Equal(HttpStatusCode.TooManyRequests, response429.StatusCode);

        // Verify Retry-After header
        Assert.True(response429.Headers.Contains("Retry-After"), "Missing Retry-After response header");
        var retryAfter = response429.Headers.GetValues("Retry-After");
        Assert.Contains("60", retryAfter);

        // Verify RFC 7807 ProblemDetails body
        var problem = await response429.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.Equal("Too Many Requests", problem.Title);

        // Verify a second, different IP is NOT throttled (Partition Independence)
        var secondIp = GetNextUniqueIp();
        var clientSecond = CreateClientWithIp(secondIp);
        var secondResponse = await clientSecond.GetAsync($"/{shortCode}");
        Assert.Equal(HttpStatusCode.Found, secondResponse.StatusCode);
    }
}
