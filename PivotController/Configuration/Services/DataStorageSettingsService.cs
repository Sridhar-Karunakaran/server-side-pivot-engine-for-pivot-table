using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Manages Data Storage settings (spec §2): validation, persistence to
    /// {DataFolder}/datastorage.json, and runtime application. RetryAttempts and
    /// KeepDataOnRefresh apply immediately; CacheSizeLimit and MaxCacheItemSizeBytes
    /// require restart (they configure MemoryCache at startup).
    /// </summary>
    public class DataStorageSettingsService
    {
        private const string FileName = "datastorage.json";

        private readonly DataStorageSettings _settings;
        private readonly AtomicJsonStore _store;
        private readonly GeneralSettingsService _general;
        private readonly ILogger<DataStorageSettingsService> _logger;
        private readonly object _sync = new();

        public DataStorageSettingsService(DataStorageSettings settings, AtomicJsonStore store,
            GeneralSettingsService general, ILogger<DataStorageSettingsService> logger)
        {
            _settings = settings;
            _store = store;
            _general = general;
            _logger = logger;
        }

        public DataStorageSettings Current
        {
            get
            {
                lock (_sync)
                {
                    return new DataStorageSettings
                    {
                        RetryAttempts = _settings.RetryAttempts,
                        CacheSizeLimit = _settings.CacheSizeLimit,
                        KeepDataOnRefresh = _settings.KeepDataOnRefresh,
                        MaxCacheItemSizeBytes = _settings.MaxCacheItemSizeBytes
                    };
                }
            }
        }

        public string SettingsFilePath => Path.Combine(_general.DataFolderPath, FileName);

        /// <summary>
        /// Merge-patch: applies provided values, persists, applies runtime-live settings,
        /// and reports whether a restart is required (CacheSizeLimit/MaxCacheItemSizeBytes).
        /// </summary>
        public (DataStorageSettings Settings, bool RequiresRestart, List<string> Errors) ApplyPatch(
            int? retryAttempts, int? cacheSizeLimit, bool? keepDataOnRefresh, long? maxCacheItemSizeBytes)
        {
            var candidate = Current;
            if (retryAttempts.HasValue) candidate.RetryAttempts = retryAttempts.Value;
            if (cacheSizeLimit.HasValue) candidate.CacheSizeLimit = cacheSizeLimit.Value;
            if (keepDataOnRefresh.HasValue) candidate.KeepDataOnRefresh = keepDataOnRefresh.Value;
            if (maxCacheItemSizeBytes.HasValue) candidate.MaxCacheItemSizeBytes = maxCacheItemSizeBytes.Value;

            var errors = candidate.Validate();
            if (errors.Count > 0)
            {
                return (Current, false, errors);
            }

            bool requiresRestart = false;
            lock (_sync)
            {
                if (candidate.RetryAttempts != _settings.RetryAttempts)
                {
                    _settings.RetryAttempts = candidate.RetryAttempts;
                    _logger.LogInformation("DataStorage RetryAttempts set to {RetryAttempts} (applied immediately).", _settings.RetryAttempts);
                }
                if (candidate.CacheSizeLimit != _settings.CacheSizeLimit)
                {
                    _settings.CacheSizeLimit = candidate.CacheSizeLimit;
                    requiresRestart = true;
                    _logger.LogWarning("DataStorage CacheSizeLimit set to {Limit}; requires restart to take effect.", _settings.CacheSizeLimit);
                }
                if (candidate.MaxCacheItemSizeBytes != _settings.MaxCacheItemSizeBytes)
                {
                    _settings.MaxCacheItemSizeBytes = candidate.MaxCacheItemSizeBytes;
                    requiresRestart = true;
                    _logger.LogWarning("DataStorage MaxCacheItemSizeBytes set to {Bytes}; requires restart to take effect.", _settings.MaxCacheItemSizeBytes);
                }
                if (candidate.KeepDataOnRefresh != _settings.KeepDataOnRefresh)
                {
                    _settings.KeepDataOnRefresh = candidate.KeepDataOnRefresh;
                    _logger.LogInformation("DataStorage KeepDataOnRefresh set to {Keep} (applied immediately).", _settings.KeepDataOnRefresh);
                }
                PersistNoLock();
            }
            return (Current, requiresRestart, errors);
        }

        /// <summary>
        /// Bootstrap: loads persisted settings from {DataFolder}/datastorage.json so that
        /// "requires restart" changes (CacheSizeLimit/MaxCacheItemSizeBytes) actually take
        /// effect after a restart. appsettings values are defaults; the persisted file wins.
        /// </summary>
        public void LoadPersisted()
        {
            lock (_sync)
            {
                var persisted = _store.Load<DataStorageSettings>(SettingsFilePath, new DataStorageSettings());
                var errors = persisted.Validate();
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Persisted datastorage.json invalid ({Errors}); appsettings defaults kept.", string.Join("; ", errors));
                    return;
                }
                _settings.RetryAttempts = persisted.RetryAttempts;
                _settings.CacheSizeLimit = persisted.CacheSizeLimit;
                _settings.KeepDataOnRefresh = persisted.KeepDataOnRefresh;
                _settings.MaxCacheItemSizeBytes = persisted.MaxCacheItemSizeBytes;
                _logger.LogInformation("DataStorage settings loaded from {Path}.", SettingsFilePath);
            }
        }

        private void PersistNoLock()
        {
            _store.Save(SettingsFilePath, Current);
        }

        /// <summary>Persists current settings to {DataFolder}/datastorage.json (atomic write).</summary>
        public void Persist()
        {
            lock (_sync)
            {
                PersistNoLock();
            }
        }
    }
}