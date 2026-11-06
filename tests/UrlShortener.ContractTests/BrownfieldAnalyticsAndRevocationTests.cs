using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Domain.Contracts;

namespace UrlShortener.ContractTests;

/// <summary>
/// OpenAPI brownfield contract tests verifying non-blocking click analytics, unique visitor counting,
/// and soft-delete link revocation (specs/tasks.md T021).
/// </summary>
public class BrownfieldAnalyticsAndRevocationTests : IClassFixture<ContractTestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public BrownfieldAnalyticsAndRevocationTests(ContractTestWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // =========================================================================
    // 1. Click & Unique Visitor Analytics Tests (T018 / T019 / T021)
    // =========================================================================

    [Fact]
    public async Task GetMetadata_Initially_ReturnsZeroClicksAndZeroUniqueVisitors()
    {
        // 1. Create a fresh shortened URL
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com/initial-analytics-test"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        // 2. Fetch metadata immediately
        var metadataResponse = await _client.GetAsync($"/api/v1/urls/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<UrlMetadataResponse>();
        Assert.NotNull(metadata);
        Assert.Equal(created.ShortCode, metadata.ShortCode);
        Assert.Equal("https://example.com/initial-analytics-test", metadata.OriginalUrl);
        Assert.Equal(0, metadata.TotalClicks);
        Assert.Equal(0, metadata.UniqueVisitors);
        Assert.True(metadata.IsActive);
    }

    [Fact]
    public async Task ResolveRedirects_MultipleVisitors_AccuratelyAggregatesClicksAndUniqueVisitors()
    {
        // 1. Create a fresh shortened URL
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com/multi-visitor-analytics"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        // 2. Simulate 5 clicks across 3 distinct visitor IPs:
        //    - 2 clicks from IP 198.51.100.10
        //    - 2 clicks from IP 198.51.100.20
        //    - 1 click from IP 198.51.100.30
        var visitorIps = new[]
        {
            "198.51.100.10",
            "198.51.100.10",
            "198.51.100.20",
            "198.51.100.20",
            "198.51.100.30"
        };

        foreach (var ip in visitorIps)
        {
            using var redirectRequest = new HttpRequestMessage(HttpMethod.Get, $"/{created.ShortCode}");
            redirectRequest.Headers.Add("X-Forwarded-For", ip);
            redirectRequest.Headers.Add("User-Agent", "TestRunner-Agentic/1.0");

            var redirectResponse = await _client.SendAsync(redirectRequest);
            Assert.Equal(HttpStatusCode.Found, redirectResponse.StatusCode);
        }

        // 3. Poll metadata until background worker persists all 5 click events
        var finalMetadata = await PollUntilConditionAsync(
            url: $"/api/v1/urls/{created.ShortCode}",
            predicate: meta => meta != null && meta.TotalClicks >= 5,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50));

        Assert.NotNull(finalMetadata);
        Assert.Equal(5, finalMetadata.TotalClicks);
        Assert.Equal(3, finalMetadata.UniqueVisitors);
        Assert.True(finalMetadata.IsActive);
    }

    [Fact]
    public async Task GetMetadata_NonExistentShortCode_ReturnsNotFoundProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/urls/missing99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.NotFound, problem.Status);
    }

    // =========================================================================
    // 2. Link Revocation (Soft-Delete) Tests (T019 / T021)
    // =========================================================================

    [Fact]
    public async Task DeleteUrl_ExistingLink_ReturnsNoContentAndSubsequentRedirectReturnsGone()
    {
        // 1. Create a shortened link
        var createResponse = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com/revocation-target"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        // 2. Confirm redirect works initially
        var preRevokeRedirect = await _client.GetAsync($"/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.Found, preRevokeRedirect.StatusCode);

        // 3. Revoke link via DELETE /api/v1/urls/{shortCode}
        var deleteResponse = await _client.DeleteAsync($"/api/v1/urls/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // 4. Subsequent redirect immediately returns 410 Gone (RFC 7807 ProblemDetails)
        var postRevokeRedirect = await _client.GetAsync($"/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.Gone, postRevokeRedirect.StatusCode);

        var goneProblem = await postRevokeRedirect.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(goneProblem);
        Assert.Equal((int)HttpStatusCode.Gone, goneProblem.Status);

        // 5. Metadata query indicates IsActive = false
        var metadataResponse = await _client.GetAsync($"/api/v1/urls/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.OK, metadataResponse.StatusCode);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<UrlMetadataResponse>();
        Assert.NotNull(metadata);
        Assert.False(metadata.IsActive);
    }

    [Fact]
    public async Task DeleteUrl_NonExistentShortCode_ReturnsNotFoundProblemDetails()
    {
        var deleteResponse = await _client.DeleteAsync("/api/v1/urls/nonexistentcode");

        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        var problem = await deleteResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)HttpStatusCode.NotFound, problem.Status);
    }

    // =========================================================================
    // 3. Greenfield US1 Zero-Regression Verification
    // =========================================================================

    [Fact]
    public async Task GreenfieldRegression_CreateAndRedirect_ReturnsExpectedHeaders()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/urls", new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com/regression-safe"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        var redirect = await _client.GetAsync($"/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("https://example.com/regression-safe", redirect.Headers.Location?.ToString());
        Assert.True(redirect.Headers.CacheControl?.NoStore);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Resilient polling helper that checks metadata until asynchronous background persistence completes.
    /// Eliminates flakiness under CPU contention without artificial thread-sleeps.
    /// </summary>
    private async Task<UrlMetadataResponse?> PollUntilConditionAsync(
        string url,
        Func<UrlMetadataResponse?, bool> predicate,
        TimeSpan timeout,
        TimeSpan pollInterval)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.Token.IsCancellationRequested)
        {
            var response = await _client.GetAsync(url, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var metadata = await response.Content.ReadFromJsonAsync<UrlMetadataResponse>(cancellationToken: cts.Token);
                if (predicate(metadata))
                {
                    return metadata;
                }
            }

            try
            {
                await Task.Delay(pollInterval, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Final attempt to fetch and return whatever metadata is present
        var finalResponse = await _client.GetAsync(url);
        return finalResponse.IsSuccessStatusCode
            ? await finalResponse.Content.ReadFromJsonAsync<UrlMetadataResponse>()
            : null;
    }
}
