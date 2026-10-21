using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Manages Database Query settings (spec §3): validation, persistence to
    /// {DataFolder}/databasequery.json, and runtime application. CommandTimeoutEnabled
    /// and CommandTimeLimit apply immediately (read on every query execution);
    /// no restart is required for either.
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

        public DatabaseQuerySettings Current
        {
            get
            {
                lock (_sync)
                {
                    return new DatabaseQuerySettings
                    {
                        CommandTimeoutEnabled = _settings.CommandTimeoutEnabled,
                        CommandTimeLimit = _settings.CommandTimeLimit
                    };
                }
            }
        }

        public string SettingsFilePath => Path.Combine(_general.DataFolderPath, FileName);

        /// <summary>
        /// Merge-patch: applies provided values, persists, and applies at runtime.
        /// Both options apply immediately (spec §3.2.4); requiresRestart is always false.
        /// </summary>
        public (DatabaseQuerySettings Settings, bool RequiresRestart, List<string> Errors) ApplyPatch(
            bool? commandTimeoutEnabled, int? commandTimeLimit)
        {
            var candidate = Current;
            if (commandTimeoutEnabled.HasValue) candidate.CommandTimeoutEnabled = commandTimeoutEnabled.Value;
            if (commandTimeLimit.HasValue) candidate.CommandTimeLimit = commandTimeLimit.Value;

            var errors = candidate.Validate();
            if (errors.Count > 0)
            {
                return (Current, false, errors);
            }

            lock (_sync)
            {
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
                PersistNoLock();
            }
            return (Current, false, errors);
        }

        /// <summary>
        /// Bootstrap: loads persisted settings from {DataFolder}/databasequery.json so that
        /// API changes survive restarts. appsettings values are defaults; the persisted file wins.
        /// </summary>
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

        private void PersistNoLock()
        {
            _store.Save(SettingsFilePath, Current);
        }

        /// <summary>Persists current settings to {DataFolder}/databasequery.json (atomic write).</summary>
        public void Persist()
        {
            lock (_sync)
            {
                PersistNoLock();
            }
        }
    }
}