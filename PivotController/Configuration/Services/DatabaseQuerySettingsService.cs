using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Database Query settings (spec §3): command timeout. Applies immediately via the
    /// provider reading the live settings object. Persisted to {DataFolder}/databasequery.json.
    /// </summary>
    public class DatabaseQuerySettingsService
    {
        private const string FileName = "databasequery.json";

        private readonly DatabaseQuerySettings _settings;
        private readonly AtomicJsonStore _store;
        private readonly GeneralSettingsService _general;
        private readonly ILogger<DatabaseQuerySettingsService> _logger;
        private readonly object _sync = new();

        public DatabaseQuerySettingsService(DatabaseQuerySettings settings, AtomicJsonStore store,
            GeneralSettingsService general, ILogger<DatabaseQuerySettingsService> logger)
        {
            _settings = settings;
            _store = store;
            _general = general;
            _logger = logger;
        }

        private string SettingsFilePath => Path.Combine(_general.DataFolderPath, FileName);

        public DatabaseQuerySettings Current
        {
            get { lock (_sync) return _settings.Clone(); }
        }

        /// <summary>Merge-patch (spec §3.3.2/§3.2.4): applies immediately, persists.</summary>
        public (DatabaseQuerySettings Settings, List<string> Errors) ApplyPatch(bool? commandTimeoutEnabled, int? commandTimeLimit)
        {
            lock (_sync)
            {
                var candidate = _settings.Clone();
                if (commandTimeoutEnabled.HasValue) candidate.CommandTimeoutEnabled = commandTimeoutEnabled.Value;
                if (commandTimeLimit.HasValue) candidate.CommandTimeLimit = commandTimeLimit.Value;

                var errors = candidate.Validate();
                if (errors.Count > 0)
                {
                    return (Current, errors);
                }

                if (candidate.CommandTimeoutEnabled != _settings.CommandTimeoutEnabled)
                {
                    _settings.CommandTimeoutEnabled = candidate.CommandTimeoutEnabled;
                    _logger.LogInformation("DatabaseQuery CommandTimeoutEnabled set to {Enabled} (applied immediately).", _settings.CommandTimeoutEnabled);
                }
                if (candidate.CommandTimeLimit != _settings.CommandTimeLimit)
                {
                    _settings.CommandTimeLimit = candidate.CommandTimeLimit;
                    _logger.LogInformation("DatabaseQuery CommandTimeLimit set to {Limit}s (applied immediately).", _settings.CommandTimeLimit);
                }
                _store.Save(SettingsFilePath, _settings.Clone());
                return (Current, errors);
            }
        }

        /// <summary>Bootstrap: loads persisted settings from {DataFolder}/databasequery.json.</summary>
        public void LoadPersisted()
        {
            lock (_sync)
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _logger.LogInformation("No persisted databasequery.json; appsettings.json values in effect.");
                    return;
                }
                var persisted = _store.Load<DatabaseQuerySettings>(SettingsFilePath, new DatabaseQuerySettings());
                var errors = persisted.Validate();
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Persisted databasequery.json invalid ({Errors}); appsettings defaults kept.", string.Join("; ", errors));
                    return;
                }
                _settings.CommandTimeoutEnabled = persisted.CommandTimeoutEnabled;
                _settings.CommandTimeLimit = persisted.CommandTimeLimit;
                _logger.LogInformation("DatabaseQuery settings loaded from {Path}.", SettingsFilePath);
            }
        }
    }
}