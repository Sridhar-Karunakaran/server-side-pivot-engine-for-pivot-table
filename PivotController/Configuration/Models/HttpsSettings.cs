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
        public bool AllowInvalidCertificates { get; set; } = false;
        /// <summary>
        /// Bind a companion plain-HTTP listener on General:Port+1 when HTTPS is enabled —
        /// for tunnels/edge proxies (e.g. *.e2b.app) that terminate TLS at the edge and
        /// speak plain HTTP to the sandbox port. Default false (HTTPS-only, spec §4.2).
        /// </summary>
        public bool AllowHttpTunnelPort { get; set; } = false;

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
            Protocols = Protocols,
            AllowInvalidCertificates = AllowInvalidCertificates,
            AllowHttpTunnelPort = AllowHttpTunnelPort
        };
    }
}