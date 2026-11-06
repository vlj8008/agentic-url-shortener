using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Domain.Entities;
using UrlShortener.Api.Domain.Exceptions;
using UrlShortener.Api.Infrastructure.Data;
using UrlShortener.Api.Services;

namespace UrlShortener.UnitTests;

/// <summary>
/// Unit tests for short code token generation, Anti-SSRF validation, and core shortening service (specs/tasks.md T014).
/// </summary>
public class Base62AndValidationTests
{
    // =========================================================================
    // 1. Short Code Generation Tests (T010)
    // =========================================================================

    [Fact]
    public void Generate_DefaultLength_ReturnsSevenCharacters()
    {
        var generator = new Base58CodeGenerator();

        var code = generator.Generate();

        Assert.NotNull(code);
        Assert.Equal(7, code.Length);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(12)]
    [InlineData(32)]
    public void Generate_CustomLength_ReturnsRequestedLength(int requestedLength)
    {
        var generator = new Base58CodeGenerator();

        var code = generator.Generate(requestedLength);

        Assert.Equal(requestedLength, code.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-7)]
    public void Generate_InvalidLength_ThrowsArgumentOutOfRangeException(int invalidLength)
    {
        var generator = new Base58CodeGenerator();

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.Generate(invalidLength));
    }

    [Fact]
    public void Generate_CharacterSet_ContainsOnlyUnambiguousBase58Characters()
    {
        const string base58Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
        var generator = new Base58CodeGenerator();

        for (var i = 0; i < 50; i++)
        {
            var code = generator.Generate(7);
            foreach (var ch in code)
            {
                Assert.Contains(ch, base58Alphabet);
                // Verify ambiguous characters are strictly excluded
                Assert.DoesNotContain(ch, "0OIl");
            }
        }
    }

    [Fact]
    public void Generate_OneThousandCodes_AllAreUnique()
    {
        var generator = new Base58CodeGenerator();
        var generatedCodes = new HashSet<string>(1000);

        for (var i = 0; i < 1000; i++)
        {
            var code = generator.Generate(7);
            Assert.True(generatedCodes.Add(code), $"Duplicate code generated: {code}");
        }
    }

    // =========================================================================
    // 2. URL Security & Boundary Validation Tests (T011)
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_EmptyOrWhitespace_ReturnsFailure(string url)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(url);

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_ExceedsMaxUrlLength_ReturnsFailure()
    {
        var validator = CreateValidator(isDevelopment: true);
        var longUrl = "https://example.com/" + new string('a', 2050);

        var result = await validator.ValidateAsync(longUrl);

        Assert.False(result.IsValid);
        Assert.Contains("maximum", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not-a-valid-url")]
    [InlineData("://missing-scheme.com")]
    public async Task ValidateAsync_MalformedUri_ReturnsFailure(string url)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(url);

        Assert.False(result.IsValid);
        Assert.Contains("not a valid absolute URI", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("ftp://ftp.example.com/file.txt")]
    public async Task ValidateAsync_DisallowedSchemes_ReturnsFailure(string url)
    {
        var validator = CreateValidator(isDevelopment: false);

        var result = await validator.ValidateAsync(url);

        Assert.False(result.IsValid);
        Assert.Contains("not allowed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_HttpSchemeInProduction_ReturnsFailure()
    {
        var validator = CreateValidator(isDevelopment: false);

        var result = await validator.ValidateAsync("http://example.com/insecure");

        Assert.False(result.IsValid);
        Assert.Contains("Only HTTPS is permitted", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_SelfLoopUrl_ReturnsFailure()
    {
        var validator = CreateValidator(isDevelopment: true, shortenerDomain: "shortener.enterprise-finance.example.com");

        var result = await validator.ValidateAsync("https://shortener.enterprise-finance.example.com/loop");

        Assert.False(result.IsValid);
        Assert.Contains("Self-referencing", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:8080/api")]
    [InlineData("http://127.0.0.1/test")]
    [InlineData("http://10.0.0.1/admin")]
    [InlineData("http://172.16.0.1/internal")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task ValidateAsync_AntiSsrfRestrictedAddresses_ReturnsFailure(string url)
    {
        var validator = CreateValidator(isDevelopment: true);

        var result = await validator.ValidateAsync(url);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("https://93.184.216.34/products/portfolio")] // Public IPv4 (example.com)
    [InlineData("https://1.1.1.1/dns-query")]                // Public IPv4 (Cloudflare DNS)
    public async Task ValidateAsync_ValidPublicHttpsUrl_ReturnsSuccess(string validUrl)
    {
        var validator = CreateValidator(isDevelopment: false);

        var result = await validator.ValidateAsync(validUrl);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Null(result.ErrorMessage);
    }

    // =========================================================================
    // 3. Core UrlShortenerService Tests (T012)
    // =========================================================================

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndReturnsShortUrlResponse()
    {
        var repository = new InMemoryTestUrlRepository();
        var generator = new Base58CodeGenerator();
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, generator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://www.enterprise-finance.example.com/retirement-planning"
        };

        var response = await service.CreateAsync(request, "https://go.example.com");

        Assert.NotNull(response);
        Assert.Equal(7, response.ShortCode.Length);
        Assert.Equal($"https://go.example.com/{response.ShortCode}", response.ShortUrl);
        Assert.Equal(request.OriginalUrl, response.OriginalUrl);
        Assert.True(response.IsActive);
        Assert.True(await repository.ExistsAsync(response.ShortCode));
    }

    [Fact]
    public async Task CreateAsync_CustomSlug_SucceedsWhenAvailable()
    {
        var repository = new InMemoryTestUrlRepository();
        var generator = new Base58CodeGenerator();
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, generator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://www.enterprise-finance.example.com/tax-guide",
            CustomSlug = "tax-guide-2026"
        };

        var response = await service.CreateAsync(request, "https://go.example.com");

        Assert.Equal("tax-guide-2026", response.ShortCode);
        Assert.True(await repository.ExistsAsync("tax-guide-2026"));
    }

    [Fact]
    public async Task CreateAsync_DuplicateCustomSlug_ThrowsInvalidOperationException()
    {
        var repository = new InMemoryTestUrlRepository();
        var generator = new Base58CodeGenerator();
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, generator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        await repository.AddAsync(new ShortUrl
        {
            ShortCode = "existing-alias",
            OriginalUrl = "https://example.com/1",
            IsActive = true
        });

        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com/2",
            CustomSlug = "existing-alias"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request, "https://go.example.com"));
        Assert.Contains("already taken", ex.Message);
    }

    [Theory]
    [InlineData("abc")] // < 4 chars
    [InlineData("this-slug-is-way-too-long-and-exceeds-the-maximum-thirty-two-character-boundary")] // > 32 chars
    public async Task CreateAsync_InvalidCustomSlugLength_ThrowsArgumentException(string invalidSlug)
    {
        var repository = new InMemoryTestUrlRepository();
        var generator = new Base58CodeGenerator();
        var validator = CreateValidator(isDevelopment: true);
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, generator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com",
            CustomSlug = invalidSlug
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request, "https://go.example.com"));
    }

    [Fact]
    public async Task CreateAsync_PastExpirationDate_ThrowsArgumentException()
    {
        var repository = new InMemoryTestUrlRepository();
        var generator = new Base58CodeGenerator();
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, generator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var request = new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5) // In the past
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(request, "https://go.example.com"));
        Assert.Contains("future", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_CollisionRetry_RecoversAndPersistsOnNextAttempt()
    {
        var repository = new InMemoryTestUrlRepository();
        // Seed first two candidates as already existing
        await repository.AddAsync(new ShortUrl { ShortCode = "CODE001", OriginalUrl = "https://prior1.com" });
        await repository.AddAsync(new ShortUrl { ShortCode = "CODE002", OriginalUrl = "https://prior2.com" });

        // Deterministic generator sequence: CODE001 (collides), CODE002 (collides), CODE003 (succeeds)
        var sequenceGenerator = new SequentialTestCodeGenerator("CODE001", "CODE002", "CODE003");
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, sequenceGenerator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var request = new CreateShortUrlRequest { OriginalUrl = "https://example.com/unique" };

        var response = await service.CreateAsync(request, "https://go.example.com");

        Assert.Equal("CODE003", response.ShortCode);
        Assert.True(await repository.ExistsAsync("CODE003"));
    }

    [Fact]
    public async Task CreateAsync_CollisionExhaustionAfterThreeAttempts_ThrowsShortCodeCollisionException()
    {
        var repository = new InMemoryTestUrlRepository();
        await repository.AddAsync(new ShortUrl { ShortCode = "COLLIDE", OriginalUrl = "https://prior.com" });

        // Deterministic generator that always returns a colliding code
        var sequenceGenerator = new SequentialTestCodeGenerator("COLLIDE", "COLLIDE", "COLLIDE");
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, sequenceGenerator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var request = new CreateShortUrlRequest { OriginalUrl = "https://example.com/exhaustion" };

        await Assert.ThrowsAsync<ShortCodeCollisionException>(() => service.CreateAsync(request, "https://go.example.com"));
    }

    [Fact]
    public async Task ResolveDestinationAsync_HotCacheHit_ReturnsDestinationWithoutHittingRepository()
    {
        var repository = new InMemoryTestUrlRepository();
        var generator = new Base58CodeGenerator();
        var validator = new AlwaysValidSecurityValidator();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, generator, validator, cache, NullLogger<UrlShortenerService>.Instance);

        var created = await service.CreateAsync(new CreateShortUrlRequest
        {
            OriginalUrl = "https://www.enterprise-finance.example.com/fast-redirect"
        }, "https://go.example.com");

        // Clear the repository to prove the resolution comes directly from memory cache
        repository.Clear();

        var resolved = await service.ResolveDestinationAsync(created.ShortCode, null, null);

        Assert.Equal("https://www.enterprise-finance.example.com/fast-redirect", resolved);
    }

    [Fact]
    public async Task ResolveDestinationAsync_ExpiredLink_ThrowsUrlGoneException()
    {
        var repository = new InMemoryTestUrlRepository();
        await repository.AddAsync(new ShortUrl
        {
            ShortCode = "expired1",
            OriginalUrl = "https://example.com/expired",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), // Expired
            IsActive = true
        });

        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, new Base58CodeGenerator(), new AlwaysValidSecurityValidator(), cache, NullLogger<UrlShortenerService>.Instance);

        await Assert.ThrowsAsync<UrlGoneException>(() => service.ResolveDestinationAsync("expired1", null, null));
    }

    [Fact]
    public async Task ResolveDestinationAsync_RevokedLink_ThrowsUrlGoneException()
    {
        var repository = new InMemoryTestUrlRepository();
        await repository.AddAsync(new ShortUrl
        {
            ShortCode = "revoked1",
            OriginalUrl = "https://example.com/revoked",
            IsActive = false // Revoked
        });

        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, new Base58CodeGenerator(), new AlwaysValidSecurityValidator(), cache, NullLogger<UrlShortenerService>.Instance);

        await Assert.ThrowsAsync<UrlGoneException>(() => service.ResolveDestinationAsync("revoked1", null, null));
    }

    [Fact]
    public async Task RevokeAsync_ExistingLink_DeactivatesAndEvictsCache()
    {
        var repository = new InMemoryTestUrlRepository();
        var cache = CreateMemoryCache();
        var service = new UrlShortenerService(repository, new Base58CodeGenerator(), new AlwaysValidSecurityValidator(), cache, NullLogger<UrlShortenerService>.Instance);

        var created = await service.CreateAsync(new CreateShortUrlRequest
        {
            OriginalUrl = "https://example.com/to-revoke"
        }, "https://go.example.com");

        var revoked = await service.RevokeAsync(created.ShortCode);

        Assert.True(revoked);
        var updated = await repository.GetByShortCodeAsync(created.ShortCode);
        Assert.NotNull(updated);
        Assert.False(updated.IsActive);

        // Subsequent resolution should throw UrlGoneException
        await Assert.ThrowsAsync<UrlGoneException>(() => service.ResolveDestinationAsync(created.ShortCode, null, null));
    }

    // =========================================================================
    // Test Helpers & Test Fakes
    // =========================================================================

    private static UrlSecurityValidator CreateValidator(bool isDevelopment, string? shortenerDomain = null)
    {
        var env = new TestHostEnvironment { EnvironmentName = isDevelopment ? Environments.Development : Environments.Production };
        var inMemorySettings = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(shortenerDomain))
        {
            inMemorySettings["Shortener:Domain"] = shortenerDomain;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        return new UrlSecurityValidator(env, config);
    }

    private static IMemoryCache CreateMemoryCache()
    {
        return new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000,
            CompactionPercentage = 0.20
        });
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "UrlShortener.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class AlwaysValidSecurityValidator : IUrlSecurityValidator
    {
        public Task<UrlValidationResult> ValidateAsync(string url, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UrlValidationResult.Success(IPAddress.Loopback));
        }
    }

    private sealed class SequentialTestCodeGenerator : IShortCodeGenerator
    {
        private readonly Queue<string> _sequence;

        public SequentialTestCodeGenerator(params string[] codes)
        {
            _sequence = new Queue<string>(codes);
        }

        public string Generate(int length = 7)
        {
            return _sequence.Count > 0 ? _sequence.Dequeue() : "FALLBACK";
        }
    }

    private sealed class InMemoryTestUrlRepository : IUrlRepository
    {
        private readonly ConcurrentDictionary<string, ShortUrl> _store = new();
        private readonly List<ClickEvent> _clicks = new();

        public void Clear() => _store.Clear();

        public Task<ShortUrl?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue(shortCode, out var value);
            return Task.FromResult(value);
        }

        public Task<bool> ExistsAsync(string shortCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.ContainsKey(shortCode));
        }

        public Task AddAsync(ShortUrl shortUrl, CancellationToken cancellationToken = default)
        {
            if (!_store.TryAdd(shortUrl.ShortCode, shortUrl))
            {
                throw new InvalidOperationException($"Duplicate short code '{shortUrl.ShortCode}'");
            }
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ShortUrl shortUrl, CancellationToken cancellationToken = default)
        {
            _store[shortUrl.ShortCode] = shortUrl;
            return Task.CompletedTask;
        }

        public Task RecordClickAsync(ClickEvent clickEvent, CancellationToken cancellationToken = default)
        {
            lock (_clicks) { _clicks.Add(clickEvent); }
            return Task.CompletedTask;
        }

        public Task RecordClicksBatchAsync(IEnumerable<ClickEvent> clickEvents, CancellationToken cancellationToken = default)
        {
            lock (_clicks) { _clicks.AddRange(clickEvents); }
            return Task.CompletedTask;
        }

        public Task<(long TotalClicks, long UniqueVisitors)> GetAnalyticsStatsAsync(string shortCode, CancellationToken cancellationToken = default)
        {
            lock (_clicks)
            {
                var matching = _clicks.Where(c => c.ShortCode == shortCode).ToList();
                var totalClicks = (long)matching.Count;
                var uniqueVisitors = (long)matching.Select(c => c.HashedIp).Distinct().Count();
                return Task.FromResult((totalClicks, uniqueVisitors));
            }
        }
    }
}
