using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UrlShortener.Api.Domain.Contracts;
using UrlShortener.Api.Domain.Entities;
using UrlShortener.Api.Domain.Exceptions;
using UrlShortener.Api.Infrastructure.Data;

namespace UrlShortener.Api.Services;

/// <summary>
/// Implements core URL shortening and cached resolution with collision retries and telemetry hooks (specs/tasks.md T012, T019).
/// </summary>
public sealed class UrlShortenerService : IUrlShortenerService
{
    private const int MaxCollisionRetries = 3;
    private const int GeneratedCodeLength = 7;
    private static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromHours(24);

    private readonly IUrlRepository _repository;
    private readonly IShortCodeGenerator _codeGenerator;
    private readonly IUrlSecurityValidator _validator;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UrlShortenerService> _logger;
    private readonly ChannelWriter<ClickEvent> _channelWriter;
    private readonly IIpPrivacyHasher _privacyHasher;

    /// <summary>
    /// Legacy constructor preserving backward compatibility with Phase 3 (US1) unit tests.
    /// </summary>
    public UrlShortenerService(
        IUrlRepository repository,
        IShortCodeGenerator codeGenerator,
        IUrlSecurityValidator validator,
        IMemoryCache cache,
        ILogger<UrlShortenerService> logger)
        : this(
            repository,
            codeGenerator,
            validator,
            cache,
            logger,
            new IpPrivacyHasher(new ConfigurationBuilder().Build()),
            Channel.CreateUnbounded<ClickEvent>().Writer)
    {
    }

    /// <summary>
    /// Primary DI constructor used by the application host (T019).
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public UrlShortenerService(
        IUrlRepository repository,
        IShortCodeGenerator codeGenerator,
        IUrlSecurityValidator validator,
        IMemoryCache cache,
        ILogger<UrlShortenerService> logger,
        IIpPrivacyHasher privacyHasher,
        ChannelWriter<ClickEvent> channelWriter)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(codeGenerator);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(privacyHasher);
        ArgumentNullException.ThrowIfNull(channelWriter);

