namespace PivotController.Configuration.Models
{
    public enum HttpProtocolsOption { HTTP1, HTTP2, HTTP1_AND_HTTP2 }
    public enum CertificateStoreLocationOption { CurrentUser, LocalMachine }

    /// <summary>HTTPS settings per spec §4 (Step 4): certificate source, protocols, TLS.</summary>
    public class HttpsSettings
    {
        public const string SectionName = "Https";

        public bool Enabled { get; set; } = false;
        public string? PfxPath { get; set; }
        /// <summary>Secret; accepted via API but never persisted or returned by GET.</summary>
        public string? PfxPassword { get; set; }
        public string? PemCertPath { get; set; }
        public string? PemKeyPath { get; set; }
        public CertificateStoreLocationOption StoreLocation { get; set; } = CertificateStoreLocationOption.CurrentUser;
        public string StoreName { get; set; } = "My";
        /// <summary>Store lookup by thumbprint (exact, preferred).</summary>
        public string? Thumbprint { get; set; }
        /// <summary>Store lookup by subject name (e.g. "CN=localhost"); alternative to thumbprint.</summary>
        public string? SubjectName { get; set; }
        public HttpProtocolsOption Protocols { get; set; } = HttpProtocolsOption.HTTP1_AND_HTTP2;

        /// <summary>Certificate source count configured (PFX, PEM, or Windows store).</summary>
        public int ConfiguredSourceCount =>
            (!string.IsNullOrWhiteSpace(PfxPath) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(PemCertPath) ? 1 : 0) +
            (!string.IsNullOrWhiteSpace(Thumbprint) || !string.IsNullOrWhiteSpace(SubjectName) ? 1 : 0);

        /// <summary>
        /// Returns a copy without the PFX password (used for GET responses and persistence).
        /// </summary>
        public HttpsSettings Sanitized() => new()
        {
            Enabled = Enabled,
            PfxPath = PfxPath,
            PfxPassword = null,
            PemCertPath = PemCertPath,
            PemKeyPath = PemKeyPath,
            StoreLocation = StoreLocation,
            StoreName = StoreName,
            Thumbprint = Thumbprint,
            SubjectName = SubjectName,
            Protocols = Protocols
        };
    }
}