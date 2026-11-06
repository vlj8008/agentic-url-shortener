using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Domain.Entities;
using UrlShortener.Api.Domain.Exceptions;
using UrlShortener.Api.Infrastructure.Data;
using UrlShortener.Api.Services;

namespace UrlShortener.UnitTests;

/// <summary>
/// Unit tests for salted SHA-256 IP privacy hashing, telemetry queueing, and cache-safe revocation (specs/tasks.md T020).
/// </summary>
public class PrivacyAndAnalyticsTests
{
    // =========================================================================
    // 1. IP Privacy Hasher Tests (T017 / T020)
    // =========================================================================

    [Fact]
    public void HashIp_ValidIPv4_Returns64CharLowercaseHexSha256()
    {
        var hasher = CreateHasher();
        var hash = hasher.HashIp("198.51.100.42");

        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[a-f0-9]{64}$", hash);
    }

    [Fact]
    public void HashIp_SameIpAddress_IsDeterministic()
    {
        var hasher = CreateHasher();
        var hash1 = hasher.HashIp("203.0.113.19");
        var hash2 = hasher.HashIp("203.0.113.19");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashIp_DifferentIps_ProduceDistinctHashes()
    {
        var hasher = CreateHasher();
        var hash1 = hasher.HashIp("198.51.100.1");
        var hash2 = hasher.HashIp("198.51.100.2");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void HashIp_DifferentSalts_ProduceDistinctHashesForSameIp()
    {
        var hasher1 = CreateHasher("salt-alpha-2026");
        var hasher2 = CreateHasher("salt-beta-2026");

        var hash1 = hasher1.HashIp("198.51.100.99");
        var hash2 = hasher2.HashIp("198.51.100.99");

        Assert.NotEqual(hash1, hash2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HashIp_NullOrWhitespace_NormalizesToUnknownDeterministically(string? input)
    {
        var hasher = CreateHasher();
        var hash = hasher.HashIp(input);
        var expectedUnknownHash = hasher.HashIp("unknown");

        Assert.Equal(expectedUnknownHash, hash);
    }

    [Fact]
    public void HashIp_LeadingAndTrailingWhitespace_IsNormalizedBeforeHashing()
    {
        var hasher = CreateHasher();
        var rawHash = hasher.HashIp("198.51.100.55");
        var paddedHash = hasher.HashIp("   198.51.100.55  \t ");

        Assert.Equal(rawHash, paddedHash);
    }

    [Fact]
    public void HashIp_IPv4MappedIPv6_NormalizesToStandardIPv4()
    {
        var hasher = CreateHasher();
        var ipv4Hash = hasher.HashIp("198.51.100.77");
        var mappedHash = hasher.HashIp("::ffff:198.51.100.77");

        Assert.Equal(ipv4Hash, mappedHash);
    }

    [Fact]
    public void HashIp_IPv6Address_IsCaseInsensitive()
    {
        var hasher = CreateHasher();
        var lower = hasher.HashIp("2001:db8::8a2e:370:7334");
        var upper = hasher.HashIp("2001:DB8::8A2E:370:7334");

        Assert.Equal(lower, upper);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_EmptyOrMissingConfigSalt_FallsBackToDefaultSalt(string? configuredSalt)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Privacy:IpHashSalt"] = configuredSalt
            })
            .Build();

        var fallbackHasher = new IpPrivacyHasher(config);
        var defaultHasher = new IpPrivacyHasher(new ConfigurationBuilder().Build());

        Assert.Equal(defaultHasher.HashIp("198.51.100.1"), fallbackHasher.HashIp("198.51.100.1"));
    }

    // =========================================================================
    // 2. Click Telemetry Queueing Tests (T018 / T019 / T020)
    // =========================================================================

    [Fact]
    public async Task ResolveDestinationAsync_EnqueuesClickEventWithHashedIpAndTruncatedUserAgent()
    {
        var repository = new InMemoryAnalyticsRepository();
        var channel = Channel.CreateUnbounded<ClickEvent>();
        var hasher = CreateHasher();
        var service = CreateService(repository, hasher, channel.Writer);

        var shortUrl = new ShortUrl
        {
            ShortCode = "metric1",
            OriginalUrl = "https://example.com/analytics-target",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await repository.AddAsync(shortUrl);

        var rawIp = "198.51.100.88";
        var expectedHash = hasher.HashIp(rawIp);
        var longUserAgent = new string('A', 600);

        var resolved = await service.ResolveDestinationAsync("metric1", rawIp, longUserAgent);

        Assert.Equal("https://example.com/analytics-target", resolved);

        // Verify channel has event
        Assert.True(channel.Reader.TryRead(out var queuedEvent));
        Assert.NotNull(queuedEvent);
        Assert.Equal("metric1", queuedEvent.ShortCode);
        Assert.Equal(expectedHash, queuedEvent.HashedIp);
        Assert.DoesNotContain(rawIp, queuedEvent.HashedIp); // Plain IP is never stored
        Assert.Equal(512, queuedEvent.UserAgent?.Length);
        Assert.Equal(new string('A', 512), queuedEvent.UserAgent);
        Assert.True(queuedEvent.Timestamp <= DateTimeOffset.UtcNow);
        Assert.True(queuedEvent.Timestamp >= DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task ResolveDestinationAsync_WhenChannelFull_DoesNotBlockOrThrow()
    {
        var repository = new InMemoryAnalyticsRepository();
        // Bounded channel of capacity 1 with DropWrite
        var channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
        var hasher = CreateHasher();
        var service = CreateService(repository, hasher, channel.Writer);

        var shortUrl = new ShortUrl
        {
            ShortCode = "fullchan",
            OriginalUrl = "https://example.com/full-channel-test",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await repository.AddAsync(shortUrl);

        // First write fills channel
        var r1 = await service.ResolveDestinationAsync("fullchan", "198.51.100.1", "Agent1");
        Assert.Equal("https://example.com/full-channel-test", r1);

        // Second write when full should not block or throw
        var r2 = await service.ResolveDestinationAsync("fullchan", "198.51.100.2", "Agent2");
        Assert.Equal("https://example.com/full-channel-test", r2);
    }

    // =========================================================================
    // 3. Cache-Safe Revocation & Metadata Tests (T019 / T020)
    // =========================================================================

    [Fact]
    public async Task RevokeAsync_MarksLinkInactiveAndSubsequentResolutionThrowsUrlGoneExceptionFromCache()
    {
        var repository = new InMemoryAnalyticsRepository();
        var service = CreateService(repository);

        var shortUrl = new ShortUrl
        {
            ShortCode = "revokeme",
            OriginalUrl = "https://example.com/pre-revoke",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await repository.AddAsync(shortUrl);

        // 1. Initial resolution succeeds and populates cache
        var destination = await service.ResolveDestinationAsync("revokeme", "198.51.100.1", "TestAgent");
        Assert.Equal("https://example.com/pre-revoke", destination);

        // 2. Revoke link
        var revoked = await service.RevokeAsync("revokeme");
        Assert.True(revoked);

        // Verify entity in repository is marked inactive
        var stored = await repository.GetByShortCodeAsync("revokeme");
        Assert.NotNull(stored);
        Assert.False(stored.IsActive);

        // Clear repository to verify subsequent resolution is served directly from cache without hitting repository
        repository.Clear();

        // 3. Subsequent resolution immediately throws UrlGoneException from cache without needing database
        var ex = await Assert.ThrowsAsync<UrlGoneException>(() =>
            service.ResolveDestinationAsync("revokeme", "198.51.100.1", "TestAgent"));
        Assert.Contains("revokeme", ex.Message);
    }

    [Fact]
    public async Task RevokeAsync_NonExistentShortCode_ReturnsFalse()
    {
        var repository = new InMemoryAnalyticsRepository();
        var service = CreateService(repository);

        var result = await service.RevokeAsync("nonexistent");
        Assert.False(result);
    }

    [Fact]
    public async Task GetMetadataAsync_ExistingShortCode_ReturnsAggregatedStats()
    {
        var repository = new InMemoryAnalyticsRepository();
        var service = CreateService(repository);

        var shortUrl = new ShortUrl
        {
            ShortCode = "statlink",
            OriginalUrl = "https://example.com/stats",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        await repository.AddAsync(shortUrl);

        // Seed 3 clicks with 2 unique hashed IPs
        await repository.RecordClickAsync(new ClickEvent { ShortCode = "statlink", HashedIp = "hash1" });
        await repository.RecordClickAsync(new ClickEvent { ShortCode = "statlink", HashedIp = "hash2" });
        await repository.RecordClickAsync(new ClickEvent { ShortCode = "statlink", HashedIp = "hash1" });

        var metadata = await service.GetMetadataAsync("statlink");

        Assert.NotNull(metadata);
        Assert.Equal("statlink", metadata.ShortCode);
        Assert.Equal("https://example.com/stats", metadata.OriginalUrl);
        Assert.Equal(3, metadata.TotalClicks);
        Assert.Equal(2, metadata.UniqueVisitors);
        Assert.True(metadata.IsActive);
    }

    [Fact]
    public async Task GetMetadataAsync_NonExistentShortCode_ReturnsNull()
    {
        var repository = new InMemoryAnalyticsRepository();
        var service = CreateService(repository);

        var metadata = await service.GetMetadataAsync("missingcode");
        Assert.Null(metadata);
    }

    // =========================================================================
    // Helpers and Test Doubles
    // =========================================================================

    private static IIpPrivacyHasher CreateHasher(string? salt = null)
    {
        var configBuilder = new ConfigurationBuilder();
        if (salt is not null)
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Privacy:IpHashSalt"] = salt
            });
        }
        return new IpPrivacyHasher(configBuilder.Build());
    }

    private static UrlShortenerService CreateService(
        IUrlRepository repository,
        IIpPrivacyHasher? hasher = null,
        ChannelWriter<ClickEvent>? writer = null)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000,
            CompactionPercentage = 0.20
        });

        return new UrlShortenerService(
            repository,
            new ConstantCodeGenerator("abcdefg"),
            new PassThroughSecurityValidator(),
            memoryCache,
            NullLogger<UrlShortenerService>.Instance,
            hasher ?? CreateHasher(),
            writer ?? Channel.CreateUnbounded<ClickEvent>().Writer);
    }

    private sealed class ConstantCodeGenerator : IShortCodeGenerator
    {
        private readonly string _code;
        public ConstantCodeGenerator(string code) => _code = code;
        public string Generate(int length = 7) => _code;
    }

    private sealed class PassThroughSecurityValidator : IUrlSecurityValidator
    {
        public Task<UrlValidationResult> ValidateAsync(string url, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UrlValidationResult.Success(IPAddress.Loopback));
        }
    }

    private sealed class InMemoryAnalyticsRepository : IUrlRepository
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
            _store[shortUrl.ShortCode] = shortUrl;
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
