using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;
using PivotController.Reports.Models;

namespace PivotController.Reports.Services
{
    /// <summary>
    /// Report storage (spec §6): save/retrieve/list/delete with expiration and quota.
    /// Quota handling (clarified §6.5): auto-delete processing occurs BEFORE the storage
    /// validation failure is returned — with AutoDelete enabled, oldest reports are
    /// deleted first to make room; if eviction cannot free enough space, the save
    /// returns 507 Insufficient Storage.
    /// </summary>
    public class ReportSharingService
    {
        private readonly ReportSharingSettings _settings;
        private readonly GeneralSettingsService _general;
        private readonly AtomicJsonStore _store;
        private readonly ILogger<ReportSharingService> _logger;
        private readonly object _sync = new();

        public ReportSharingService(ReportSharingSettings settings, GeneralSettingsService general,
            AtomicJsonStore store, ILogger<ReportSharingService> logger)
        {
            _settings = settings;
            _general = general;
            _store = store;
            _logger = logger;
        }

        private string StorageFolder => _settings.ResolveStorageFolder(_general.DataFolderPath, _general.DataFolderPath);

        private void EnsureFolder() => Directory.CreateDirectory(StorageFolder);

        private string PathFor(string reportId) => System.IO.Path.Combine(StorageFolder, reportId + ".json");

        public List<ReportMetadata> ListReports()
        {
            lock (_sync)
            {
                EnsureFolder();
                return Directory.EnumerateFiles(StorageFolder, "*.json")
                    .Select(f => _store.Load<StoredReport>(f, new StoredReport()))
                    .Where(r => !string.IsNullOrEmpty(r.ReportId))
                    .Select(r => new ReportMetadata
                    {
                        ReportId = r.ReportId,
                        Name = r.Name,
                        Owner = r.Owner,
                        CreatedAt = r.CreatedAt,
                        ExpiresAt = r.ExpiresAt,
                        SizeBytes = r.SizeBytes
                    })
                    .OrderBy(r => r.CreatedAt)
                    .ToList();
            }
        }

        /// <summary>Saves a report (spec §6.3.1/§6.4/§6.5). Returns (report, errors, status).</summary>
        public (StoredReport? Report, List<string> Errors) Save(string name, string? owner, object? payloadRawJson)
        {
            lock (_sync)
            {
                EnsureFolder();
                var errors = new List<string>();
                if (string.IsNullOrWhiteSpace(name)) errors.Add("name is required");
                if (payloadRawJson == null) errors.Add("payload is required");
                if (errors.Count > 0) return (null, errors);

                var report = new StoredReport
                {
                    Name = name,
                    Owner = owner,
                    Payload = payloadRawJson,
                    CreatedAt = DateTime.UtcNow
                };
                if (_settings.ReportExpirationTimeMinutes > 0)
                {
                    report.ExpiresAt = report.CreatedAt.AddMinutes(_settings.ReportExpirationTimeMinutes);
                }

                var serialized = JsonConvert.SerializeObject(report, Formatting.Indented);
                report.SizeBytes = System.Text.Encoding.UTF8.GetByteCount(serialized);

                // Quota handling (clarified §6.5): eviction BEFORE rejection.
                if (_settings.MaxFolderSizeMB > 0)
                {
                    long limitBytes = (long)_settings.MaxFolderSizeMB * 1024 * 1024;
                    long currentBytes = FolderSizeNoLock();
                    if (currentBytes + report.SizeBytes > limitBytes)
                    {
                        if (_settings.AutoDelete)
                        {
                            foreach (var oldest in Directory.EnumerateFiles(StorageFolder, "*.json")
                                .Select(f => (File: f, Report: _store.Load<StoredReport>(f, new StoredReport())))
                                .Where(x => !string.IsNullOrEmpty(x.Report.ReportId))
                                .OrderBy(x => x.Report.CreatedAt)
                                .ToList())
                            {
                                if (currentBytes + report.SizeBytes <= limitBytes) break;
                                currentBytes -= new System.IO.FileInfo(oldest.File).Length;
                                File.Delete(oldest.File);
                                _logger.LogInformation("Report {ReportId} evicted (oldest) to satisfy MaxFolderSizeMB quota.", oldest.Report.ReportId);
                            }
                        }
                        if (currentBytes + report.SizeBytes > limitBytes)
                        {
                            errors.Add($"Report exceeds storage quota ({_settings.MaxFolderSizeMB} MB)");
                            return (null, errors);
                        }
                    }
                }

                _store.Save(PathFor(report.ReportId), report);
                _logger.LogInformation("Report '{Name}' saved as {ReportId} ({SizeBytes} bytes).", name, report.ReportId, report.SizeBytes);
                return (report, errors);
            }
        }

        public (StoredReport? Report, bool Expired) GetReport(string reportId)
        {
            lock (_sync)
            {
                var path = PathFor(reportId);
                if (!File.Exists(path)) return (null, false);
                var report = _store.Load<StoredReport>(path, new StoredReport());
                if (string.IsNullOrEmpty(report.ReportId)) return (null, false);
                if (report.ExpiresAt.HasValue && report.ExpiresAt.Value <= DateTime.UtcNow)
                {
                    return (null, true);
                }
                return (report, false);
            }
        }

        public bool Delete(string reportId)
        {
            lock (_sync)
            {
                var path = PathFor(reportId);
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }

        /// <summary>
        /// Sweep (spec §6.6): deletes expired reports. Runs only when AutoDelete is enabled
        /// (every 5 minutes via ReportSweepWorker).
        /// </summary>
        public int SweepExpired()
        {
            lock (_sync)
            {
                EnsureFolder();
                var deleted = 0;
                foreach (var f in Directory.EnumerateFiles(StorageFolder, "*.json"))
                {
                    var report = _store.Load<StoredReport>(f, new StoredReport());
                    if (string.IsNullOrEmpty(report.ReportId)) continue;
                    if (report.ExpiresAt.HasValue && report.ExpiresAt.Value <= DateTime.UtcNow)
                    {
                        File.Delete(f);
                        deleted++;
                        _logger.LogInformation("Report {ReportId} expired and deleted (AutoDelete sweep).", report.ReportId);
                    }
                }
                return deleted;
            }
        }

        private long FolderSizeNoLock()
        {
            EnsureFolder();
            return Directory.EnumerateFiles(StorageFolder).Sum(f => new System.IO.FileInfo(f).Length);
        }
    }
}