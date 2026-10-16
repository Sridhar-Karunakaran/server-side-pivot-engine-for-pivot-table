using PivotController.Configuration.Models;

namespace PivotController.Indexing;

/// <summary>
/// Spec §12.4 — Auto-Reload. For every enabled index with AutoReload=true and
/// ReloadIntervalMinutes > 0, reloads the index data periodically. The refresh
/// time is measured from Data Server startup (Flexmonster semantics): the first
/// reload of each index happens at startup + ReloadIntervalMinutes.
/// Reload failures are logged and never crash the host; KeepDataOnRefresh governs
/// whether the previous data keeps being served (IndexManager.LoadAsync semantics).
/// </summary>
public sealed class IndexReloadWorker : BackgroundService
{
    private readonly IIndexManager _indexes;
    private readonly ILogger<IndexReloadWorker> _logger;
    private readonly List<IndexTimer> _timers = [];

    public IndexReloadWorker(IIndexManager indexes, ILogger<IndexReloadWorker> logger)
    {
        _indexes = indexes;
        _logger = logger;
    }

    private sealed record IndexTimer(string IndexName, int IntervalMinutes, PeriodicTimer Timer);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Seed once after startup so the worker observes the final index set
        // (appsettings bootstrap + runtime indexes.json are both applied by then).
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        SeedTimers();

        while (!stoppingToken.IsCancellationRequested)
        {
            IndexTimer? next = _timers
                .OrderBy(t => t.IntervalMinutes)
                .FirstOrDefault();
            if (next is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                if (!await next.Timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ReloadIndexAsync(next.IndexName, stoppingToken).ConfigureAwait(false);
            // Re-seed so configuration changes (interval edits, newly created,
            // enabled or disabled indexes) take effect without a restart.
            SeedTimers();
        }
    }

    private void SeedTimers()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HashSet<string> active = _indexes.Config.Indexes
            .Where(i => i.Enabled && i.AutoReload && i.ReloadIntervalMinutes > 0)
            .Select(i => i.Index)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove timers whose index was deleted, disabled, or had AutoReload turned off.
        foreach (IndexTimer timer in _timers.Where(t => !active.Contains(t.IndexName)).ToList())
        {
            timer.Timer.Dispose();
            _timers.Remove(timer);
            _logger.LogInformation("Auto-reload removed for index {Index}.", timer.IndexName);
        }

        // Interval changed? Replace the timer so the new cadence applies.
        foreach (IndexDefinition definition in _indexes.Config.Indexes)
        {
            if (!definition.Enabled || !definition.AutoReload || definition.ReloadIntervalMinutes <= 0)
            {
                continue;
            }
            IndexTimer? existing = _timers.FirstOrDefault(t =>
                string.Equals(t.IndexName, definition.Index, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.IntervalMinutes != definition.ReloadIntervalMinutes)
                {
                    existing.Timer.Dispose();
                    _timers.Remove(existing);
                }
                else
                {
                    continue;
                }
            }

            _timers.Add(new IndexTimer(
                definition.Index,
                definition.ReloadIntervalMinutes,
                new PeriodicTimer(TimeSpan.FromMinutes(definition.ReloadIntervalMinutes))));
            _logger.LogInformation(
                "Auto-reload scheduled for index {Index} every {Minutes} minute(s) from server start.",
                definition.Index, definition.ReloadIntervalMinutes);
        }
    }

    private async Task ReloadIndexAsync(string indexName, CancellationToken cancellationToken)
    {
        try
        {
            IndexSnapshot snapshot = await _indexes.ReloadAsync(indexName).ConfigureAwait(false);
            _logger.LogInformation(
                "Auto-reloaded index {Index}: {RowCount} rows at {LoadedAt}.",
                indexName, snapshot.RowCount, snapshot.LoadedAt);
        }
        catch (IndexNotFoundException)
        {
            // Index was deleted; drop its timer.
            IndexTimer timer = _timers.FirstOrDefault(t => string.Equals(t.IndexName, indexName, StringComparison.OrdinalIgnoreCase));
            if (timer is not null)
            {
                timer.Timer.Dispose();
                _timers.Remove(timer);
            }
        }
        catch (IndexDisabledException)
        {
            // Index was disabled since seeding; drop its timer.
            IndexTimer timer = _timers.FirstOrDefault(t => string.Equals(t.IndexName, indexName, StringComparison.OrdinalIgnoreCase));
            if (timer is not null)
            {
                timer.Timer.Dispose();
                _timers.Remove(timer);
            }
            _logger.LogInformation("Auto-reload stopped for disabled index {Index}.", indexName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Never crash the host; KeepDataOnRefresh keeps the previous data serving.
            _logger.LogWarning(exception, "Auto-reload failed for index {Index}; previous data kept serving.", indexName);
        }
    }

    public override void Dispose()
    {
        foreach (IndexTimer timer in _timers)
        {
            timer.Timer.Dispose();
        }
        _timers.Clear();
        base.Dispose();
    }
}
