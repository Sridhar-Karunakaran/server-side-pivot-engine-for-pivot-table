using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PivotController.Core.Persistence;
using PivotController.Reports.Models;

namespace PivotController.Reports.Services
{
    /// <summary>
    /// Report Sharing service (spec §6): file-backed report storage under
    /// {DataFolder}/{PathToFolder} with expiration (independent cleanup) and quota
    /// enforcement (AutoDeletingEnabled evicts oldest; otherwise 507). Settings are
    /// patched through ReportSharingSettingsService and persisted to
    /// {DataFolder}/reportsharing.json.
    /// </summary>
    public class ReportSharingService
    {
        public const string FileName = "reportsharing.json";

        private readonly ReportSharingSettings _settings;
        private readonly GeneralSettingsService _general;
        private readonly ILogger<ReportSharingService> _logger;
        private readonly object _sync = new();

        public ReportSharingService(ReportSharingSettings settings, GeneralSettingsService general,
            ILogger<ReportSharingService> logger)
        {
            _settings = settings;
            _general = general;
            _logger = logger;
        }

        /// <summary>Reports folder: relative PathToFolder resolves against {DataFolder} (spec §6.1).</summary>
        public string ReportsFolderPath
        {
            get
            {
                lock (_sync)
                {
                    var path = _settings.PathToFolder;
                    var root = _general.DataFolderPath;
                    return System.IO.Path.IsPathRooted(path)
                        ? path
                        : System.IO.Path.Combine(root, path);
                }
            }
        }

        public string SettingsFilePath => System.IO.Path.Combine(_general.DataFolderPath, FileName);

        public ReportSharingSettings CurrentSettings
        {
            get { lock (_sync) return _settings.Clone(); }
        }

        public bool EndpointEnabled
        {
            get { lock (_sync) return _settings.EndpointEnabled; }
        }

        // ---- Settings patch (called by ReportSharingConfigController) ----

        /// <summary>
        /// Merge-patch of Report Sharing settings (spec §6.7.2). All fields except
        /// pathToFolder apply immediately; pathToFolder requires restart (folder used at
        /// save time comes from the live object, but existing files are not moved).
        /// </summary>
        public (ReportSharingSettings Settings, bool RequiresRestart, List<string> Errors) ApplyPatch(
            bool? endpointEnabled, string? pathToFolder, int? reportExpirationTime,
            int? maxSizeOfFolder, bool? autoDeletingEnabled)
        {
            lock (_sync)
            {
                var candidate = _settings.Clone();
                if (endpointEnabled.HasValue) candidate.EndpointEnabled = endpointEnabled.Value;
                if (pathToFolder != null) candidate.PathToFolder = pathToFolder;
                if (reportExpirationTime.HasValue) candidate.ReportExpirationTime = reportExpirationTime.Value;
                if (maxSizeOfFolder.HasValue) candidate.MaxSizeOfFolder = maxSizeOfFolder.Value;
                if (autoDeletingEnabled.HasValue) candidate.AutoDeletingEnabled = autoDeletingEnabled.Value;

                var errors = candidate.Validate();
                if (!string.IsNullOrWhiteSpace(pathToFolder)
                    && ReportSharingSettings.IsPathTraversal(pathToFolder, _general.DataFolderPath))
                {
                    errors.Add("PathToFolder path traversal not allowed");
                }
                if (errors.Count > 0)
                {
                    return (CurrentSettings, false, errors);
                }

                bool requiresRestart = false;
                if (!string.Equals(candidate.PathToFolder, _settings.PathToFolder, System.StringComparison.Ordinal))
                {
                    requiresRestart = true; // active folder switches on restart (spec §6.7.2)
                }

                _settings.EndpointEnabled = candidate.EndpointEnabled;
                _settings.PathToFolder = candidate.PathToFolder;
                _settings.ReportExpirationTime = candidate.ReportExpirationTime;
                _settings.MaxSizeOfFolder = candidate.MaxSizeOfFolder;
                _settings.AutoDeletingEnabled = candidate.AutoDeletingEnabled;

                PersistNoLock();
                _logger.LogInformation(
                    "ReportSharing settings applied: endpointEnabled={Endpoint}, pathToFolder={Path}, expirationMinutes={Expiration}, maxSizeMB={Quota}, autoDelete={AutoDelete}",
                    _settings.EndpointEnabled, _settings.PathToFolder, _settings.ReportExpirationTime,
                    _settings.MaxSizeOfFolder, _settings.AutoDeletingEnabled);
                return (CurrentSettings, requiresRestart, errors);
            }
        }

