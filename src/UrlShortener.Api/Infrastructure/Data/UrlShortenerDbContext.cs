using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Domain.Entities;

namespace UrlShortener.Api.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context configured for SQL Server (and InMemory providers for unit testing).
/// </summary>
public class UrlShortenerDbContext : DbContext
{
    public UrlShortenerDbContext(DbContextOptions<UrlShortenerDbContext> options)
        : base(options)
    {
    }

    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ShortUrl>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.ShortCode)
                .IsUnique();

            entity.Property(e => e.ShortCode)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.OriginalUrl)
                .IsRequired()
                .HasMaxLength(2048);

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.Property(e => e.ConcurrencyToken)
                .IsRowVersion();

            entity.HasMany(e => e.ClickEvents)
                .WithOne(e => e.ShortUrl)
                .HasForeignKey(e => e.ShortUrlId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.ShortCode);

            entity.Property(e => e.ShortCode)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(e => e.HashedIp)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(e => e.UserAgent)
                .HasMaxLength(512);
        });
    }
}
