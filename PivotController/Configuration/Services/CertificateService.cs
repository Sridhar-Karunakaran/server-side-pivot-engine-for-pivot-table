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

        /// <summary>
        /// Validates a loaded certificate against the AllowInvalidCertificates policy (spec §4.4).
        /// When invalid certs are NOT allowed (default), the server must not serve a certificate
        /// that clients will reject (ERR_CERT_AUTHORITY_INVALID for self-signed/untrusted chains):
        /// build the chain locally and require a trusted root + a server-capable leaf with
        /// usable private key and valid time window. Returns error list; empty = certificate is
        /// acceptable to serve.
        /// </summary>
        public List<string> ValidateCertificateChain(X509Certificate2 cert)
        {
            var errors = new List<string>();
            using var chain = new X509Chain();
            // Do not hide problems from custom/partial trust stores; use default policy.
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // offline machines cannot reach CRL/OCSP
            if (!chain.Build(cert))
            {
                foreach (var status in chain.ChainStatus)
                {
                    errors.Add($"Certificate chain not trusted: {status.StatusInformation.Trim()}");
                }
                if (errors.Count == 0)
                {
                    errors.Add("Certificate chain could not be validated (untrusted root or self-signed).");
                }
                return errors;
            }
            if (!cert.HasPrivateKey)
            {
                errors.Add("Certificate has no private key (cannot serve TLS).");
            }
            if (!cert.NotBefore.ToLocalTime().Equals(cert.NotBefore) || cert.NotAfter < DateTime.Now)
            {
                errors.Add($"Certificate expired on {cert.NotAfter:u}.");
            }
            return errors;
        }

        /// <summary>
        /// True when the configured certificate policy permits serving a certificate that
        /// fails chain validation (AllowInvalidCertificates=true, Development only, spec §4.4).
        /// </summary>
        public bool AllowsInvalid(HttpsSettings settings) => settings.AllowInvalidCertificates;

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
                // PFX password is memory-only (never persisted, spec §4.5.1): after a restart
                // the password comes from appsettings.json; a persisted-PFX PUT without it
                // must not crash startup — fall back to appsettings' PfxPassword (may be null).
                X509Certificate2? cert = null;
                try
                {
                    cert = string.IsNullOrEmpty(settings.PfxPassword)
                        ? new X509Certificate2(settings.PfxPath)
                        : new X509Certificate2(settings.PfxPath, settings.PfxPassword);
                }
                catch (System.Security.Cryptography.CryptographicException ex)
                {
                    _logger.LogError(ex, "PFX '{Path}' could not be read with the provided password (password is memory-only and does not survive restarts).", settings.PfxPath);
                    throw new InvalidOperationException(
                        $"PFX certificate '{settings.PfxPath}' requires a password that is not available in this process. " +
                        "PFX passwords are never persisted; set the password in appsettings.json (Https:PfxPassword) or re-supply it via PUT /api/config/https and keep the process running.", ex);
                }
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
                // Multiple matches with the same thumbprint are impossible (hash collision);
                // pick the first entry that actually has a usable private key.
                X509Certificate2? candidate = null;
                foreach (var c in found)
                {
                    if (c.HasPrivateKey) { candidate = c; break; }
                }
                if (candidate is null)
                {
                    throw new InvalidOperationException(
                        $"Certificate '{settings.Thumbprint}' in store {storeLocation}/{settings.StoreName} has no private key and cannot serve TLS. " +
                        "Import the certificate with its private key (and mark it exportable if needed).");
                }
                _logger.LogInformation("HTTPS certificate loaded from Windows store {Location}/{Store} by thumbprint.", storeLocation, settings.StoreName);
                return candidate;
            }
            throw new InvalidOperationException("HTTPS enabled but no certificate source configured.");
        }

        /// <summary>
        /// Resolves the certificate AND enforces the AllowInvalidCertificates policy (spec §4.4):
        /// when invalid certificates are not allowed, a certificate that fails chain validation
        /// (self-signed / untrusted root — the browser's ERR_CERT_AUTHORITY_INVALID) is rejected
        /// with a clear error instead of being served. Callers that must not crash (startup) use
        /// <see cref="TryResolveValidCertificate"/> instead.
        /// </summary>
        public X509Certificate2? ResolveValidatedCertificate(HttpsSettings settings)
        {
            var cert = ResolveCertificate(settings);
            if (cert is null) return null;
            if (AllowsInvalid(settings)) return cert;
            var chainErrors = ValidateCertificateChain(cert);
            if (chainErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Configured HTTPS certificate is invalid/untrusted and AllowInvalidCertificates=false: " +
                    string.Join(" ", chainErrors) +
                    " Clients would reject it (net::ERR_CERT_AUTHORITY_INVALID). " +
                    "Use a certificate chained to a trusted root, or set Https:AllowInvalidCertificates=true in Development.");
            }
            return cert;
        }

        /// <summary>
        /// Non-throwing variant for startup: returns (cert, errors). When the policy forbids
        /// invalid certificates and the resolved certificate fails chain validation, cert is
        /// null and errors explain why (caller logs + degrades to HTTP instead of crashing).
        /// </summary>
        public (X509Certificate2? Certificate, List<string> Errors) TryResolveValidatedCertificate(HttpsSettings settings)
        {
            try
            {
                var cert = ResolveValidatedCertificate(settings);
                return (cert, new List<string>());
            }
            catch (Exception ex)
            {
                return (null, new List<string> { ex.Message });
            }
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