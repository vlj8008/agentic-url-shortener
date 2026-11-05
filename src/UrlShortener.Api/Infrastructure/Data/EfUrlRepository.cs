using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Domain.Entities;

namespace UrlShortener.Api.Infrastructure.Data;

/// <summary>
/// EF Core implementation of IUrlRepository with strict .AsNoTracking() on read paths (specs/tasks.md T008).
/// </summary>
public sealed class EfUrlRepository : IUrlRepository
{
    private readonly UrlShortenerDbContext _dbContext;

    public EfUrlRepository(UrlShortenerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ShortUrl?> GetByShortCodeAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        // Enforcing .AsNoTracking() on all read queries (no FindAsync on read paths per Constraint 4)
        return await _dbContext.ShortUrls
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.ShortCode == shortCode, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        // Enforcing .AsNoTracking() on read queries
        return await _dbContext.ShortUrls
            .AsNoTracking()
            .AnyAsync(u => u.ShortCode == shortCode, cancellationToken);
    }

    public async Task AddAsync(ShortUrl shortUrl, CancellationToken cancellationToken = default)
    {
        await _dbContext.ShortUrls.AddAsync(shortUrl, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ShortUrl shortUrl, CancellationToken cancellationToken = default)
    {
        _dbContext.ShortUrls.Update(shortUrl);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordClickAsync(ClickEvent clickEvent, CancellationToken cancellationToken = default)
    {
        await _dbContext.ClickEvents.AddAsync(clickEvent, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordClicksBatchAsync(IEnumerable<ClickEvent> clickEvents, CancellationToken cancellationToken = default)
    {
        await _dbContext.ClickEvents.AddRangeAsync(clickEvents, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<(long TotalClicks, long UniqueVisitors)> GetAnalyticsStatsAsync(string shortCode, CancellationToken cancellationToken = default)
    {
        // Enforcing .AsNoTracking() on read analytics aggregation
        var totalClicks = await _dbContext.ClickEvents
            .AsNoTracking()
            .Where(c => c.ShortCode == shortCode)
            .LongCountAsync(cancellationToken);

        var uniqueVisitors = await _dbContext.ClickEvents
            .AsNoTracking()
            .Where(c => c.ShortCode == shortCode)
            .Select(c => c.HashedIp)
            .Distinct()
            .LongCountAsync(cancellationToken);

        return (totalClicks, uniqueVisitors);
    }
}
