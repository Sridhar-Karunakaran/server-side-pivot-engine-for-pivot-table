using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;
using PivotController.Data.Providers;
using PivotController.Indexing;
using PivotController.Reports.Services;

var builder = WebApplication.CreateBuilder(args);
var CustomOrigins = "_customOrigins";

// Data Server configuration (Index Configuration — Step 1).
var dataServerSettings = builder.Configuration.GetSection(DataServerSettings.SectionName).Get<DataServerSettings>() ?? new DataServerSettings();
builder.Services.AddSingleton(dataServerSettings);
builder.Services.AddSingleton<AtomicJsonStore>();
builder.Services.AddSingleton<IndexStore>();

// Database Query Settings (Step 7 — Database Query Integration, spec §3.1/§3.x):
// command timeout for all database indexes; runtime-applicable via the config API.
builder.Services.Configure<DatabaseQuerySettings>(builder.Configuration.GetSection("DatabaseQuery"));
builder.Services.AddSingleton<IDatabaseQueryProviderFactory, DatabaseQueryProviderRegistry>();

builder.Services.AddSingleton<IDataSourceProviderFactory, DataSourceProviderFactory>();
builder.Services.AddSingleton<IIndexManager, IndexManager>();
builder.Services.AddHostedService<IndexReloadWorker>();

// General settings (Step 2): port, minimum log level, data folder.
var generalSettings = builder.Configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new GeneralSettings();
var generalValidation = generalSettings.Validate();
if (generalValidation.Count > 0)
{
    Console.Error.WriteLine("Invalid General settings: " + string.Join("; ", generalValidation));
    Environment.Exit(1);
}
builder.Services.AddSingleton(generalSettings);
// Runtime-adjustable minimum-level filter so General:MinimumLogLevel is observable in the
// local console log output (spec §1.3). One shared MinimumLevelLoggerProvider instance is
// registered both as the ILoggerProvider (replacing the default console provider) and as
// a DI singleton that GeneralSettingsService flips live.
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
var minimumLevelProvider = new MinimumLevelLoggerProvider();
builder.Logging.AddProvider(minimumLevelProvider);
builder.Services.AddSingleton(minimumLevelProvider);
builder.Services.AddSingleton<GeneralSettingsService>();
builder.Services.AddHostedService<GeneralSettingsFileWatcher>();
builder.Services.AddSingleton<DataStorageSettingsService>();

// Data storage settings (Step 3): retry, cache size limit, keep-data-on-refresh, item size.
var dataStorageSettings = builder.Configuration.GetSection(DataStorageSettings.SectionName).Get<DataStorageSettings>() ?? new DataStorageSettings();
var dataStorageValidation = dataStorageSettings.Validate();
if (dataStorageValidation.Count > 0)
{
    Console.Error.WriteLine("Invalid DataStorage settings: " + string.Join("; ", dataStorageValidation));
    Environment.Exit(1);
}
builder.Services.AddSingleton(dataStorageSettings);

