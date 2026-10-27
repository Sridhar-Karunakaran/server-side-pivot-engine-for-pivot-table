using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Manages General settings (spec §1): validation, persistence to {DataFolder}/config.json,
    /// live log-level filtering, and data-folder writability checks. Port/DataFolder changes
    /// require restart; MinimumLogLevel applies immediately.
    /// </summary>
    public class GeneralSettingsService
    {
        public const string ConfigFileName = "config.json";

        private readonly GeneralSettings _settings;
        private readonly AtomicJsonStore _store;
        private readonly IWebHostEnvironment _environment;
        private readonly MinimumLevelLoggerProvider _minLevelProvider;
        private readonly ILogger<GeneralSettingsService> _logger;
        private readonly object _sync = new();

        public GeneralSettingsService(GeneralSettings settings, AtomicJsonStore store,
            IWebHostEnvironment environment, ILogger<GeneralSettingsService> logger,
            MinimumLevelLoggerProvider minLevelProvider)
        {
            _settings = settings;
            _store = store;
            _environment = environment;
            _logger = logger;
            _minLevelProvider = minLevelProvider;
            minLevelProvider.MinimumLevel = GeneralSettings.ToAspNetLogLevel(settings.MinimumLogLevel);
        }

        public GeneralSettings Current
        {
            get
            {
                lock (_sync)
                {
                    return new GeneralSettings
                    {
                        Port = _settings.Port,
                        MinimumLogLevel = _settings.MinimumLogLevel,
                        DataFolder = _settings.DataFolder
                    };
                }
            }
        }

        public string ConfigFilePath => System.IO.Path.Combine(_environment.ContentRootPath, _settings.DataFolder, ConfigFileName);
        public string DataFolderPath => System.IO.Path.Combine(_environment.ContentRootPath, _settings.DataFolder);

        /// <summary>
        /// Validates a merge-patch against spec §1 rules. Returns error list; empty means valid.
        /// Caller passes only the properties present in the patch body.
        /// </summary>
        public List<string> ValidatePatch(int? port, string? minimumLogLevel, string? dataFolder)
        {
            var errors = new List<string>();
            if (port.HasValue && (port.Value < 1 || port.Value > 65535))
            {
                errors.Add("Port must be between 1 and 65535");
            }
            if (minimumLogLevel != null)
            {
                if (!Enum.TryParse<LogLevelOption>(minimumLogLevel, ignoreCase: true, out var level)
                    || !Enum.IsDefined(level))
                {
                    errors.Add("MinimumLogLevel must be one of: Info, Warn, Error, Fatal");
                }
            }
            if (dataFolder != null)
            {
                if (string.IsNullOrWhiteSpace(dataFolder))
                {
                    errors.Add("DataFolder must be a non-empty folder name");
                }
                else if (GeneralSettings.IsPathTraversal(dataFolder, _environment.ContentRootPath))
                {
                    errors.Add("DataFolder path traversal not allowed");
                }
            }
            return errors;
        }

        /// <summary>
        /// Merge-patch: applies provided values, persists to config.json, applies log level
        /// immediately, and reports whether a restart is required (Port/DataFolder changes).
        /// </summary>
        public (GeneralSettings Settings, bool RequiresRestart) ApplyPatch(int? port, string? minimumLogLevel, string? dataFolder)
        {
            bool requiresRestart = false;
            lock (_sync)
            {
                if (port.HasValue && port.Value != _settings.Port)
                {
                    Console.WriteLine($"[General] Port changed {_settings.Port} → {port.Value}. Takes effect after restart (requiresRestart: true).");
                    _settings.Port = port.Value;
                    requiresRestart = true;
                }
                if (minimumLogLevel != null
                    && Enum.TryParse<LogLevelOption>(minimumLogLevel, ignoreCase: true, out var level)
                    && Enum.IsDefined(level))
                {
                    if (_settings.MinimumLogLevel != level)
                    {
                        // Console.WriteLine (not _logger): the change confirmation must be
                        // visible even when the previous MinimumLogLevel would filter an
                        // Information entry (e.g. Warn → Error), so the user can confirm
                        // the switch in the local console/terminal immediately.
                        Console.WriteLine($"[General] MinimumLogLevel changed {_settings.MinimumLogLevel} → {level}. Applied immediately — messages below {level} are now suppressed.");
                        _settings.MinimumLogLevel = level;
                        ApplyLogLevel(level);
                    }
                }
                if (dataFolder != null && !string.IsNullOrWhiteSpace(dataFolder)
                    && !GeneralSettings.IsPathTraversal(dataFolder, _environment.ContentRootPath)
                    && !string.Equals(_settings.DataFolder, dataFolder, StringComparison.OrdinalIgnoreCase))
                {
                    _settings.DataFolder = dataFolder;
                    requiresRestart = true;
                }
                PersistNoLock();
            }
            return (Current, requiresRestart);
        }

        /// <summary>
        /// Applies the minimum log level to the logging pipeline immediately (no restart,
        /// spec §1.3): the MinimumLevelLoggerProvider filters out lower-severity messages
        /// as soon as the level changes.
        /// </summary>
        public void ApplyLogLevel(LogLevelOption level)
        {
            _minLevelProvider.MinimumLevel = GeneralSettings.ToAspNetLogLevel(level);
        }

        /// <summary>Ensures the data folder and standard subdirectories exist and are writable.</summary>
        public void EnsureDataFolders()
        {
            var root = DataFolderPath;
            System.IO.Directory.CreateDirectory(root);
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "reports"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "logs"));
            // Writability probe per spec §1.4.3.
            var probe = System.IO.Path.Combine(root, ".write-probe");
            System.IO.File.WriteAllText(probe, "ok");
            System.IO.File.Delete(probe);
        }

        /// <summary>Persists current settings to {DataFolder}/config.json (atomic write).</summary>
        public void Persist()
        {
            lock (_sync)
            {
                PersistNoLock();
            }
        }

        private void PersistNoLock()
        {
            _store.Save(ConfigFilePath, new GeneralSettingsConfigFile
            {
                Port = _settings.Port,
                MinimumLogLevel = _settings.MinimumLogLevel.ToString(),
                DataFolder = _settings.DataFolder
            });
        }

        /// <summary>File shape persisted to config.json (camelCase for external tooling).</summary>
        public class GeneralSettingsConfigFile
        {
            public int Port { get; set; }
            public string MinimumLogLevel { get; set; } = "Info";
            public string DataFolder { get; set; } = "DataSource";
        }
    }

    /// <summary>Implemented by logger providers that support live minimum-level changes.</summary>
    public interface IConfigurationLoggerLevelFilter
    {
        void SetMinimumLevel(LogLevel level);
    }
}