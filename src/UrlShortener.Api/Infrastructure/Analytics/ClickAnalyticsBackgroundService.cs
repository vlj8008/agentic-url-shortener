using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrlShortener.Api.Domain.Entities;
using UrlShortener.Api.Infrastructure.Data;

namespace UrlShortener.Api.Infrastructure.Analytics;

/// <summary>
/// Background worker consuming click events from a bounded channel and persisting them to the database (specs/tasks.md T018).
/// Decouples analytics persistence from the high-throughput HTTP redirect path.
/// </summary>
public sealed class ClickAnalyticsBackgroundService : BackgroundService
{
    private readonly ChannelReader<ClickEvent> _channelReader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClickAnalyticsBackgroundService> _logger;

    public ClickAnalyticsBackgroundService(
        ChannelReader<ClickEvent> channelReader,
        IServiceScopeFactory scopeFactory,
        ILogger<ClickAnalyticsBackgroundService> logger)
    {
        _channelReader = channelReader;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClickAnalyticsBackgroundService started listening for redirect click events.");

        try
        {
            await foreach (var clickEvent in _channelReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var repository = scope.ServiceProvider.GetRequiredService<IUrlRepository>();
                    await repository.RecordClickAsync(clickEvent, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to persist click event for short code '{ShortCode}'. Event dropped.",
                        clickEvent.ShortCode);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error occurred in ClickAnalyticsBackgroundService message loop.");
        }

        _logger.LogInformation("ClickAnalyticsBackgroundService stopped.");
    }
}
