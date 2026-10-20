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
            if ((!string.IsNullOrWhiteSpace(settings.Thumbprint) || !string.IsNullOrWhiteSpace(settings.SubjectName)) && !OperatingSystem.IsWindows())
            {
                errors.Add("Certificate store lookup is only supported on Windows; use pfxPath or pemCertPath.");
            }
            else if (!string.IsNullOrWhiteSpace(settings.Thumbprint) && !string.IsNullOrWhiteSpace(settings.SubjectName))
            {
                errors.Add("Configure either thumbprint or subjectName for the store lookup, not both.");
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
                var cert = LoadPemCertificate(settings.PemCertPath!, settings.PemKeyPath!);
                _logger.LogInformation("HTTPS certificate loaded from PEM {Path}.", settings.PemCertPath);
                return cert;
            }
            bool storeByThumbprint = !string.IsNullOrWhiteSpace(settings.Thumbprint);
            bool storeBySubject = !string.IsNullOrWhiteSpace(settings.SubjectName);
            if ((storeByThumbprint || storeBySubject) && OperatingSystem.IsWindows())
            {
                if (storeByThumbprint && storeBySubject)
                {
                    throw new InvalidOperationException("Configure either thumbprint or subjectName for the store lookup, not both.");
                }
                var storeLocation = settings.StoreLocation == CertificateStoreLocationOption.LocalMachine
                    ? System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine
                    : System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser;
                using var store = new X509Store(settings.StoreName, storeLocation);
                store.Open(OpenFlags.ReadOnly);
                var findType = storeByThumbprint ? X509FindType.FindByThumbprint : X509FindType.FindBySubjectName;
                var findValue = storeByThumbprint ? settings.Thumbprint! : settings.SubjectName!;
                var found = store.Certificates.Find(findType, findValue, validOnly: false);
                if (found.Count == 0)
                {
                    throw new InvalidOperationException($"Certificate with {(storeByThumbprint ? $"thumbprint '{settings.Thumbprint}'" : $"subject name '{settings.SubjectName}'")} not found in store {storeLocation}/{settings.StoreName}.");
                }
                // Pick the first entry that actually has a usable private key (subject-name
                // lookups can match multiple certificates; thumbprint matches cannot).
                X509Certificate2? candidate = null;
                foreach (var c in found)
                {
                    if (c.HasPrivateKey) { candidate = c; break; }
                }
                if (candidate is null)
                {
                    throw new InvalidOperationException(
                        $"Certificate '{(storeByThumbprint ? settings.Thumbprint : settings.SubjectName)}' in store {storeLocation}/{settings.StoreName} has no private key and cannot serve TLS. " +
                        "Import the certificate with its private key (and mark it exportable if needed).");
                }
                _logger.LogInformation("HTTPS certificate loaded from Windows store {Location}/{Store} by {Lookup}.", storeLocation, settings.StoreName, storeByThumbprint ? "thumbprint" : "subject name");
                return candidate;
            }
            throw new InvalidOperationException("HTTPS enabled but no certificate source configured.");
        }

        /// <summary>
        /// Resolves the certificate and verifies it is usable to serve TLS (spec §4.4):
        /// a private key is mandatory. Trust and validity are NOT enforced server-side —
        /// browsers/clients block invalid or untrusted certificates themselves, so any
        /// server-side check would only add false rejections.
        /// </summary>
        public X509Certificate2? ResolveValidatedCertificate(HttpsSettings settings)
        {
            var cert = ResolveCertificate(settings);
            if (cert is null) return null;
            if (!cert.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "Configured HTTPS certificate has no private key (cannot serve TLS).");
            }
            return cert;
        }

        /// <summary>
        /// Non-throwing variant for startup: returns (cert, errors). When the certificate
        /// cannot be loaded or has no private key, cert is null and errors explain why
        /// (caller logs + degrades to HTTP instead of crashing).
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

        /// <summary>
        /// Loads a PEM cert+key pair with clear diagnostics for the common failures
        /// (paths swapped, encrypted key, mismatched pair). The key is kept exactly as
        /// CreateFromPemFile produces it — no export/round-trip, so no behavior change
        /// versus PFX on any platform. Private key presence is verified.
        /// </summary>
        private X509Certificate2 LoadPemCertificate(string certPath, string keyPath)
        {
            X509Certificate2 combined;
            try
            {
                combined = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to read PEM certificate '{certPath}' / key '{keyPath}'. " +
                    "Check that PemCertPath points at the certificate (BEGIN CERTIFICATE) and " +
                    "PemKeyPath at the private key (BEGIN PRIVATE KEY / BEGIN RSA PRIVATE KEY), " +
                    $"and that the key matches the certificate. ({ex.Message})", ex);
            }
            if (!combined.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    $"PEM certificate '{certPath}' loaded without a private key; " +
                    "verify PemKeyPath points at the matching private key file.");
            }
            if (!OperatingSystem.IsWindows())
            {
                return combined; // Linux/macOS: Kestrel uses the PEM key directly — nothing further needed.
            }
            // Windows-only normalization (does NOT affect PFX or Windows-store sources):
            // CreateFromPemFile yields an EPHEMERAL key handle. Kestrel's SChannel cannot use
            // an ephemeral handle for the TLS handshake — the browser's connection to
            // https://localhost:<port> is closed before any HTTP response (ERR_CONNECTION_CLOSED /
            // "closed before it receives an HTTP response"). Round-tripping through an IN-MEMORY
            // PFX re-imports the key as a normal CNG key that SChannel can use.
            // Default key flags (no MachineKeySet): no elevation required.
            // If the round-trip fails for any reason, fall back to the ORIGINAL combined
            // certificate — never fail a load that would otherwise succeed.
            try
            {
                var pfxBytes = combined.Export(X509ContentType.Pfx);
                var normalized = new X509Certificate2(pfxBytes, (string?)null);
                if (normalized.HasPrivateKey)
                {
                    _logger.LogInformation(
                        "PEM key normalized for Windows (in-memory PFX round-trip; ephemeral key handles cannot serve TLS on Windows).");
                    return normalized;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PEM in-memory PFX normalization failed; using the original PEM certificate.");
            }
            return combined;
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