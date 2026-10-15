using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using PivotController.Data;
using Syncfusion.Pivot.Engine;

namespace PivotController.Controllers;

[Route("api/[controller]")]
public sealed class PivotController : Controller
{
    private readonly IMemoryCache cache;
    private readonly DynamicDataLoader dataLoader;
    private bool isRendered;
    private readonly PivotEngine<ExpandoObject> pivotEngine = new();
    private readonly ExcelExport excelExport = new();
    private readonly PivotExportEngine<ExpandoObject> pivotExport = new();

    public PivotController(IMemoryCache cache, DynamicDataLoader dataLoader)
    {
        this.cache = cache;
        this.dataLoader = dataLoader;
    }

    [Route("/api/pivot/post")]
    [HttpPost]
    [SuppressMessage("Security", "CA5391:Use ValidateAntiForgeryToken on methods which support GET, HEAD, PUT, DELETE, PATCH, or POST")]
    public async Task<object> Post([FromBody] object args)
    {
        ArgumentNullException.ThrowIfNull(args);
        FetchData? parameter = JsonConvert.DeserializeObject<FetchData>(args.ToString() ?? string.Empty);
        ArgumentNullException.ThrowIfNull(parameter);

        return parameter.Action switch
        {
            "fetchFieldMembers" => await GetMembers(parameter).ConfigureAwait(false),
            "fetchRawData" => await GetRawData(parameter).ConfigureAwait(false),
            "onExcelExport" or "onCsvExport" or "onPivotExcelExport" or "onPivotCsvExport" => await Export(parameter).ConfigureAwait(false),
            _ => await GetPivotValues(parameter).ConfigureAwait(false)
        };
    }

    private async Task<object> Export(FetchData parameter)
    {
        EngineProperties engine = await GetEngine(parameter).ConfigureAwait(false);
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

    private async Task<EngineProperties> GetEngine(FetchData parameter)
    {
        isRendered = false;
        EngineProperties? engine = await cache.GetOrCreateAsync(GetEngineCacheKey(parameter), async cacheEntry =>
        {
            isRendered = true;
            cacheEntry.SetSize(1);
            cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);
            pivotEngine.Data = await GetData().ConfigureAwait(false);
            return await pivotEngine.GetEngine(parameter).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return engine ?? throw new InvalidOperationException("The pivot engine could not be initialized.");
    }

    private async Task<object> GetData()
    {
        object? data = await cache.GetOrCreateAsync($"dataSource:{dataLoader.SourceIdentity}", async cacheEntry =>
        {
            cacheEntry.SetSize(1);
            cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);
            return await dataLoader.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return data ?? throw new InvalidOperationException("The pivot data source could not be loaded.");
    }

    private async Task<object> GetMembers(FetchData parameter)
    {
        EngineProperties engine = await GetEngine(parameter).ConfigureAwait(false);
        Dictionary<string, object> result = new() { ["memberName"] = parameter.MemberName };
        if (!engine.FieldList[parameter.MemberName].IsMembersFilled)
        {
            await pivotEngine.PerformAction(engine, parameter).ConfigureAwait(false);
        }
        result["members"] = JsonConvert.SerializeObject(engine.FieldList[parameter.MemberName].Members);
        return result;
    }

    private async Task<object> GetRawData(FetchData parameter)
    {
        EngineProperties engine = await GetEngine(parameter).ConfigureAwait(false);
        return pivotEngine.GetRawData(parameter, engine);
    }

    private async Task<object> GetPivotValues(FetchData parameter)
    {
        EngineProperties engine = await GetEngine(parameter).ConfigureAwait(false);
        if (parameter.IsGroupingUpdated)
        {
            engine.Data = await GetData().ConfigureAwait(false);
        }
        if (!isRendered)
        {
            engine = await pivotEngine.PerformAction(engine, parameter).ConfigureAwait(false);
        }
        string cacheKey = GetEngineCacheKey(parameter);
        cache.Remove(cacheKey);
        cache.Set(cacheKey, engine, new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddMinutes(60)));
        return pivotEngine.GetSerializedPivotValues();
    }

    private string GetEngineCacheKey(FetchData parameter) => $"engine:{dataLoader.SourceIdentity}:{parameter.Hash}";
}
