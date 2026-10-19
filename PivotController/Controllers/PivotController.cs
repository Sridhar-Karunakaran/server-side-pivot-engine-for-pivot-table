using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using PivotController.Indexing;
using Syncfusion.Pivot.Engine;

namespace PivotController.Controllers;

[Route("api/[controller]")]
public sealed class PivotController : Controller
{
    // The index served to the pivot table is fixed here on the server side for now.
    // The client does not send an index name yet; when client-side selection is
    // added later, this constant becomes the fallback and the payload `index`
    // value takes precedence.
    private const string ServerIndexName = "Sales-Json";

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

        // Index routing is currently fixed to ServerIndexName on the server side;
        // no client-supplied index is expected yet.
        IndexSnapshot? resolved = indexes.Resolve(ServerIndexName);
        if (resolved is null)
        {
            return NotFound(new
            {
                title = "IndexNotFound",
                status = 404,
                detail = $"Index '{ServerIndexName}' was not found.",
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
        // The IndexManager already caches index data under DataKey(indexName) and
        // invalidates it on edit/reload/disable. Reading directly (no extra caching
        // layer here) guarantees the engine always sees freshly reloaded data.
        IReadOnlyList<System.Dynamic.ExpandoObject> data = await indexes.GetDataAsync(indexName, HttpContext.RequestAborted).ConfigureAwait(false);
        return data;
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
        // EngineVersion bumps on every reload/disable/delete/edit so stale engines
        // are never served after the index data changes (spec §12.2 eviction).
        => $"{indexes.EngineKeyPrefix(indexName)}v{indexes.EngineVersion(indexName)}:{parameter.Hash}";
}
