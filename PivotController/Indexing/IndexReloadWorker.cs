using PivotController.Configuration.Models;

namespace PivotController.Indexing
{
    /// <summary>Background service that auto-reloads indexes with AutoReload=true at their configured intervals.</summary>
    public class IndexReloadWorker : BackgroundService
    {
        private readonly IIndexManager _indexManager;
        private readonly ILogger<IndexReloadWorker> _logger;

        public IndexReloadWorker(IIndexManager indexManager, ILogger<IndexReloadWorker> logger)
        {
            _indexManager = indexManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            var lastRun = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            var observedUpdates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            var startedAt = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                foreach (var status in _indexManager.ListIndexes())
                {
                    if (!_indexManager.TryGetDefinition(status.Index, out var definition)
                        || !definition.AutoReload
                        || definition.ReloadIntervalMinutes <= 0
                        || !definition.Enabled)
                    {
                        continue;
                    }

                    if (!observedUpdates.TryGetValue(status.Index, out var observedUpdate)
                        || observedUpdate != definition.UpdatedAt)
                    {
                        observedUpdates[status.Index] = definition.UpdatedAt;
                        lastRun[status.Index] = status.LoadedAt ?? startedAt;
                    }

                    var due = lastRun.TryGetValue(status.Index, out var last)
                        ? last.AddMinutes(definition.ReloadIntervalMinutes)
                        : startedAt.AddMinutes(definition.ReloadIntervalMinutes);
                    if (DateTime.UtcNow >= due)
                    {
                        lastRun[status.Index] = DateTime.UtcNow;
                        try
                        {
                            await _indexManager.ReloadIndexAsync(status.Index, manual: false).ConfigureAwait(false);
                        }
                        catch (ReloadFailedKeepDataException)
                        {
                            // Already logged by IndexManager; cached data continues to serve.
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Automatic reload of index '{Index}' failed.", status.Index);
                        }
                    }
                }
            }
        }
    }
}
