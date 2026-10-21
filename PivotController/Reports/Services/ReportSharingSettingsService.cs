using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;

namespace PivotController.Reports.Services
{
    /// <summary>
    /// Report Sharing settings (spec §6.7): GET/PUT /api/config/reportsharing. All values
    /// except StoragePath apply immediately; StoragePath requires restart.
    /// </summary>
    public class ReportSharingSettingsService
    {
        private const string FileName = "reportsharing.json";

        private readonly ReportSharingSettings _settings;
        private readonly AtomicJsonStore _store;
        private readonly GeneralSettingsService _general;
        private readonly ILogger<ReportSharingSettingsService> _logger;
        private readonly object _sync = new();

        public ReportSharingSettingsService(ReportSharingSettings settings, AtomicJsonStore store,
            GeneralSettingsService general, ILogger<ReportSharingSettingsService> logger)
        {
            _settings = settings;
            _store = store;
            _general = general;
            _logger = logger;
        }

        private string SettingsFilePath => Path.Combine(_general.DataFolderPath, FileName);

        public ReportSharingSettings Current
        {
            get { lock (_sync) return _settings.Clone(); }
        }

        /// <summary>Merge-patch (spec §6.7.2): applies provided values; StoragePath requires restart.</summary>
        public (ReportSharingSettings Settings, bool RequiresRestart, List<string> Errors) ApplyPatch(
            bool? endpointEnabled, string? storagePath, int? reportExpirationTimeMinutes,
            int? maxFolderSizeMB, bool? autoDelete)
        {
            lock (_sync)
            {
                var candidate = _settings.Clone();
                if (endpointEnabled.HasValue) candidate.EndpointEnabled = endpointEnabled.Value;
                if (storagePath != null) candidate.StoragePath = string.IsNullOrWhiteSpace(storagePath) ? null : storagePath;
                if (reportExpirationTimeMinutes.HasValue) candidate.ReportExpirationTimeMinutes = reportExpirationTimeMinutes.Value;
                if (maxFolderSizeMB.HasValue) candidate.MaxFolderSizeMB = maxFolderSizeMB.Value;
                if (autoDelete.HasValue) candidate.AutoDelete = autoDelete.Value;

                var errors = candidate.Validate();
                if (errors.Count > 0)
                {
                    return (Current, false, errors);
                }

                bool requiresRestart = false;
                if (!string.Equals(candidate.StoragePath, _settings.StoragePath, StringComparison.Ordinal))
                {
                    requiresRestart = true;
                    _logger.LogWarning("ReportSharing StoragePath set; requires restart to take effect.");
                }
                _settings.EndpointEnabled = candidate.EndpointEnabled;
                _settings.StoragePath = candidate.StoragePath;
                _settings.ReportExpirationTimeMinutes = candidate.ReportExpirationTimeMinutes;
                _settings.MaxFolderSizeMB = candidate.MaxFolderSizeMB;
                _settings.AutoDelete = candidate.AutoDelete;
                _store.Save(SettingsFilePath, _settings.Clone());
                return (Current, requiresRestart, errors);
            }
        }

        /// <summary>Bootstrap: loads persisted settings from {DataFolder}/reportsharing.json.</summary>
        public void LoadPersisted()
        {
            lock (_sync)
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _logger.LogInformation("No persisted reportsharing.json; appsettings.json values in effect.");
                    return;
                }
                var persisted = _store.Load<ReportSharingSettings>(SettingsFilePath, new ReportSharingSettings());
                var errors = persisted.Validate();
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Persisted reportsharing.json invalid ({Errors}); appsettings defaults kept.", string.Join("; ", errors));
                    return;
                }
                _settings.EndpointEnabled = persisted.EndpointEnabled;
                _settings.StoragePath = persisted.StoragePath;
                _settings.ReportExpirationTimeMinutes = persisted.ReportExpirationTimeMinutes;
                _settings.MaxFolderSizeMB = persisted.MaxFolderSizeMB;
                _settings.AutoDelete = persisted.AutoDelete;
                _logger.LogInformation("ReportSharing settings loaded from {Path}.", SettingsFilePath);
            }
        }
    }
}