using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using PivotController.Configuration.Models;
using PivotController.Data;
using PivotController.Indexing;

var builder = WebApplication.CreateBuilder(args);
var CustomOrigins = "_customOrigins";
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Add services to the container.

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // Accept camelCase enum values (e.g. "Csv", "Number") in API payloads.
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});
builder.Services.AddProblemDetails();
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
builder.Services.AddMemoryCache((options) =>
{
    options.SizeLimit = 100;
});

// ---------- Data Server bootstrap (Step 1: Index Configuration) ----------
// Indexes are registered in appsettings.json under DataServer:Indexes (Flexmonster parity).
// A one-time migration seeds the legacy PivotDataSource block as the "default" index so
// existing clients (Sample/pivot.js) keep working without an `index` field.

string contentRoot = builder.Environment.ContentRootPath;
string dataDirectory = Path.Combine(contentRoot, "DataSource");
string dataServerDirectory = Path.Combine(contentRoot, "DataServer");
string indexesFilePath = Path.Combine(dataServerDirectory, "indexes.json");
Directory.CreateDirectory(dataServerDirectory);

DataServerConfiguration dataServerConfig = new();
builder.Configuration.GetSection(DataServerConfiguration.SectionName).Bind(dataServerConfig);

if (dataServerConfig.Indexes.Count == 0)
{
    // Bootstrap: migrate the legacy PivotDataSource block into the "default" index so
    // existing clients keep working; honor the legacy Type (Json/Csv/Database).
    string legacyType = (builder.Configuration["PivotDataSource:Type"] ?? "Csv").Trim();
    IndexDefinition defaultIndex;
    if (legacyType.Equals("Json", StringComparison.OrdinalIgnoreCase))
    {
        defaultIndex = new IndexDefinition
        {
            Index = "default",
            Name = "Default (legacy PivotDataSource)",
            SourceType = DataSourceType.Json,
            SourcePath = builder.Configuration["PivotDataSource:JsonPath"] ?? "sales-analysis.json",
            Enabled = true
        };
    }
    else if (legacyType.Equals("Database", StringComparison.OrdinalIgnoreCase))
    {
        defaultIndex = new IndexDefinition
        {
            Index = "default",
            Name = "Default (legacy PivotDataSource)",
            SourceType = DataSourceType.Database,
            DatabaseType = DatabaseType.Sqlite,
            SourcePath = builder.Configuration["PivotDataSource:DatabasePath"] ?? "sales.db",
            Query = builder.Configuration["PivotDataSource:Query"] ?? "SELECT 1",
            Enabled = true
        };
    }
    else
    {
        defaultIndex = new IndexDefinition
        {
            Index = "default",
            Name = "Default (legacy PivotDataSource)",
            SourceType = DataSourceType.Csv,
            SourcePath = builder.Configuration["PivotDataSource:CsvPath"] ?? "sales.csv",
            CsvDelimiter = (builder.Configuration["PivotDataSource:CsvDelimiter"] ?? ",").FirstOrDefault(','),
            Enabled = true
        };
    }
    dataServerConfig.Indexes.Add(defaultIndex);
}

IndexManager indexManager = new(dataServerConfig, new MemoryCache(new MemoryCacheOptions { SizeLimit = dataServerConfig.DataStorage.CacheSizeLimit }),
    dataDirectory, indexesFilePath,
    builder.Configuration["PivotDataSource:InitializationScript"] ?? string.Empty,
    LoggerFactory.Create(config => config.AddConsole()).CreateLogger<IndexManager>());

// One-time legacy migration: preserve previously-bootstrapped runtime indexes when appsettings has none.
RuntimeIndexDocument runtimeDocument = PivotController.Core.Persistence.AtomicJsonStore.Load<RuntimeIndexDocument>(indexesFilePath);
if (runtimeDocument.Indexes.Count > 0)
{
    indexManager.LoadRuntimeIndexes(runtimeDocument.Indexes, runtimeDocument.DefaultIndexId ?? dataServerConfig.DefaultIndexId);
}

builder.Services.AddSingleton<IIndexManager>(indexManager);
builder.Services.AddSingleton<DataServerConfiguration>(dataServerConfig);

var app = builder.Build();

// ---------- Index bootstrap (pre-load enabled indexes at startup) ----------
_ = Task.Run(async () =>
{
    try
    {
        await indexManager.PreloadEnabledAsync().ConfigureAwait(false);
        indexManager.PersistIndexes();
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(exception, "Index bootstrap encountered an error.");
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CustomOrigins);
app.UseExceptionHandler();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.Run();

public sealed class RuntimeIndexDocument
{
    public string? ConfigVersion { get; set; }
    public string? DefaultIndexId { get; set; }
    public List<IndexDefinition> Indexes { get; set; } = [];
}
