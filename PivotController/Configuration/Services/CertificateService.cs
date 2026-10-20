using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Resolves the server certificate from exactly one configured source (spec §4):
    /// PFX file, PEM cert+key pair, or (Windows-only) certificate store lookup by thumbprint.
    /// </summary>
    public class CertificateService
    {
        private readonly ILogger<CertificateService> _logger;

        public CertificateService(ILogger<CertificateService> logger)
        {
            _logger = logger;
        }

        /// <summary>Validates settings without loading (for PUT-time checks). Empty list = valid.</summary>
        public List<string> Validate(HttpsSettings settings)
        {
            var errors = new List<string>();
            if (!settings.Enabled)
            {
                return errors;
            }
            if (settings.ConfiguredSourceCount == 0)
            {
                errors.Add("HTTPS enabled but no certificate source configured (pfxPath, pemCertPath/pemKeyPath, or thumbprint).");
                return errors;
            }
            if (settings.ConfiguredSourceCount > 1)
            {
                errors.Add("Exactly one certificate source must be configured (PFX, PEM, or store); found " + settings.ConfiguredSourceCount + ".");
                return errors;
            }
            if (!string.IsNullOrWhiteSpace(settings.PfxPath) && !File.Exists(settings.PfxPath))
            {
                errors.Add($"PFX file not found: {settings.PfxPath}");
            }
            if (!string.IsNullOrWhiteSpace(settings.PemCertPath))
            {
                if (!File.Exists(settings.PemCertPath)) errors.Add($"PEM certificate file not found: {settings.PemCertPath}");
                if (string.IsNullOrWhiteSpace(settings.PemKeyPath)) errors.Add("pemKeyPath is required when pemCertPath is set.");
                else if (!File.Exists(settings.PemKeyPath)) errors.Add($"PEM key file not found: {settings.PemKeyPath}");
            }
            if (!string.IsNullOrWhiteSpace(settings.Thumbprint) && !OperatingSystem.IsWindows())
            {
                errors.Add("Certificate store lookup is only supported on Windows; use pfxPath or pemCertPath.");
            }
            return errors;
        }

        /// <summary>Loads the certificate; returns null when disabled or no source configured (spec §4.2.1).</summary>
        public X509Certificate2? ResolveCertificate(HttpsSettings settings)
        {
            if (!settings.Enabled)
            {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(settings.PfxPath))
            {
                var cert = string.IsNullOrEmpty(settings.PfxPassword)
                    ? new X509Certificate2(settings.PfxPath)
                    : new X509Certificate2(settings.PfxPath, settings.PfxPassword);
                _logger.LogInformation("HTTPS certificate loaded from PFX {Path}.", settings.PfxPath);
                return cert;
            }
            if (!string.IsNullOrWhiteSpace(settings.PemCertPath) && !string.IsNullOrWhiteSpace(settings.PemKeyPath))
            {
                var cert = X509Certificate2.CreateFromPemFile(settings.PemCertPath, settings.PemKeyPath);
                _logger.LogInformation("HTTPS certificate loaded from PEM {Path}.", settings.PemCertPath);
                return cert;
            }
            if (!string.IsNullOrWhiteSpace(settings.Thumbprint) && OperatingSystem.IsWindows())
            {
                var storeLocation = settings.StoreLocation == CertificateStoreLocationOption.LocalMachine
                    ? System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine
                    : System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser;
                using var store = new X509Store(settings.StoreName, storeLocation);
                store.Open(OpenFlags.ReadOnly);
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, settings.Thumbprint, validOnly: false);
                if (found.Count == 0)
                {
                    throw new InvalidOperationException($"Certificate with thumbprint '{settings.Thumbprint}' not found in store {storeLocation}/{settings.StoreName}.");
                }
                _logger.LogInformation("HTTPS certificate loaded from Windows store {Location}/{Store} by thumbprint.", storeLocation, settings.StoreName);
                return found[0];
            }
            throw new InvalidOperationException("HTTPS enabled but no certificate source configured.");
        }

        /// <summary>Maps Protocols option onto Kestrel's HttpProtocols enum.</summary>
        public static Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols MapProtocols(HttpProtocolsOption option) => option switch
        {
            HttpProtocolsOption.HTTP1 => Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1,
            HttpProtocolsOption.HTTP2 => Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2,
            _ => Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2
        };
    }
}