// HTTPS settings (Step 4): certificate source + protocols. Persisted https.json (written
// by PUT /api/config/https) overrides appsettings.json so restart-required changes bind.
// https.json is read pre-Build because the Kestrel listener (certificate, protocols) is
// configured below and fixed for the process lifetime (spec §4.2.4).
var httpsSettings = builder.Configuration.GetSection(HttpsSettings.SectionName).Get<HttpsSettings>() ?? new HttpsSettings();
var httpsGeneralPath = System.IO.Path.Combine(builder.Environment.ContentRootPath, generalSettings.DataFolder);
var httpsPersistedPath = System.IO.Path.Combine(httpsGeneralPath, "https.json");
var httpsStore = new AtomicJsonStore(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
if (System.IO.File.Exists(httpsPersistedPath))
{
    var httpsPersisted = httpsStore.Load<HttpsSettings>(httpsPersistedPath, new HttpsSettings());
    // https.json is the persisted source of truth (restart-required changes set via the API).
    // Explicit nulls in it must NOT wipe appsettings values (user's config style writes nulls
    // for unused fields); a stored Enabled=false from an old run must not silently kill a
    // newly-enabled appsettings config. Log loudly — silent precedence has caused false
    // "HTTP instead of HTTPS" reports.
    Console.WriteLine($"HTTPS: https.json found at {httpsPersistedPath}; its values override appsettings.json 'Https'. Delete the file to configure via appsettings.json only.");
    if (httpsPersisted.Enabled) httpsSettings.Enabled = true;
    if (httpsPersisted.PfxPath != null) httpsSettings.PfxPath = httpsPersisted.PfxPath;
    if (httpsPersisted.PemCertPath != null) httpsSettings.PemCertPath = httpsPersisted.PemCertPath;
    if (httpsPersisted.PemKeyPath != null) httpsSettings.PemKeyPath = httpsPersisted.PemKeyPath;
    // Enums are always written by the API (never null) — always authoritative.
    httpsSettings.StoreLocation = httpsPersisted.StoreLocation;
    httpsSettings.Protocols = httpsPersisted.Protocols;
    if (!string.IsNullOrWhiteSpace(httpsPersisted.StoreName)) httpsSettings.StoreName = httpsPersisted.StoreName;
    if (httpsPersisted.Thumbprint != null) httpsSettings.Thumbprint = httpsPersisted.Thumbprint;
    if (httpsPersisted.SubjectName != null) httpsSettings.SubjectName = httpsPersisted.SubjectName;
}
builder.Services.AddSingleton(httpsSettings);
builder.Services.AddSingleton<CertificateService>();
builder.Services.AddSingleton<HttpsSettingsService>();
var certificateService = new CertificateService(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<CertificateService>());
var httpsErrors = certificateService.Validate(httpsSettings);
if (httpsErrors.Count > 0)
{
    // Fail fast with a clear message instead of a listener that never comes up (spec §4.2).
    Console.Error.WriteLine("Invalid HTTPS settings: " + string.Join("; ", httpsErrors));
    Environment.Exit(1);
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new CsvDecimalSeparatorConverter());
    options.JsonSerializerOptions.Converters.Add(new CsvThousandsSeparatorConverter());
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// Security settings (Step 5, spec §5): Authorization.Enabled + CORS.AllowOrigin.
// Credentials (username/password) live in the Data Server configuration
// (appsettings.json Security:Authorization) — Admin Panel later.
// Persisted security.json (written by PUT /api/config/security) overrides the
// Enabled/AllowOrigin appsettings values; credentials are never persisted to disk.
var securitySettings = builder.Configuration.GetSection(SecuritySettings.SectionName).Get<SecuritySettings>() ?? new SecuritySettings();
{
    // Persisted security.json (written by PUT /api/config/security) overrides the
    // Enabled/AllowOrigin appsettings values; credentials are never persisted to disk.
    var securityPersistedPath = System.IO.Path.Combine(
        System.IO.Path.Combine(builder.Environment.ContentRootPath,
            (builder.Configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new GeneralSettings()).DataFolder),
        "security.json");
    if (System.IO.File.Exists(securityPersistedPath))
    {
        var persisted = new PivotController.Core.Persistence.AtomicJsonStore(
            Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<PivotController.Core.Persistence.AtomicJsonStore>())
            .Load<SecuritySettings>(securityPersistedPath, new SecuritySettings());
        if (persisted.Authorization != null)
        {
            securitySettings.Authorization.Enabled = persisted.Authorization.Enabled;
        }
        if (persisted.CORS != null && !string.IsNullOrWhiteSpace(persisted.CORS.AllowOrigin))
        {
            securitySettings.CORS.AllowOrigin = persisted.CORS.AllowOrigin;
        }
        Console.WriteLine($"Security: security.json found at {securityPersistedPath}; Enabled/AllowOrigin override appsettings.json 'Security'.");
    }
}
// Flexmonster UG rule (spec §5.2): when basic authorization is enabled, Allow origin
// must be specified with proper origin URLs — never "*" and never empty. Fail fast.
var securityValidation = securitySettings.Validate();
if (securityValidation.Count > 0 && securitySettings.Authorization.Enabled)
{
    Console.Error.WriteLine("Invalid Security settings: " + string.Join("; ", securityValidation));
    Console.Error.WriteLine("Fix Security settings (appsettings.json Security:* or {DataFolder}/security.json) and restart.");
    Environment.Exit(1);
}
builder.Services.AddSingleton(securitySettings);
builder.Services.AddSingleton<SecuritySettingsService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CustomOrigins, policy =>
    {
        // Native ASP.NET Core CORS behavior (spec §5.3, clarified): disallowed origins
        // receive no CORS headers and are blocked by the browser — no server-side 403.
        if (securitySettings.CORS.AllowsAllOrigins)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(securitySettings.CORS.Origins);
        }
        policy.AllowAnyHeader().AllowAnyMethod();
    });
});
// MemoryCache without a global size cap: CacheSizeLimit is enforced per index
// (Flexmonster semantics — max cached server responses per index), not globally.
// CacheSizeLimit == 0 disables response caching (handled in IndexManager).
builder.Services.AddMemoryCache();

