using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using PivotController.Indexing;
using Syncfusion.Pivot.Engine;

namespace PivotController.Controllers;

[Route("api/[controller]")]
public sealed class PivotController : Controller
{
    private readonly IMemoryCache cache;
    private readonly IIndexManager indexes;
    private bool isRendered;
    private readonly PivotEngine<ExpandoObject> pivotEngine = new();
    private readonly ExcelExport excelExport = new();
    private readonly PivotExportEngine<ExpandoObject> pivotExport = new();

    public PivotController(IMemoryCache cache, IIndexManager indexes)
    {
        this.cache = cache;
        this.indexes = indexes;
    }

    [Route("/api/pivot/post")]
    [HttpPost]
    [SuppressMessage("Security", "CA5391:Use ValidateAntiForgeryToken on methods which support GET, HEAD, PUT, DELETE, PATCH, or POST")]
    public async Task<object> Post([FromBody] object args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string payload = args.ToString() ?? string.Empty;
        FetchData? parameter = JsonConvert.DeserializeObject<FetchData>(payload);
        ArgumentNullException.ThrowIfNull(parameter);

        // Step 1 (index configuration): resolve the client-supplied index name.
        // `index` is not part of the Syncfusion FetchData contract, so it is read from the
        // raw payload. Missing index falls back to DataServer:DefaultIndexId (legacy "default").
        string? requestedIndex;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            requestedIndex = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("index", out JsonElement indexElement)
                && indexElement.ValueKind == JsonValueKind.String
                ? indexElement.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            requestedIndex = null;
        }

        IndexSnapshot? resolved = indexes.Resolve(requestedIndex);
        if (resolved is null)
        {
            return NotFound(new
            {
                title = "IndexNotFound",
                status = 404,
                detail = $"Index '{requestedIndex}' was not found.",
                validIndexes = indexes.List().Select(i => i.Index)
            });
        }
        if (!resolved.Enabled || resolved.Status == IndexStatus.Disabled)
        {
            return StatusCode(423, new { title = "IndexDisabled", status = 423, detail = $"Index '{resolved.Index}' is disabled." });
        }
        string indexName = resolved.Index;

        return parameter.Action switch
        {
            "fetchFieldMembers" => await GetMembers(indexName, parameter).ConfigureAwait(false),
            "fetchRawData" => await GetRawData(indexName, parameter).ConfigureAwait(false),
            "onExcelExport" or "onCsvExport" or "onPivotExcelExport" or "onPivotCsvExport" => await Export(indexName, parameter).ConfigureAwait(false),
            _ => await GetPivotValues(indexName, parameter).ConfigureAwait(false)
        };
    }

    private async Task<object> Export(string indexName, FetchData parameter)
    {
        EngineProperties engine = await GetEngine(indexName, parameter).ConfigureAwait(false);
        if (parameter.InternalProperties.EnableVirtualization && parameter.ExportAllPages)
        {
            engine = await pivotEngine.PerformAction(engine, parameter).ConfigureAwait(false);
        }
        return parameter.Action switch
        {
            "onExcelExport" => excelExport.ExportToExcel("Excel", engine, null, parameter.ExcelExportProperties),
            "onPivotExcelExport" => pivotExport.ExportAsPivot(ExportType.Excel, engine, parameter),
            "onPivotCsvExport" => pivotExport.ExportAsPivot(ExportType.CSV, engine, parameter),
            _ => excelExport.ExportToExcel("CSV", engine, null, parameter.ExcelExportProperties)
        };
    }

    private async Task<EngineProperties> GetEngine(string indexName, FetchData parameter)
    {
        isRendered = false;
        EngineProperties? engine = await cache.GetOrCreateAsync(GetEngineCacheKey(indexName, parameter), async cacheEntry =>
        {
            isRendered = true;
            cacheEntry.SetSize(1);
            cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);
            pivotEngine.Data = await GetData(indexName).ConfigureAwait(false);
            return await pivotEngine.GetEngine(parameter).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return engine ?? throw new InvalidOperationException("The pivot engine could not be initialized.");
    }

    private async Task<object> GetData(string indexName)
    {
        object? data = await cache.GetOrCreateAsync(indexes.DataKey(indexName), async cacheEntry =>
        {
            cacheEntry.SetSize(1);
            cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);
            return await indexes.GetDataAsync(indexName, HttpContext.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return data ?? throw new InvalidOperationException("The pivot data source could not be loaded.");
    }

    private async Task<object> GetMembers(string indexName, FetchData parameter)
    {
        EngineProperties engine = await GetEngine(indexName, parameter).ConfigureAwait(false);
        Dictionary<string, object> result = new() { ["memberName"] = parameter.MemberName };
        if (!engine.FieldList[parameter.MemberName].IsMembersFilled)
        {
            await pivotEngine.PerformAction(engine, parameter).ConfigureAwait(false);
        }
        result["members"] = JsonConvert.SerializeObject(engine.FieldList[parameter.MemberName].Members);
        return result;
    }

    private async Task<object> GetRawData(string indexName, FetchData parameter)
    {
        EngineProperties engine = await GetEngine(indexName, parameter).ConfigureAwait(false);
        return pivotEngine.GetRawData(parameter, engine);
    }

    private async Task<object> GetPivotValues(string indexName, FetchData parameter)
    {
        EngineProperties engine = await GetEngine(indexName, parameter).ConfigureAwait(false);
        if (parameter.IsGroupingUpdated)
        {
            engine.Data = await GetData(indexName).ConfigureAwait(false);
        }
        if (!isRendered)
        {
            engine = await pivotEngine.PerformAction(engine, parameter).ConfigureAwait(false);
        }
        string cacheKey = GetEngineCacheKey(indexName, parameter);
        cache.Remove(cacheKey);
        cache.Set(cacheKey, engine, new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddMinutes(60)));
        return pivotEngine.GetSerializedPivotValues();
    }

    private string GetEngineCacheKey(string indexName, FetchData parameter)
        => $"{indexes.EngineKeyPrefix(indexName)}{parameter.Hash}";
}
