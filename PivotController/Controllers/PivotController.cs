using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Syncfusion.Pivot.Engine;


namespace PivotController.Controllers
{
    [Route("api/[controller]")]
    public class PivotController : Controller
    {
        private readonly IMemoryCache _cache;
        private bool isRendered;
        private PivotEngine<DataSource.PivotViewData> PivotEngine = new PivotEngine<DataSource.PivotViewData>();
        private ExcelExport excelExport = new ExcelExport();
        private PivotExportEngine<DataSource.PivotViewData> pivotExport = new PivotExportEngine<DataSource.PivotViewData>();
        public PivotController(IMemoryCache cache, IWebHostEnvironment environment)
        {
            _cache = cache;
        }

        [Route("/api/pivot/post")]
        [HttpPost]
        [SuppressMessage("Security", "CA5391:Use ValidateAntiForgeryToken on methods which support GET, HEAD, PUT, DELETE, PATCH, or POST")]
        public async Task<object> Post([FromBody] object args)
        {
            ArgumentNullException.ThrowIfNull(args);
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            FetchData param = JsonConvert.DeserializeObject<FetchData>(args.ToString() ?? string.Empty);
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
            ArgumentNullException.ThrowIfNull(param);
            if (param.Action == "fetchFieldMembers")
            {
                return await GetMembers(param).ConfigureAwait(false);
            }
            else if (param.Action == "fetchRawData")
            {
                return await GetRawData(param).ConfigureAwait(false);
            }
            else if (param.Action == "onExcelExport" || param.Action == "onCsvExport" ||
                     param.Action == "onPivotExcelExport" || param.Action == "onPivotCsvExport")
            {
                EngineProperties engine = await GetEngine(param).ConfigureAwait(false);
                if (param.InternalProperties.EnableVirtualization && param.ExportAllPages)
                {
                    engine = await PivotEngine.PerformAction(engine, param).ConfigureAwait(false);
                }
                if (param.Action == "onExcelExport")
                {
                    return excelExport.ExportToExcel("Excel", engine, null, param.ExcelExportProperties);
                }
                else if (param.Action == "onPivotExcelExport" || param.Action == "onPivotCsvExport")
                {
                    return pivotExport.ExportAsPivot(param.Action == "onPivotExcelExport" ? ExportType.Excel : ExportType.CSV, engine, param);
                }
                else
                {
                    return excelExport.ExportToExcel("CSV", engine, null, param.ExcelExportProperties);
                }
            }
            else
            {
                return await GetPivotValues(param).ConfigureAwait(false);
            }
        }

        private async Task<EngineProperties> GetEngine(FetchData param)
        {
            isRendered = false;
#pragma warning disable CS8603 // Possible null reference return.
            return await _cache.GetOrCreateAsync("engine" + param.Hash,
                async (cacheEntry) =>
                {
                    isRendered = true;
                    cacheEntry.SetSize(1);
                    cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);
                    PivotEngine.Data = await GetData(param).ConfigureAwait(false);
                    return await PivotEngine.GetEngine(param).ConfigureAwait(false);
                }).ConfigureAwait(false);
#pragma warning restore CS8603 // Possible null reference return.
        }

        private async Task<object> GetData(FetchData param)
        {
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
            return await _cache.GetOrCreateAsync("dataSource" + param.Hash,
                async (cacheEntry) =>
                {
                    cacheEntry.SetSize(1);
                    cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);

                    // Here, you can refer different kinds of data sources. We've bound a collection in this illustration.
                    // return new PivotViewData().GetVirtualData();
                    return new DataSource.PivotViewData().GetVirtualData();

                    // EXAMPLE:
                    // Other data sources, such as DataTable, CSV, JSON, etc., can be bound as shown below.
                    // return new DataSource.BusinessObjectsDataView().GetDataTable();
                    // return new DataSource.PivotJSONData().ReadJSONData(_hostingEnvironment.ContentRootPath + "\\DataSource\\sales-analysis.json");
                    // return new DataSource.PivotCSVData().ReadCSVData(_hostingEnvironment.ContentRootPath + "\\DataSource\\sales.csv");
                    // return new DataSource.PivotJSONData().ReadJSONData("http://cdn.syncfusion.com/data/sales-analysis.json");
                    // return new DataSource.PivotCSVData().ReadCSVData("http://cdn.syncfusion.com/data/sales-analysis.csv");
                }).ConfigureAwait(false);
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning restore CS8603 // Possible null reference return.
        }

        private async Task<object> GetMembers(FetchData param)
        {
            EngineProperties engine = await GetEngine(param).ConfigureAwait(false);
            Dictionary<string, object> returnValue = new Dictionary<string, object>();
            returnValue["memberName"] = param.MemberName;
            if (engine.FieldList[param.MemberName].IsMembersFilled)
            {
                returnValue["members"] = JsonConvert.SerializeObject(engine.FieldList[param.MemberName].Members);
            }
            else
            {
                await PivotEngine.PerformAction(engine, param).ConfigureAwait(false);
                returnValue["members"] = JsonConvert.SerializeObject(engine.FieldList[param.MemberName].Members);
            }
            return returnValue;
        }

        private async Task<object> GetRawData(FetchData param)
        {
            EngineProperties engine = await GetEngine(param).ConfigureAwait(false);
            return PivotEngine.GetRawData(param, engine);
        }

        private async Task<object> GetPivotValues(FetchData param)
        {
            EngineProperties engine = await GetEngine(param).ConfigureAwait(false);
            if (param.IsGroupingUpdated)
            {
                engine.Data = await GetData(param).ConfigureAwait(false);
            }
            if (!isRendered)
            {
                engine = await PivotEngine.PerformAction(engine, param).ConfigureAwait(false);
            }
            _cache.Remove("engine" + param.Hash);
            _cache.Set("engine" + param.Hash, engine, new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddMinutes(60)));
            return PivotEngine.GetSerializedPivotValues();
        }
    }
}
