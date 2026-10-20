using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;
using PivotController.Data.Providers;
using PivotController.Indexing;

var builder = WebApplication.CreateBuilder(args);
var CustomOrigins = "_customOrigins";

// Data Server configuration (Index Configuration — Step 1).
var dataServerSettings = builder.Configuration.GetSection(DataServerSettings.SectionName).Get<DataServerSettings>() ?? new DataServerSettings();
builder.Services.AddSingleton(dataServerSettings);
builder.Services.AddSingleton<AtomicJsonStore>();
builder.Services.AddSingleton<IndexStore>();
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
    httpsSettings.Enabled = httpsPersisted.Enabled;
    httpsSettings.PfxPath = httpsPersisted.PfxPath;
    httpsSettings.PemCertPath = httpsPersisted.PemCertPath;
    httpsSettings.PemKeyPath = httpsPersisted.PemKeyPath;
    httpsSettings.StoreLocation = httpsPersisted.StoreLocation;
    httpsSettings.StoreName = httpsPersisted.StoreName;
    httpsSettings.Thumbprint = httpsPersisted.Thumbprint;
    httpsSettings.Protocols = httpsPersisted.Protocols;
    httpsSettings.AllowInvalidCertificates = httpsPersisted.AllowInvalidCertificates;
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
builder.Services.AddCors(options =>
{
    options.AddPolicy(CustomOrigins,
    builder =>
    {
        builder.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});
// MemoryCache without a global size cap: CacheSizeLimit is enforced per index
// (Flexmonster semantics — max cached server responses per index), not globally.
// CacheSizeLimit == 0 disables response caching (handled in IndexManager).
builder.Services.AddMemoryCache();

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
        try
        {
            httpsCertificate = certificateService.ResolveCertificate(httpsSettings);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("HTTPS certificate could not be loaded; starting HTTP-only. " + ex.Message);
            Console.Error.WriteLine("Fix Https settings (appsettings.json Https:* or PUT /api/config/https) and restart.");
            httpsSettings.Enabled = false;
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

app.MapControllers();
app.UseCors(CustomOrigins);
// Static demo page (tunnel sample) — served from wwwroot/data-server.html.
app.UseStaticFiles();
app.Run();