// Report Sharing settings (Step 6, spec §6): appsettings.json "ReportSharingOptions",
// persisted overrides in {DataFolder}/reportsharing.json (written by PUT /api/config/reportsharing).
// Expiration cleanup runs regardless of AutoDeletingEnabled; AutoDeletingEnabled only governs
// MaxSizeOfFolder quota eviction (spec §6.4/§6.6).
var reportSharingSettings = builder.Configuration.GetSection(ReportSharingSettings.SectionName).Get<ReportSharingSettings>() ?? new ReportSharingSettings();
var reportSharingGeneral = builder.Configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new GeneralSettings();
var reportSharingDataFolder = System.IO.Path.Combine(builder.Environment.ContentRootPath, reportSharingGeneral.DataFolder);
var reportSharingPersistedPath = System.IO.Path.Combine(reportSharingDataFolder, ReportSharingService.FileName);
if (System.IO.File.Exists(reportSharingPersistedPath))
{
    var reportSharingStore = new AtomicJsonStore(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
    var reportSharingPersisted = reportSharingStore.Load<ReportSharingSettings>(reportSharingPersistedPath, new ReportSharingSettings());
    // Precedence (spec §6.8, same model as Steps 2-5): non-null persisted fields override appsettings.
    reportSharingSettings.EndpointEnabled = reportSharingPersisted.EndpointEnabled;
    if (!string.IsNullOrWhiteSpace(reportSharingPersisted.PathToFolder)) reportSharingSettings.PathToFolder = reportSharingPersisted.PathToFolder;
    reportSharingSettings.ReportExpirationTime = reportSharingPersisted.ReportExpirationTime;
    reportSharingSettings.MaxSizeOfFolder = reportSharingPersisted.MaxSizeOfFolder;
    reportSharingSettings.AutoDeletingEnabled = reportSharingPersisted.AutoDeletingEnabled;
    Console.WriteLine($"ReportSharing: reportsharing.json found at {reportSharingPersistedPath}; its values override appsettings.json 'ReportSharingOptions'. Delete the file to configure via appsettings.json only.");
}
// Fail fast on invalid values (negative quota/expiration, traversal path — spec §6.8).
if (reportSharingSettings.ReportExpirationTime < 0 || reportSharingSettings.MaxSizeOfFolder < 0)
{
    Console.Error.WriteLine("Invalid ReportSharingOptions: ReportExpirationTime and MaxSizeOfFolder must be >= 0.");
    Environment.Exit(1);
}
if (reportSharingSettings.PathToFolder != null && reportSharingSettings.PathToFolder.Contains(".."))
{
    Console.Error.WriteLine("Invalid ReportSharingOptions: PathToFolder path traversal (..) not allowed.");
    Environment.Exit(1);
}
builder.Services.AddSingleton(reportSharingSettings);
builder.Services.AddSingleton<ReportSharingService>();
builder.Services.AddHostedService<ReportSweepWorker>();

// Kestrel listener (spec §1.2 port binding, spec §4 HTTPS). General:Port takes precedence
// over the `urls` key; only an explicit ASPNETCORE_URLS environment variable overrides it.
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    // Pre-resolve the certificate once so a bad/locked PFX (e.g. memory-only password lost
    // across restarts) degrades to a clear log + HTTP fallback instead of crashing startup
    // (bootstrap-resilience precedent from Step 1). spec §4.2.
    System.Security.Cryptography.X509Certificates.X509Certificate2? httpsCertificate = null;
    if (httpsSettings.Enabled)
    {
        // Resolve the certificate once so a bad/locked PFX (e.g. memory-only password lost
        // across restarts) degrades to a clear log + HTTP fallback instead of crashing startup
        // (bootstrap-resilience precedent from Step 1). spec §4.2. Only loadability (private
        // key present) is checked — browsers enforce trust/validity themselves.
        var (cert, certErrors) = certificateService.TryResolveValidatedCertificate(httpsSettings);
        if (certErrors.Count > 0)
        {
            Console.Error.WriteLine("HTTPS certificate could not be used; starting HTTP-only.");
            foreach (var err in certErrors)
            {
                Console.Error.WriteLine("  " + err);
            }
            Console.Error.WriteLine("Fix Https settings (appsettings.json Https:* or PUT /api/config/https) and restart.");
            httpsSettings.Enabled = false;
        }
        else
        {
            httpsCertificate = cert;
        }
    }
    if (httpsSettings.Enabled && httpsCertificate is not null)
    {
        // HTTPS listener (spec §4.2): resolved certificate, protocol selection (§4.3),
        // TLS 1.2+ enforced, fixed for the process lifetime (spec §4.2.4).
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Any, generalSettings.Port, listenOptions =>
            {
                listenOptions.UseHttps(httpsCertificate, httpsOptions =>
                {
                    // TLS 1.2+ enforced: disable legacy protocol versions (spec §4.2).
                    httpsOptions.SslProtocols =
                        System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                });
                listenOptions.Protocols = CertificateService.MapProtocols(httpsSettings.Protocols);
            });
        });
    }
    else
    {
        builder.WebHost.UseUrls($"http://0.0.0.0:{generalSettings.Port}");
    }
}

