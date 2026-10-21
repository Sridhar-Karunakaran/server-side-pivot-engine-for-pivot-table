namespace PivotController.Reports.Services
{
    /// <summary>
    /// Background sweep (spec §6.6): every 5 minutes, deletes expired reports and evicts
    /// oldest reports when the quota is exceeded — only when AutoDelete is enabled.
    /// </summary>
    public class ReportSweepWorker : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);
        private readonly ReportSharingService _reports;
        private readonly ILogger<ReportSweepWorker> _logger;

        public ReportSweepWorker(ReportSharingService reports, ILogger<ReportSweepWorker> logger)
        {
            _reports = reports;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                    var deleted = _reports.SweepExpired();
                    if (deleted > 0)
                    {
                        _logger.LogInformation("Report sweep deleted {Count} expired report(s).", deleted);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Report sweep failed; will retry next interval.");
                }
            }
        }
    }
}