using UrlShortener.Api.Domain.Entities;

namespace UrlShortener.Api.Infrastructure.Data;

/// <summary>
/// Repository abstraction for ShortUrl and ClickEvent persistence.
/// </summary>
public interface IUrlRepository
{
    Task<ShortUrl?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string shortCode, CancellationToken cancellationToken = default);
    Task AddAsync(ShortUrl shortUrl, CancellationToken cancellationToken = default);
    Task UpdateAsync(ShortUrl shortUrl, CancellationToken cancellationToken = default);
    Task RecordClickAsync(ClickEvent clickEvent, CancellationToken cancellationToken = default);
    Task RecordClicksBatchAsync(IEnumerable<ClickEvent> clickEvents, CancellationToken cancellationToken = default);
    Task<(long TotalClicks, long UniqueVisitors)> GetAnalyticsStatsAsync(string shortCode, CancellationToken cancellationToken = default);
}