        private void PersistNoLock()
        {
            var store = new AtomicJsonStore(LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
            store.Save(SettingsFilePath, _settings.Clone());
        }

        /// <summary>Bootstrap: loads persisted settings from {DataFolder}/reportsharing.json (spec §6.8).</summary>
        public void LoadPersisted()
        {
            lock (_sync)
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _logger.LogInformation("No persisted reportsharing.json; appsettings ReportSharingOptions in effect.");
                    return;
                }
                var store = new AtomicJsonStore(LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
                var persisted = store.Load<ReportSharingSettings>(SettingsFilePath, new ReportSharingSettings());
                var errors = persisted.Validate();
                if (!string.IsNullOrWhiteSpace(persisted.PathToFolder)
                    && ReportSharingSettings.IsPathTraversal(persisted.PathToFolder, _general.DataFolderPath))
                {
                    errors.Add("PathToFolder path traversal not allowed");
                }
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Persisted reportsharing.json invalid ({Errors}); appsettings values kept.", string.Join("; ", errors));
                    return;
                }
                // Non-null persisted fields override appsettings (same precedence as Steps 2-5).
                _settings.EndpointEnabled = persisted.EndpointEnabled;
                if (!string.IsNullOrWhiteSpace(persisted.PathToFolder)) _settings.PathToFolder = persisted.PathToFolder;
                _settings.ReportExpirationTime = persisted.ReportExpirationTime;
                _settings.MaxSizeOfFolder = persisted.MaxSizeOfFolder;
                _settings.AutoDeletingEnabled = persisted.AutoDeletingEnabled;
                _logger.LogInformation("ReportSharing settings loaded from {Path}.", SettingsFilePath);
            }
        }

        // ---- Report lifecycle ----

        private string PayloadPath(string reportId) => System.IO.Path.Combine(ReportsFolderPath, $"{reportId}.json");

        /// <summary>Result of SaveAsync so the controller can map it to 201/404/507.</summary>
        public enum SaveResult { Saved, EndpointDisabled, QuotaExceeded, InvalidPayload }

        /// <summary>
        /// Saves a new report (spec §6.3.1). Always creates a new GUID (no overwrite).
        /// payloadJson must be valid JSON (opaque blob — structure not validated).
        /// Quota (spec §6.5): 0 = unlimited; AutoDeletingEnabled evicts oldest until the
        /// new report fits, otherwise 507.
        /// </summary>
        public (SaveResult Result, ReportMetadata? Metadata) Save(string name, string owner, string payloadJson)
        {
            lock (_sync)
            {
                if (!_settings.EndpointEnabled)
                {
                    return (SaveResult.EndpointDisabled, null);
                }
                // Opaque blob: only JSON validity is checked (structure not validated).
                JToken? reportToken;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                    reportToken = JToken.Parse(payloadJson);
                }
                catch
                {
                    return (SaveResult.InvalidPayload, null);
                }
                var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(payloadJson);

                if (_settings.MaxSizeOfFolder > 0)
                {
                    var quotaBytes = _settings.MaxSizeOfFolder * 1024L * 1024L;
                    var current = FolderSizeBytes();
                    if (current + sizeBytes > quotaBytes)
                    {
                        if (_settings.AutoDeletingEnabled)
                        {
                            // Evict oldest until the new report fits (spec §6.5.3).
                            foreach (var old in ListMetadataNoLock().OrderBy(m => m.CreatedAt).ToList())
                            {
                                if (current + sizeBytes <= quotaBytes)
                                {
                                    break;
                                }
                                current -= old.SizeBytes;
                                DeleteNoLock(old.ReportId);
                                _logger.LogInformation("ReportSharing quota eviction: deleted oldest report {ReportId} ('{Name}').", old.ReportId, old.Name);
                            }
                            if (current + sizeBytes > quotaBytes)
                            {
                                return (SaveResult.QuotaExceeded, null);
                            }
                        }
                        else
                        {
                            // Auto-Delete disabled: save fails (spec §6.5.2).
                            return (SaveResult.QuotaExceeded, null);
                        }
                    }
                }

                var reportId = Guid.NewGuid().ToString();
                var meta = new ReportMetadata
                {
                    ReportId = reportId,
                    Name = name,
                    Owner = owner,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = _settings.ReportExpirationTime > 0
                        ? DateTime.UtcNow.AddMinutes(_settings.ReportExpirationTime)
                        : null,
                    SizeBytes = sizeBytes
                };
                // Single JSON file per report (user direction): metadata + client-side
                // report in one {reportId}.json — no separate .meta.json sidecar.
                var file = new ReportFile
                {
                    ReportId = meta.ReportId,
                    Name = meta.Name,
                    Owner = meta.Owner,
                    CreatedAt = meta.CreatedAt,
                    ExpiresAt = meta.ExpiresAt,
                    SizeBytes = meta.SizeBytes,
                    Report = reportToken
                };
                var store = new AtomicJsonStore(LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
                store.Save(PayloadPath(reportId), file);
                _logger.LogInformation("Report saved: {ReportId} ('{Name}', {SizeBytes} bytes, expires {Expires}).",
                    meta.ReportId, meta.Name, meta.SizeBytes, meta.ExpiresAt?.ToString("O") ?? "never");
                return (SaveResult.Saved, meta);
            }
        }

        public enum GetResult { Ok, NotFound, Expired, EndpointDisabled }

