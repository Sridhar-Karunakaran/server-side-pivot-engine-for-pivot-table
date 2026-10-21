using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Security settings (spec §5): Authorization.Enabled + CORS.AllowOrigin. Persisted to
    /// {DataFolder}/security.json. Both apply immediately (auth middleware reads the live
    /// settings object). Credentials come from the Data Server configuration (appsettings
    /// Security:Authorization) — not settable via the API (Admin Panel later).
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
            get { lock (_sync) return _settings.Sanitized(); }
        }

        public SecuritySettingsService SetPersistedFilePath(string path)
        {
            _persistedPath = path;
            return this;
        }
        private string? _persistedPath;

        /// <summary>Merge-patch (spec §5.4.2): applies provided values immediately, persists.</summary>
        public (SecuritySettings Settings, List<string> Errors) ApplyPatch(bool? authorizationEnabled, string? allowOrigin)
        {
            lock (_sync)
            {
                var candidate = _settings.Clone();
                if (authorizationEnabled.HasValue) candidate.Authorization.Enabled = authorizationEnabled.Value;
                if (allowOrigin != null) candidate.CORS.AllowOrigin = allowOrigin;

                var errors = candidate.Validate();
                if (errors.Count > 0)
                {
                    return (Current, errors);
                }

                bool changed = candidate.Authorization.Enabled != _settings.Authorization.Enabled
                    || !string.Equals(candidate.CORS.AllowOrigin, _settings.CORS.AllowOrigin, StringComparison.OrdinalIgnoreCase);
                _settings.Authorization.Enabled = candidate.Authorization.Enabled;
                _settings.CORS.AllowOrigin = candidate.CORS.AllowOrigin;

                _store.Save(SettingsFilePath, new
                {
                    Authorization = new { _settings.Authorization.Enabled },
                    CORS = new { AllowOrigin = _settings.CORS.AllowOrigin }
                });
                if (changed)
                {
                    _logger.LogInformation("Security settings updated (applied immediately): authorization.enabled={Enabled}, cors.allowOrigin={Origin}.",
                        _settings.Authorization.Enabled, _settings.CORS.AllowOrigin);
                }
                return (Current, errors);
            }
        }

        /// <summary>Bootstrap: loads persisted settings from {DataFolder}/security.json.</summary>
        public void LoadPersisted(string dataFolderPath)
        {
            lock (_sync)
            {
                var path = _persistedPath ?? Path.Combine(dataFolderPath, FileName);
                if (!File.Exists(path))
                {
                    _logger.LogInformation("No persisted security.json; appsettings.json Security values in effect.");
                    return;
                }
                var persisted = _store.Load<SecuritySettings>(path, new SecuritySettings());
                // Only the persisted fields are validated: credentials stay in appsettings
                // (never written to disk), so a persisted enabled:true without username/
                // password here is expected, not an error.
                if (persisted.CORS == null || string.IsNullOrWhiteSpace(persisted.CORS.AllowOrigin))
                {
                    _logger.LogWarning("Persisted security.json invalid (CORS:AllowOrigin missing); appsettings Security values kept.");
                    return;
                }
                if (persisted.Authorization.Enabled && !_settings.IsAuthEnabled)
                {
                    _logger.LogWarning("Persisted security.json enables Authorization but no credentials are configured in appsettings (Security:Authorization Username/Password); appsettings enabled state kept.");
                }
                else
                {
                    _settings.Authorization.Enabled = persisted.Authorization.Enabled;
                }
                _settings.CORS.AllowOrigin = persisted.CORS.AllowOrigin;
                _logger.LogInformation("Security settings (authorization.enabled, cors.allowOrigin) loaded from {Path}.", path);
            }
        }
    }
}