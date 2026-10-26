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
        // 15-second cadence so short expirations (ReportExpirationTime = 1 minute)
        // are honored promptly instead of waiting a full 5 minutes (user direction).
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

        private readonly ReportSharingService _reportSharing;
        private readonly ILogger<ReportSweepWorker> _logger;

        public ReportSweepWorker(ReportSharingService reportSharing, ILogger<ReportSweepWorker> logger)
        {
            _reportSharing = reportSharing;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ReportSharing sweep worker started (every {Seconds} seconds).", Interval.TotalSeconds);
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

                try
                {
                    _reportSharing.SweepOnce();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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