        /// <summary>Retrieves a report payload (spec §6.3.2); expired → 410 (spec §6.4.3).</summary>
        public (GetResult Result, ReportMetadata? Metadata, string? Payload) Get(string reportId)
        {
            lock (_sync)
            {
                if (!_settings.EndpointEnabled)
                {
                    return (GetResult.EndpointDisabled, null, null);
                }
                var file = ReadReportFile(reportId);
                if (file == null)
                {
                    return (GetResult.NotFound, null, null);
                }
                if (file.ExpiresAt.HasValue && file.ExpiresAt.Value < DateTime.UtcNow)
                {
                    return (GetResult.Expired, ToMetadata(file), null);
                }
                var payload = file.Report?.ToString(Formatting.None) ?? "{}";
                return (GetResult.Ok, ToMetadata(file), payload);
            }
        }

        /// <summary>Lists report metadata (spec §6.3.3).</summary>
        public List<ReportMetadata> List()
        {
            lock (_sync)
            {
                return EndpointEnabled ? ListMetadataNoLock() : new List<ReportMetadata>();
            }
        }

        public enum DeleteResult { Deleted, NotFound, EndpointDisabled }

        public DeleteResult Delete(string reportId)
        {
            lock (_sync)
            {
                if (!_settings.EndpointEnabled)
                {
                    return DeleteResult.EndpointDisabled;
                }
                if (!File.Exists(PayloadPath(reportId)))
                {
                    return DeleteResult.NotFound;
                }
                DeleteNoLock(reportId);
                return DeleteResult.Deleted;
            }
        }

        // ---- Sweep (spec §6.4/§6.6, called by ReportSweepWorker) ----

        /// <summary>
        /// One cleanup pass: (1) expiration — delete every expired report REGARDLESS of
        /// AutoDeletingEnabled; (2) quota — when MaxSizeOfFolder &gt; 0 AND
        /// AutoDeletingEnabled, evict oldest until under the limit.
        /// </summary>
        public (int ExpiredDeleted, int QuotaEvicted) SweepOnce()
        {
            lock (_sync)
            {
                if (!_settings.EndpointEnabled)
                {
                    return (0, 0);
                }
                Directory.CreateDirectory(ReportsFolderPath);
                var expiredDeleted = 0;
                foreach (var meta in ListMetadataNoLock())
                {
                    if (meta.ExpiresAt.HasValue && meta.ExpiresAt.Value < DateTime.UtcNow)
                    {
                        DeleteNoLock(meta.ReportId);
                        expiredDeleted++;
                    }
                }
                var quotaEvicted = 0;
                if (_settings.AutoDeletingEnabled && _settings.MaxSizeOfFolder > 0)
                {
                    var quotaBytes = _settings.MaxSizeOfFolder * 1024L * 1024L;
                    foreach (var old in ListMetadataNoLock().OrderBy(m => m.CreatedAt).ToList())
                    {
                        if (FolderSizeBytes() <= quotaBytes)
                        {
                            break;
                        }
                        DeleteNoLock(old.ReportId);
                        quotaEvicted++;
                        _logger.LogInformation("ReportSharing quota sweep: evicted oldest report {ReportId} ('{Name}').", old.ReportId, old.Name);
                    }
                }
                if (expiredDeleted > 0 || quotaEvicted > 0)
                {
                    _logger.LogInformation("ReportSharing sweep: {Expired} expired deleted, {Evicted} quota-evicted.", expiredDeleted, quotaEvicted);
                }
                return (expiredDeleted, quotaEvicted);
            }
        }

        // ---- helpers ----

        private List<ReportMetadata> ListMetadataNoLock()
        {
            var result = new List<ReportMetadata>();
            if (!Directory.Exists(ReportsFolderPath))
            {
                return result;
            }
            var store = new AtomicJsonStore(LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
            foreach (var file in Directory.EnumerateFiles(ReportsFolderPath, "*.json"))
            {
                if (file.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // legacy sidecar files are ignored (single-file format now)
                }
                var report = store.Load<ReportFile>(file, new ReportFile());
                if (report != null && !string.IsNullOrEmpty(report.ReportId))
                {
                    result.Add(ToMetadata(report));
                }
            }
            return result;
        }

        private ReportFile? ReadReportFile(string reportId)
        {
            var path = PayloadPath(reportId);
            if (!File.Exists(path))
            {
                return null;
            }
            var store = new AtomicJsonStore(LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
            return store.Load<ReportFile>(path, new ReportFile());
        }

        private static ReportMetadata ToMetadata(ReportFile file) => new()
        {
            ReportId = file.ReportId,
            Name = file.Name,
            Owner = file.Owner,
            CreatedAt = file.CreatedAt,
            ExpiresAt = file.ExpiresAt,
            SizeBytes = file.SizeBytes
        };

        private void DeleteNoLock(string reportId)
        {
            var path = PayloadPath(reportId);
            if (File.Exists(path))
            {
                try { File.Delete(path); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete report file '{Path}'.", path);
                }
            }
        }

        /// <summary>Total size in bytes of all report files in the reports folder.</summary>
        public long FolderSizeBytes()
        {
            if (!Directory.Exists(ReportsFolderPath))
            {
                return 0;
            }
            return Directory.EnumerateFiles(ReportsFolderPath).Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            });
        }
    }
}
