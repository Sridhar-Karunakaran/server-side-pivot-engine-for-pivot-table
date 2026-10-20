using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Manages HTTPS settings (spec §4): validation, persistence to {DataFolder}/https.json
    /// (password never persisted), and merge-patch application. All changes require restart —
    /// the Kestrel listener (certificate, protocols) is fixed at startup.
    /// </summary>
    public class HttpsSettingsService
    {
        private const string FileName = "https.json";

        private readonly HttpsSettings _settings;
        private readonly AtomicJsonStore _store;
        private readonly GeneralSettingsService _general;
        private readonly CertificateService _certificates;
        private readonly ILogger<HttpsSettingsService> _logger;
        private readonly object _sync = new();

        public HttpsSettingsService(HttpsSettings settings, AtomicJsonStore store,
            GeneralSettingsService general, CertificateService certificates,
            ILogger<HttpsSettingsService> logger)
        {
            _settings = settings;
            _store = store;
            _general = general;
            _certificates = certificates;
            _logger = logger;
        }

        public HttpsSettings Current
        {
            get
            {
                lock (_sync)
                {
                    return _settings.Sanitized();
                }
            }
        }

        public string SettingsFilePath => Path.Combine(_general.DataFolderPath, FileName);

        /// <summary>Settings as configured (including in-memory PFX password, never persisted).</summary>
        public HttpsSettings Effective => _settings;

        /// <summary>Merge-patch: applies only provided values, validates, persists (sanitized).</summary>
        public (HttpsSettings Settings, bool RequiresRestart, List<string> Errors) ApplyPatch(
            bool? enabled, string? pfxPath, string? pfxPassword, string? pemCertPath, string? pemKeyPath,
            CertificateStoreLocationOption? storeLocation, string? storeName, string? thumbprint, string? subjectName,
            HttpProtocolsOption? protocols, bool? allowInvalidCertificates, bool environmentIsProduction)
        {
            lock (_sync)
            {
                static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
                var candidate = _settings.Sanitized();
                if (enabled.HasValue) candidate.Enabled = enabled.Value;
                // Explicit null in the patch clears the setting (empty string after Deref).
                candidate.PfxPath = pfxPath == null ? candidate.PfxPath : Normalize(pfxPath);
                candidate.PemCertPath = pemCertPath == null ? candidate.PemCertPath : Normalize(pemCertPath);
                candidate.PemKeyPath = pemKeyPath == null ? candidate.PemKeyPath : Normalize(pemKeyPath);
                if (storeLocation.HasValue) candidate.StoreLocation = storeLocation.Value;
                if (storeName != null) candidate.StoreName = storeName;
                candidate.Thumbprint = thumbprint == null ? candidate.Thumbprint : Normalize(thumbprint);
                candidate.SubjectName = subjectName == null ? candidate.SubjectName : Normalize(subjectName);
                if (protocols.HasValue) candidate.Protocols = protocols.Value;
                if (allowInvalidCertificates.HasValue) candidate.AllowInvalidCertificates = allowInvalidCertificates.Value;

                var errors = _certificates.Validate(candidate);
                // Spec §4.4.1: AllowInvalidCertificates is forbidden in Production.
                if (candidate.AllowInvalidCertificates && environmentIsProduction)
                {
                    errors.Add("AllowInvalidCertificates forbidden in Production");
                }
                // Spec §4.4: when invalid certs are NOT allowed, reject a certificate that
                // clients would refuse (untrusted/self-signed → net::ERR_CERT_AUTHORITY_INVALID)
                // instead of persisting it and degrading on the next restart.
                // The unsanitized candidate carries the live PFX password for this check only.
                if (errors.Count == 0 && candidate.Enabled)
                {
                    var candidateWithSecrets = candidate;
                    if (pfxPassword != null) candidateWithSecrets.PfxPassword = Normalize(pfxPassword);
                    errors.AddRange(_certificates.TryResolveValidatedCertificate(candidateWithSecrets).Errors);
                }
                if (errors.Count > 0)
                {
                    return (Current, false, errors);
                }

                // Apply onto the live settings object; PFX password is memory-only.
                _settings.Enabled = candidate.Enabled;
                _settings.PfxPath = candidate.PfxPath;
                _settings.PemCertPath = candidate.PemCertPath;
                _settings.PemKeyPath = candidate.PemKeyPath;
                _settings.StoreLocation = candidate.StoreLocation;
                _settings.StoreName = candidate.StoreName;
                _settings.Thumbprint = candidate.Thumbprint;
                _settings.Protocols = candidate.Protocols;
                _settings.AllowInvalidCertificates = candidate.AllowInvalidCertificates;
                _settings.PfxPassword = pfxPassword != null ? Normalize(pfxPassword) : _settings.PfxPassword;

                _store.Save(SettingsFilePath, _settings.Sanitized());
                if (_settings.Enabled)
                {
                    _logger.LogWarning("HTTPS settings updated; requires restart to take effect (certificate/protocols bind at startup).");
                }
                else
                {
                    _logger.LogInformation("HTTPS settings saved (disabled).");
                }
                return (Current, RequiresRestart: true, errors);
            }
        }

        /// <summary>Bootstrap: loads persisted settings from {DataFolder}/https.json.</summary>
        public void LoadPersisted()
        {
            lock (_sync)
            {
                if (!File.Exists(SettingsFilePath))
                {
                    _logger.LogInformation("No persisted https.json; appsettings.json values in effect.");
                    return;
                }
                var persisted = _store.Load<HttpsSettings>(SettingsFilePath, new HttpsSettings());
                var errors = _certificates.Validate(persisted);
                if (errors.Count > 0)
                {
                    _logger.LogWarning("Persisted https.json invalid ({Errors}); appsettings defaults kept.", string.Join("; ", errors));
                    return;
                }
                _settings.Enabled = persisted.Enabled;
                _settings.PfxPath = persisted.PfxPath;
                _settings.PemCertPath = persisted.PemCertPath;
                _settings.PemKeyPath = persisted.PemKeyPath;
                _settings.StoreLocation = persisted.StoreLocation;
                _settings.StoreName = persisted.StoreName;
                _settings.Thumbprint = persisted.Thumbprint;
                _settings.Protocols = persisted.Protocols;
                _settings.AllowInvalidCertificates = persisted.AllowInvalidCertificates;
                _logger.LogInformation("HTTPS settings loaded from {Path}.", SettingsFilePath);
            }
        }
    }
}