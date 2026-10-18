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

        // The engine dereferences customProperties.Locale unguarded (InitProperties),
        // so InternalProperties must never be null or Locale-derivation throws NRE.
        parameter.InternalProperties ??= new CustomProperties { Locale = "{}" };
        if (parameter.InternalProperties.Locale == null)
        {
            parameter.InternalProperties.Locale = "{}";
        }
        // GenerateMembers reads Locale["Null"] and Locale["GroupOutOfRange"] unguarded;
        // ensure these keys exist so minimal payloads don't throw KeyNotFoundException.
        try
        {
            Dictionary<string, string>? localeMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(parameter.InternalProperties.Locale);
            if (localeMap is not null)
            {
                if (!localeMap.ContainsKey("Null"))
                {
                    localeMap["Null"] = "Null";
                }
                if (!localeMap.ContainsKey("GroupOutOfRange"))
                {
                    localeMap["GroupOutOfRange"] = "OutOfRange";
                }
                parameter.InternalProperties.Locale = System.Text.Json.JsonSerializer.Serialize(localeMap);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            parameter.InternalProperties.Locale = "{\"Null\":\"Null\",\"GroupOutOfRange\":\"OutOfRange\"}";
        }

        // The client may supply the index name in the payload (spec §3.2/§10); when present
        // it takes precedence over the server-side fallback index.
        string requestedIndex = ServerIndexName;
        if (parameter.DataSourceSettings is not null)
        {
            // FetchData is deserialized with Newtonsoft; a camelCase "index" property is
            // not bound to DataOptions, so read it from the raw payload instead.
            string? payloadIndex = TryGetIndexFromPayload(payload);
            if (!string.IsNullOrWhiteSpace(payloadIndex))
            {
                requestedIndex = payloadIndex;
            }
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

    private static string? TryGetIndexFromPayload(string payload)
    {
        try
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }
            if (document.RootElement.TryGetProperty("index", out System.Text.Json.JsonElement indexValue) &&
                indexValue.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return indexValue.GetString();
            }
            if (document.RootElement.TryGetProperty("dataSourceSettings", out System.Text.Json.JsonElement settings) &&
                settings.ValueKind == System.Text.Json.JsonValueKind.Object &&
                settings.TryGetProperty("index", out System.Text.Json.JsonElement settingsIndex) &&
                settingsIndex.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return settingsIndex.GetString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed payload: fall back to the server-side default index.
        }
        return null;
    }
}
