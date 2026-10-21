using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;

namespace PivotController.Security.Services
{
    /// <summary>
    /// Security settings (spec §5): Basic Auth toggle and CORS origins. Persisted to
    /// {DataFolder}/security.json. Both settings apply immediately (no restart).
    /// </summary>
    public class SecuritySettingsService
    {
        private const string FileName = "security.json";

        private readonly SecuritySettings _settings;
        private readonly AtomicJsonStore _store;
        private readonly GeneralSettingsService _general;
        private readonly ILogger<SecuritySettingsService> _logger;
        private readonly object _sync = new();

        public SecuritySettingsService(SecuritySettings settings, AtomicJsonStore store,
            GeneralSettingsService general, ILogger<SecuritySettingsService> logger)
        {
            _settings = settings;
            _store = store;
            _general = general;
            _logger = logger;
        }

        private string SettingsFilePath => Path.Combine(_general.DataFolderPath, FileName);

        public SecuritySettings Current
        {
            get
            {
                lock (_sync) return _settings.Clone();
            }
        }

        /// <summary>Merge-patch (spec §5.5.2): applies provided values immediately, persists.</summary>
        public (SecuritySettings Settings, List<string> Errors) ApplyPatch(bool? basicAuthEnabled, List<string>? allowedOrigins)
        {
            lock (_sync)
            {
                var candidate = _settings.Clone();
                if (basicAuthEnabled.HasValue) candidate.BasicAuthEnabled = basicAuthEnabled.Value;
                if (allowedOrigins != null) candidate.AllowedOrigins = new List<string>(allowedOrigins);

                var errors = candidate.Validate();
                if (errors.Count > 0)
                {
                    return (Current, errors);
                }

                if (candidate.BasicAuthEnabled != _settings.BasicAuthEnabled)
                {
                    _settings.BasicAuthEnabled = candidate.BasicAuthEnabled;
                    _logger.LogInformation("Security BasicAuthEnabled set to {Enabled} (applied immediately).", _settings.BasicAuthEnabled);
                }
                if (!candidate.AllowedOrigins.SequenceEqual(_settings.AllowedOrigins))
                {
                    _settings.AllowedOrigins = candidate.AllowedOrigins;
                    _logger.LogInformation("Security AllowedOrigins updated (applied immediately).");
                }
                _store.Save(SettingsFilePath, _settings.Clone());
                return (Current, errors);
            }
        }

        /// <summary>Bootstrap: loads persisted settings from {DataFolder}/security.json.</summary>
        public void LoadPersisted()
        {
            lock (_sync)
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _logger.LogInformation("No persisted security.json; appsettings.json values in effect.");
                    return;
                }
                var persisted = _store.Load<SecuritySettings>(SettingsFilePath, new SecuritySettings());
                var errors = persisted.Validate();
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Persisted security.json invalid ({Errors}); appsettings defaults kept.", string.Join("; ", errors));
                    return;
                }
                _settings.BasicAuthEnabled = persisted.BasicAuthEnabled;
                _settings.AllowedOrigins = persisted.AllowedOrigins;
                _logger.LogInformation("Security settings loaded from {Path}.", SettingsFilePath);
            }
        }
    }
}