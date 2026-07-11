using System.Threading.Channels;
using MongoDB.Driver;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Api.Models;

namespace UrlShortener.Api.Services;

/// <summary>
/// Background service that drains the click event Channel and batch-inserts to MongoDB.
/// This decouples the redirect response time from the analytics write latency.
/// Batches up to 100 events or flushes every 2 seconds, whichever comes first.
/// </summary>
public class ClickEventBackgroundService : BackgroundService
{
    private readonly Channel<ClickEvent> _channel;
    private readonly MongoDbContext _db;
    private readonly ILogger<ClickEventBackgroundService> _logger;

    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    public ClickEventBackgroundService(
        Channel<ClickEvent> channel,
        MongoDbContext db,
        ILogger<ClickEventBackgroundService> logger)
    {
        _channel = channel;
        _db = db;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Click event background service started.");

        var batch = new List<ClickEvent>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                batch.Clear();

                // Wait for at least one event
                if (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    // Drain up to BatchSize events or until the channel is empty
                    while (batch.Count < BatchSize && _channel.Reader.TryRead(out var clickEvent))
                    {
                        batch.Add(clickEvent);
                    }
                }

                if (batch.Count > 0)
                {
                    await _db.ClickEvents.InsertManyAsync(batch, cancellationToken: stoppingToken);
                    _logger.LogDebug("Flushed {Count} click events to MongoDB.", batch.Count);
                }
                else
                {
                    // No events available — wait a bit before checking again
                    await Task.Delay(FlushInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — flush remaining events
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing click events to MongoDB.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        // Drain remaining events on shutdown
        batch.Clear();
        while (_channel.Reader.TryRead(out var remaining))
        {
            batch.Add(remaining);
        }

        if (batch.Count > 0)
        {
            try
            {
                await _db.ClickEvents.InsertManyAsync(batch);
                _logger.LogInformation("Flushed {Count} remaining click events on shutdown.", batch.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing remaining click events on shutdown.");
            }
        }

        _logger.LogInformation("Click event background service stopped.");
    }
}
