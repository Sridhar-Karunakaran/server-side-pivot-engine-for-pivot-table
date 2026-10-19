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
var minimumLevelProvider = new PivotController.Configuration.Services.MinimumLevelLoggerProvider();
builder.Logging.AddProvider(minimumLevelProvider);
builder.Services.AddSingleton(minimumLevelProvider);
builder.Services.AddSingleton<GeneralSettingsService>();
builder.Services.AddHostedService<GeneralSettingsFileWatcher>();
builder.Services.AddSingleton<PivotController.Configuration.Services.DataStorageSettingsService>();
// Kestrel binds the configured General:Port (spec §1.2). General:Port takes precedence
// over the `urls` key (launchSettings applicationUrl would otherwise pin the port);
// only an explicit ASPNETCORE_URLS environment variable overrides it.
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{generalSettings.Port}");
}

// Data storage settings (Step 3): retry, cache size limit, keep-data-on-refresh, item size.
var dataStorageSettings = builder.Configuration.GetSection(PivotController.Configuration.Models.DataStorageSettings.SectionName)
    .Get<PivotController.Configuration.Models.DataStorageSettings>() ?? new PivotController.Configuration.Models.DataStorageSettings();
var dataStorageValidation = dataStorageSettings.Validate();
if (dataStorageValidation.Count > 0)
{
    Console.Error.WriteLine("Invalid DataStorage settings: " + string.Join("; ", dataStorageValidation));
    Environment.Exit(1);
}
builder.Services.AddSingleton(dataStorageSettings);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new PivotController.Configuration.Models.CsvDecimalSeparatorConverter());
    options.JsonSerializerOptions.Converters.Add(new PivotController.Configuration.Models.CsvThousandsSeparatorConverter());
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
// Cache size limit from DataStorage:CacheSizeLimit (spec §2.3). 0 disables caching.
builder.Services.AddMemoryCache((options) =>
{
    options.SizeLimit = dataStorageSettings.CacheSizeLimit == 0 ? null : dataStorageSettings.CacheSizeLimit;
});

var app = builder.Build();

// Bootstrap: ensure data folder structure (spec §1.4) and register indexes.
var generalService = app.Services.GetRequiredService<GeneralSettingsService>();
generalService.EnsureDataFolders();
// Load persisted DataStorage settings (datastorage.json) so restart-required changes
// (CacheSizeLimit / MaxCacheItemSizeBytes) actually apply after a restart.
app.Services.GetRequiredService<PivotController.Configuration.Services.DataStorageSettingsService>().LoadPersisted();

// Bootstrap: register indexes from appsettings.json and DataServer/indexes.json, preload enabled ones.
var indexManager = app.Services.GetRequiredService<IIndexManager>();
await indexManager.RegisterAllAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.UseCors(CustomOrigins);
// Static demo page (tunnel sample) — served from wwwroot/data-server.html.
app.UseStaticFiles();
app.Run();
