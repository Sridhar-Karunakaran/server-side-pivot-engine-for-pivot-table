using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PivotController.Reports.Services;

namespace PivotController.Reports.Services
{
    /// <summary>
    /// Background cleanup worker (spec §6.4/§6.6): every 5 minutes removes expired reports
    /// (regardless of AutoDeletingEnabled) and, when AutoDeletingEnabled manages the
    /// MaxSizeOfFolder quota, evicts oldest reports until under the limit.
    /// </summary>
    public class ReportSweepWorker : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        private readonly ReportSharingService _reportSharing;
        private readonly ILogger<ReportSweepWorker> _logger;

        public ReportSweepWorker(ReportSharingService reportSharing, ILogger<ReportSweepWorker> logger)
        {
            _reportSharing = reportSharing;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReportSharing sweep worker started (every {Minutes} minutes).", Interval.TotalMinutes);
            // Startup pass: remove reports already expired before this run (spec §6.4).
            try
            {
                _reportSharing.SweepOnce();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReportSharing startup sweep failed.");
            }
            using var timer = new PeriodicTimer(Interval);
            while (!stoppingToken.IsCancellationRequested
                && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    _reportSharing.SweepOnce();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ReportSharing sweep failed.");
                }
            }
        }
    }
}