        _repository = repository;
        _codeGenerator = codeGenerator;
        _validator = validator;
        _cache = cache;
        _logger = logger;
        _privacyHasher = privacyHasher;
        _channelWriter = channelWriter;
    }

    /// <summary>
    /// Validates destination URL, generates or validates short code with bounded retry, saves to repository, and warms the cache.
    /// </summary>
    public async Task<ShortUrlResponse> CreateAsync(
        CreateShortUrlRequest request,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OriginalUrl))
        {
            throw new ArgumentException("Original URL cannot be empty.", nameof(request));
        }

        // Validate destination URL against Anti-SSRF and scheme policies (T011)
        var validationResult = await _validator.ValidateAsync(request.OriginalUrl, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ArgumentException(validationResult.ErrorMessage ?? "Invalid destination URL.", nameof(request));
        }

        // Validate expiration timestamp if provided
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException("Expiration timestamp must be in the future (UTC).", nameof(request));
        }

        ShortUrl? persistedEntity = null;

        if (!string.IsNullOrWhiteSpace(request.CustomSlug))
        {
            var slug = request.CustomSlug.Trim();
            if (slug.Length < 4 || slug.Length > 32)
            {
                throw new ArgumentException("Custom slug must be between 4 and 32 characters in length.", nameof(request));
            }

            if (await _repository.ExistsAsync(slug, cancellationToken))
            {
                throw new InvalidOperationException($"Custom alias '{slug}' is already taken.");
            }

            var customEntity = new ShortUrl
            {
                ShortCode = slug,
                OriginalUrl = request.OriginalUrl,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = request.ExpiresAt,
                IsActive = true
            };

            try
            {
                await _repository.AddAsync(customEntity, cancellationToken);
                persistedEntity = customEntity;
            }
            catch (DbUpdateException)
            {
                throw new InvalidOperationException($"Custom alias '{slug}' is already taken.");
            }
        }
        else
        {
            // Bounded 3-attempt collision retry loop enclosing database persistence
            for (var attempt = 1; attempt <= MaxCollisionRetries; attempt++)
            {
                var candidate = _codeGenerator.Generate(GeneratedCodeLength);

                // Quick read-check to avoid unnecessary write attempts on known collisions
                if (await _repository.ExistsAsync(candidate, cancellationToken))
                {
                    _logger.LogWarning(
                        "Pre-check collision detected for generated short code '{Candidate}' on attempt {Attempt}/{MaxAttempts}. Retrying...",
                        candidate,
                        attempt,
                        MaxCollisionRetries);
                    continue;
                }

                try
                {
                    var entity = new ShortUrl
                    {
                        ShortCode = candidate,
                        OriginalUrl = request.OriginalUrl,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExpiresAt = request.ExpiresAt,
                        IsActive = true
                    };

                    await _repository.AddAsync(entity, cancellationToken);
                    persistedEntity = entity;
                    break;
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Concurrent race collision detected for short code '{Candidate}' on attempt {Attempt}/{MaxAttempts}. Retrying...",
                        candidate,
                        attempt,
                        MaxCollisionRetries);
                }
            }

            if (persistedEntity is null)
            {
                _logger.LogError(
                    "Exhausted all {MaxAttempts} attempts generating a unique short code.",
                    MaxCollisionRetries);
                throw new ShortCodeCollisionException(
                    "Unable to generate a unique short code after 3 attempts due to high hash density. Please retry shortly.");
            }
        }

        ArgumentNullException.ThrowIfNull(persistedEntity);

        // Cache warm-up with bounded size entry
        CacheEntry(persistedEntity);

        var cleanBaseUrl = baseUrl.TrimEnd('/');
        return new ShortUrlResponse
        {
            ShortCode = persistedEntity.ShortCode,
            ShortUrl = $"{cleanBaseUrl}/{persistedEntity.ShortCode}",
            OriginalUrl = persistedEntity.OriginalUrl,
            CreatedAt = persistedEntity.CreatedAt,
            ExpiresAt = persistedEntity.ExpiresAt,
            IsActive = persistedEntity.IsActive
        };
    }

    /// <summary>
    /// Resolves destination URL from cache (sub-50ms) or database fallback, verifying active status and expiration.
    /// Publishes a non-blocking click event for asynchronous background persistence (T019).
    /// </summary>
    public async Task<string?> ResolveDestinationAsync(
        string shortCode,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            return null;
        }

        var cacheKey = GetCacheKey(shortCode);

        if (!_cache.TryGetValue(cacheKey, out CachedUrlMapping? cached) || cached is null)
        {
            var entity = await _repository.GetByShortCodeAsync(shortCode, cancellationToken);
            if (entity is null)
            {
                return null;
            }

            cached = new CachedUrlMapping(entity.OriginalUrl, entity.IsActive, entity.ExpiresAt);
            CacheEntry(shortCode, cached);
        }

        // Check revocation and expiration (OpenAPI 410 Gone)
        if (!cached.IsActive || (cached.ExpiresAt.HasValue && cached.ExpiresAt.Value <= DateTimeOffset.UtcNow))
        {
            throw new UrlGoneException($"The shortened link '{shortCode}' has expired or been revoked.");
        }

        // Publish non-blocking click event for asynchronous background persistence (T018/T019)
        var clickEvent = new ClickEvent
        {
            ShortCode = shortCode,
            HashedIp = _privacyHasher.HashIp(clientIp),
            UserAgent = userAgent?.Length > 512 ? userAgent[..512] : userAgent,
            Timestamp = DateTimeOffset.UtcNow
        };

        _channelWriter.TryWrite(clickEvent);

        return cached.OriginalUrl;
    }

    /// <summary>
    /// Retrieves metadata and aggregated click analytics for an existing short code.
    /// </summary>
    public async Task<UrlMetadataResponse?> GetMetadataAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            return null;
        }

        var entity = await _repository.GetByShortCodeAsync(shortCode, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        var (totalClicks, uniqueVisitors) = await _repository.GetAnalyticsStatsAsync(shortCode, cancellationToken);

        return new UrlMetadataResponse
        {
            ShortCode = entity.ShortCode,
            OriginalUrl = entity.OriginalUrl,
            TotalClicks = totalClicks,
            UniqueVisitors = uniqueVisitors,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            IsActive = entity.IsActive
        };
    }

    /// <summary>
    /// Soft-deletes a short code by setting IsActive = false and evicting it from cache. Subsequent redirects return 410 Gone.
    /// </summary>
    public async Task<bool> RevokeAsync(
        string shortCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            return false;
        }

        var entity = await _repository.GetByShortCodeAsync(shortCode, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.IsActive = false;
        await _repository.UpdateAsync(entity, cancellationToken);

        // Evict previous active entry and cache the inactive state so subsequent redirects
        // immediately return 410 Gone in sub-50ms without hitting the database
        _cache.Remove(GetCacheKey(shortCode));
        CacheEntry(entity);

        return true;
    }

    private void CacheEntry(ShortUrl entity)
    {
        var mapping = new CachedUrlMapping(entity.OriginalUrl, entity.IsActive, entity.ExpiresAt);
        CacheEntry(entity.ShortCode, mapping);
    }

    private void CacheEntry(string shortCode, CachedUrlMapping mapping)
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            Size = 1 // Enforces bounded memory cache size entry weight
        };

        if (mapping.ExpiresAt.HasValue)
        {
            var remaining = mapping.ExpiresAt.Value - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                cacheOptions.SetAbsoluteExpiration(mapping.ExpiresAt.Value);
            }
            else
            {
                return;
            }
        }
        else
        {
            cacheOptions.SetSlidingExpiration(DefaultCacheTtl);
        }

        _cache.Set(GetCacheKey(shortCode), mapping, cacheOptions);
    }

    private static string GetCacheKey(string shortCode) => $"shorturl:{shortCode}";

    private sealed record CachedUrlMapping(string OriginalUrl, bool IsActive, DateTimeOffset? ExpiresAt);
}
