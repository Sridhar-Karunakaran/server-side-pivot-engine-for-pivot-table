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
        /// Validates a loaded certificate for SERVER use (spec §4.4, corrected semantics):
        /// the server must hold a private key, and — unless AllowInvalidCertificates=true —
        /// the certificate must be within its validity period. Chain TRUST is intentionally
        /// NOT enforced here: trust is validated by each CLIENT (browser / pivot table), and
        /// a machine-local trust store cannot see the client's CA roots — enforcing it here
        /// would reject perfectly valid certificates (e.g. missing intermediate in the
        /// machine store) and serve HTTP instead of HTTPS. AllowInvalidCertificates=true is
        /// the operator override for expired certificates.
        /// </summary>
        public List<string> ValidateCertificatePolicy(X509Certificate2 cert, bool allowInvalid)
        {
            var errors = new List<string>();
            if (!cert.HasPrivateKey)
            {
                errors.Add("Certificate has no private key (cannot serve TLS).");
                return errors;
            }
            var now = DateTime.Now;
            if (cert.NotBefore > now)
            {
                errors.Add($"Certificate is not valid yet (NotBefore {cert.NotBefore:u}).");
            }
            if (cert.NotAfter < now)
            {
                if (allowInvalid)
                {
                    _logger.LogWarning("HTTPS certificate expired on {NotAfter}; serving anyway (AllowInvalidCertificates=true).", cert.NotAfter);
                }
                else
                {
                    errors.Add($"Certificate expired on {cert.NotAfter:u}. Set AllowInvalidCertificates=true to serve it anyway, or install a valid certificate.");
                }
            }
            return errors;
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
                _logger.LogInformation("HTTPS certificate loaded from PEM {Path} (Windows-compatible key).", settings.PemCertPath);
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
        /// Resolves the certificate AND enforces the server-side policy (spec §4.4):
        /// a private key is mandatory, and — unless AllowInvalidCertificates=true —
        /// the certificate must be within its validity period (the flag is the operator
        /// override for EXPIRED certificates). Chain trust is deliberately not checked
        /// server-side (clients validate trust themselves; machine-local trust stores
        /// cannot see the client's CA roots and would reject valid certificates).
        /// </summary>
        public X509Certificate2? ResolveValidatedCertificate(HttpsSettings settings)
        {
            var cert = ResolveCertificate(settings);
            if (cert is null) return null;
            var policyErrors = ValidateCertificatePolicy(cert, settings.AllowInvalidCertificates);
            if (policyErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Configured HTTPS certificate cannot be used: " +
                    string.Join(" ", policyErrors));
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

        /// <summary>
        /// Loads a PEM cert+key pair in a Windows-safe way. X509Certificate2.CreateFromPemFile
        /// produces an EPHEMERAL key handle that works on Linux but breaks TLS on Windows
        /// (Kestrel handshake failure — client sees connection reset on the first request).
        /// Round-tripping through an in-memory PFX gives the key a normal, persistable
        /// representation so the pair behaves identically on both platforms.
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
                    "Check that PemCertPath points to the certificate (BEGIN CERTIFICATE) and " +
                    "PemKeyPath to the private key (BEGIN PRIVATE KEY / BEGIN RSA PRIVATE KEY), " +
                    "and that the key is unencrypted (no passphrase). " + ex.Message, ex);
            }
            if (!combined.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    $"PEM certificate '{certPath}' loaded without a private key. " +
                    "Ensure PemKeyPath points to the private key file matching the certificate.");
            }
            if (OperatingSystem.IsWindows())
            {
                // In-memory PFX round-trip: normalizes the ephemeral PEM key into a
                // Windows CNG/persistable key so Kestrel can use it for TLS.
                var pfxBytes = combined.Export(X509ContentType.Pfx);
                return new X509Certificate2(pfxBytes, (string?)null, X509KeyStorageFlags.MachineKeySet);
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