using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Endpoints;
using UrlShortener.Api.Infrastructure.Data;

namespace UrlShortener.ContractTests;

/// <summary>
/// OpenAPI contract tests for Greenfield MVP endpoints (specs/tasks.md T015).
/// Verifies HTTP status codes, headers, and payload schemas against specs/spec.md.
/// </summary>
public class GreenfieldContractTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string TestConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=UrlShortener_ContractTests;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public GreenfieldContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = TestConnectionString
                });
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task PostUrls_ValidDestination_Returns201Created_WithLocationHeader_AndSchema()
    {
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/retirement-planning"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Verify Location header is populated per OpenAPI contract
        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location.ToString();
        Assert.StartsWith("/api/v1/urls/", location);

        // Verify response body matches ShortUrlResponse contract
        var body = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(body);
        Assert.Equal(7, body.ShortCode.Length);
        Assert.Equal(request.OriginalUrl, body.OriginalUrl);
        Assert.True(body.IsActive);
        Assert.EndsWith(body.ShortCode, body.ShortUrl);
    }

    [Fact]
    public async Task PostUrls_WithCustomSlug_Returns201Created_AndCustomShortCode()
    {
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/tax-strategies-2026",
            CustomSlug = "tax-plan-2026"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(body);
        Assert.Equal("tax-plan-2026", body.ShortCode);
        Assert.Equal("/api/v1/urls/tax-plan-2026", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task PostUrls_DuplicateCustomSlug_Returns409Conflict_WithProblemDetails()
    {
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/duplicate-test",
            CustomSlug = "unique-slug-conflict"
        };

        // First creation succeeds
        var firstResponse = await _client.PostAsJsonAsync("/api/v1/urls", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // Second creation with the exact same custom slug returns 409 Conflict
        var secondResponse = await _client.PostAsJsonAsync("/api/v1/urls", request);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        // Verify RFC 7807 ProblemDetails
        var problemDetails = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(409, problemDetails.Status);
        Assert.Equal("Conflict", problemDetails.Title);
        Assert.Contains("unique-slug-conflict", problemDetails.Detail);
    }

    [Fact]
    public async Task PostUrls_InvalidUrl_Returns400BadRequest_WithProblemDetails()
    {
        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "not-a-valid-url"
        };

        var response = await _client.PostAsJsonAsync("/api/v1/urls", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("Bad Request", problemDetails.Title);
    }

    [Fact]
    public async Task GetRedirect_ExistingShortCode_Returns302Found_WithNoStoreCacheControl_AndLocationHeader()
    {
        var createRequest = new CreateShortUrlRequest
        {
            OriginalUrl = "https://93.184.216.34/destination-redirect"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", createRequest);
        var createdBody = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(createdBody);

        // Execute GET /{shortCode}
        var redirectResponse = await _client.GetAsync($"/{createdBody.ShortCode}");

        // Verify 302 Found temporary redirect
        Assert.Equal(HttpStatusCode.Found, redirectResponse.StatusCode);

        // Verify Target Destination URL in Location header
        Assert.NotNull(redirectResponse.Headers.Location);
        Assert.Equal(createRequest.OriginalUrl, redirectResponse.Headers.Location.ToString());

        // Verify Cache-Control header contains no-store
        Assert.NotNull(redirectResponse.Headers.CacheControl);
        Assert.True(redirectResponse.Headers.CacheControl.NoStore);
        Assert.True(redirectResponse.Headers.CacheControl.NoCache);
    }

    [Fact]
    public async Task GetRedirect_NonExistentShortCode_Returns404NotFound_WithProblemDetails()
    {
        var response = await _client.GetAsync("/nonexistent999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(404, problemDetails.Status);
        Assert.Equal("Not Found", problemDetails.Title);
    }

    [Fact]
    public async Task GetHealth_Returns200Ok_WithHealthyStatusPayload()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HealthStatusResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }
}
