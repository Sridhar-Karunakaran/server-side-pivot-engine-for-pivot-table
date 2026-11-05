using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;
using PivotController.Data.Providers;
using PivotController.Indexing;
using PivotController.Reports.Services;

var builder = WebApplication.CreateBuilder(args);
var CustomOrigins = "_customOrigins";

var dataServerSettings = builder.Configuration.GetSection(DataServerSettings.SectionName).Get<DataServerSettings>() ?? new DataServerSettings();
builder.Services.AddSingleton(dataServerSettings);
builder.Services.AddSingleton<AtomicJsonStore>();
builder.Services.AddSingleton<IndexStore>();

var databaseQuerySettings = builder.Configuration.GetSection("DatabaseQuery").Get<DatabaseQuerySettings>() ?? new DatabaseQuerySettings();
builder.Services.AddSingleton(databaseQuerySettings);
builder.Services.AddSingleton<DatabaseQuerySettingsService>();
builder.Services.AddSingleton<IDatabaseQueryProviderFactory, DatabaseQueryProviderRegistry>();

builder.Services.AddSingleton<IDataSourceProviderFactory, DataSourceProviderFactory>();
builder.Services.AddSingleton<IIndexManager, IndexManager>();
builder.Services.AddHostedService<IndexReloadWorker>();

var generalSettings = builder.Configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new GeneralSettings();
var generalValidation = generalSettings.Validate();
if (generalValidation.Count > 0)
{
    Console.Error.WriteLine("Invalid General settings: " + string.Join("; ", generalValidation));
    Environment.Exit(1);
}
builder.Services.AddSingleton(generalSettings);
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
var minimumLevelProvider = new MinimumLevelLoggerProvider();
builder.Logging.AddProvider(minimumLevelProvider);
builder.Services.AddSingleton(minimumLevelProvider);
builder.Services.AddSingleton<GeneralSettingsService>();
builder.Services.AddHostedService<GeneralSettingsFileWatcher>();
builder.Services.AddSingleton<DataStorageSettingsService>();

var dataStorageSettings = builder.Configuration.GetSection(DataStorageSettings.SectionName).Get<DataStorageSettings>() ?? new DataStorageSettings();
var dataStorageValidation = dataStorageSettings.Validate();
if (dataStorageValidation.Count > 0)
{
    Console.Error.WriteLine("Invalid DataStorage settings: " + string.Join("; ", dataStorageValidation));
    Environment.Exit(1);
}
builder.Services.AddSingleton(dataStorageSettings);

var httpsSettings = builder.Configuration.GetSection(HttpsSettings.SectionName).Get<HttpsSettings>() ?? new HttpsSettings();
var httpsGeneralPath = System.IO.Path.Combine(builder.Environment.ContentRootPath, generalSettings.DataFolder);
var httpsPersistedPath = System.IO.Path.Combine(httpsGeneralPath, "https.json");
var httpsStore = new AtomicJsonStore(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
if (System.IO.File.Exists(httpsPersistedPath))
{
    var httpsPersisted = httpsStore.Load<HttpsSettings>(httpsPersistedPath, new HttpsSettings());
    Console.WriteLine($"HTTPS: https.json found at {httpsPersistedPath}; its values override appsettings.json 'Https'. Delete the file to configure via appsettings.json only.");
    if (httpsPersisted.Enabled) httpsSettings.Enabled = true;
    if (httpsPersisted.PfxPath != null) httpsSettings.PfxPath = httpsPersisted.PfxPath;
    if (httpsPersisted.PemCertPath != null) httpsSettings.PemCertPath = httpsPersisted.PemCertPath;
    if (httpsPersisted.PemKeyPath != null) httpsSettings.PemKeyPath = httpsPersisted.PemKeyPath;
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
var securitySettings = builder.Configuration.GetSection(SecuritySettings.SectionName).Get<SecuritySettings>() ?? new SecuritySettings();
{
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
builder.Services.AddMemoryCache();

var reportSharingSettings = builder.Configuration.GetSection(ReportSharingSettings.SectionName).Get<ReportSharingSettings>() ?? new ReportSharingSettings();
var reportSharingGeneral = builder.Configuration.GetSection(GeneralSettings.SectionName).Get<GeneralSettings>() ?? new GeneralSettings();
var reportSharingDataFolder = System.IO.Path.Combine(builder.Environment.ContentRootPath, reportSharingGeneral.DataFolder);
var reportSharingPersistedPath = System.IO.Path.Combine(reportSharingDataFolder, ReportSharingService.FileName);
if (System.IO.File.Exists(reportSharingPersistedPath))
{
    var reportSharingStore = new AtomicJsonStore(Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger<AtomicJsonStore>());
    var reportSharingPersisted = reportSharingStore.Load<ReportSharingSettings>(reportSharingPersistedPath, new ReportSharingSettings());
    reportSharingSettings.EndpointEnabled = reportSharingPersisted.EndpointEnabled;
    if (!string.IsNullOrWhiteSpace(reportSharingPersisted.PathToFolder)) reportSharingSettings.PathToFolder = reportSharingPersisted.PathToFolder;
    reportSharingSettings.ReportExpirationTime = reportSharingPersisted.ReportExpirationTime;
    reportSharingSettings.MaxSizeOfFolder = reportSharingPersisted.MaxSizeOfFolder;
    reportSharingSettings.AutoDeletingEnabled = reportSharingPersisted.AutoDeletingEnabled;
    Console.WriteLine($"ReportSharing: reportsharing.json found at {reportSharingPersistedPath}; its values override appsettings.json 'ReportSharingOptions'. Delete the file to configure via appsettings.json only.");
}
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

if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    System.Security.Cryptography.X509Certificates.X509Certificate2? httpsCertificate = null;
    if (httpsSettings.Enabled)
    {
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
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(System.Net.IPAddress.Any, generalSettings.Port, listenOptions =>
            {
                listenOptions.UseHttps(httpsCertificate, httpsOptions =>
                {
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

var generalService = app.Services.GetRequiredService<GeneralSettingsService>();
generalService.EnsureDataFolders();
app.Services.GetRequiredService<DataStorageSettingsService>().LoadPersisted();
app.Services.GetRequiredService<DatabaseQuerySettingsService>().LoadPersisted();
app.Services.GetRequiredService<SecuritySettingsService>().LoadPersisted(generalService.DataFolderPath);
app.Services.GetRequiredService<ReportSharingService>().LoadPersisted();

var indexManager = app.Services.GetRequiredService<IIndexManager>();
await indexManager.RegisterAllAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (httpsSettings.Enabled)
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.UseCors(CustomOrigins);
app.UseMiddleware<PivotController.Security.PivotBasicAuthMiddleware>();

app.MapControllers();
app.UseStaticFiles();
app.Run();