var app = builder.Build();

// Bootstrap: ensure data folder structure (spec §1.4) and register indexes.
var generalService = app.Services.GetRequiredService<GeneralSettingsService>();
generalService.EnsureDataFolders();
// Load persisted DataStorage settings (datastorage.json) so restart-required changes
// (CacheSizeLimit / MaxCacheItemSizeBytes) actually apply after a restart.
app.Services.GetRequiredService<DataStorageSettingsService>().LoadPersisted();
// Load persisted Security settings (security.json: authorization.enabled, cors.allowOrigin)
// so runtime changes made via PUT /api/config/security survive restarts (spec §5.4).
app.Services.GetRequiredService<SecuritySettingsService>().LoadPersisted(generalService.DataFolderPath);
// Load persisted Report Sharing settings (reportsharing.json) — persisted values already
// merged pre-Build above for startup binding; refresh in-place via the service for parity.
app.Services.GetRequiredService<ReportSharingService>().LoadPersisted();

// Bootstrap: register indexes from appsettings.json and DataServer/indexes.json, preload enabled ones.
var indexManager = app.Services.GetRequiredService<IIndexManager>();
await indexManager.RegisterAllAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Redirect HTTP -> HTTPS only when HTTPS is enabled (spec §4.2); otherwise the middleware
// would 500 on every request with no https port configured.
if (httpsSettings.Enabled)
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
// CORS policy must be applied BEFORE Basic Auth so preflight OPTIONS requests
// (sent by browsers without credentials) are answered with CORS headers.
app.UseCors(CustomOrigins);
// Basic Authentication (spec §5.2): when Security:Authorization:Enabled, every /api/*
// request (except CORS preflights) requires valid Data Server credentials.
app.UseMiddleware<PivotController.Security.PivotBasicAuthMiddleware>();

app.MapControllers();
// Static demo page (tunnel sample) — served from wwwroot/data-server.html.
app.UseStaticFiles();
app.Run();