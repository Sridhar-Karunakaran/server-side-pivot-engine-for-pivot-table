using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncfusion.Pivot.Engine;

namespace PivotController.Controllers
{
    [Route("api/[controller]")]
    public class PivotController : Controller
    {
        // Phase 1 (Index Configuration) testing: index declared controller-side per spec §0.3.1.
        // Client-side integration sends the index in the request body in Step 8.
        private const string ServerIndexName = "Sales-Json";

        private readonly IMemoryCache _cache;
        private readonly Indexing.IIndexManager _indexManager;
        private bool isRendered;
        private PivotEngine<ExpandoObject> PivotEngine = new PivotEngine<ExpandoObject>();
        private ExcelExport excelExport = new ExcelExport();
        private PivotExportEngine<ExpandoObject> pivotExport = new PivotExportEngine<ExpandoObject>();

        public PivotController(IMemoryCache cache, Indexing.IIndexManager indexManager)
        {
            _cache = cache;
            _indexManager = indexManager;
        }

        [Route("/api/pivot/post")]
        [HttpPost]
        [SuppressMessage("Security", "CA5391:Use ValidateAntiForgeryToken on methods which support GET, HEAD, PUT, DELETE, PATCH, or POST")]
        public async Task<object> Post([FromBody] object args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var raw = JObject.Parse(args.ToString() ?? string.Empty);
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
            FetchData param = raw.ToObject<FetchData>();
#pragma warning restore CS8600
            ArgumentNullException.ThrowIfNull(param);
            // The engine dereferences Locale["Null"] on member generation; a request without
            // a locale payload must still provide that key.
            if (param.InternalProperties?.Locale == null)
            {
                param.InternalProperties ??= new CustomProperties();
                param.InternalProperties.Locale = "{\"Null\":\"null\"}";
            }

            // Index-aware routing: request body index -> controller default -> DefaultIndexId fallback.
            string indexName = raw["index"]?.ToString() ?? raw["Index"]?.ToString() ?? ServerIndexName;
            ApplyIndexFieldMapping(param, indexName);
            if (!_indexManager.TryGetDefinition(indexName, out _))
            {
                return NotFound(new { error = $"Index '{indexName}' not found.", validIndexes = _indexManager.ListIndexes().Select(s => s.Index).ToArray() });
            }
            var status = _indexManager.GetIndexStatus(indexName);
            if (status.Status == Configuration.Models.IndexStatus.Disabled)
            {
                return StatusCode(StatusCodes.Status423Locked, new { error = $"Index '{indexName}' is disabled." });
            }

            if (param.Action == "fetchFieldMembers")
            {
                return await GetMembers(param, indexName).ConfigureAwait(false);
            }
            else if (param.Action == "fetchRawData")
            {
                return await GetRawData(param, indexName).ConfigureAwait(false);
            }
            else if (param.Action == "onExcelExport" || param.Action == "onCsvExport" ||
                     param.Action == "onPivotExcelExport" || param.Action == "onPivotCsvExport")
            {
                EngineProperties engine = await GetEngine(param, indexName).ConfigureAwait(false);
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
                return await GetPivotValues(param, indexName).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Applies the indexed data source's server-side field mappings to the request so the
        /// pivot engine types each field accordingly. The engine reads this dictionary first in
        /// GetFieldType, so a data type changed in the Data Server is reflected in the client
        /// pivot table (aggregation, sorting, grouping) on the next request.
        /// </summary>
        private void ApplyIndexFieldMapping(FetchData param, string indexName)
        {
            if (!_indexManager.TryGetDefinition(indexName, out var definition)
                || definition.MappingEnabled != true
                || definition.FieldMappings is not { Count: > 0 })
            {
                return;
            }
            param.InternalProperties ??= new CustomProperties();
            param.InternalProperties.FieldType = definition.FieldMappings.ToDictionary(
                m => m.FieldName,
                m => m.Type == Configuration.Models.MappingDataType.Number ? "number"
                   : m.Type == Configuration.Models.MappingDataType.Date ? "datetime"
                   : "string",
                StringComparer.OrdinalIgnoreCase);
            // FieldMapping (FieldOptions.DataType) takes precedence over the engine's value
            // autodetection, so a mapping change fully controls the field type delivered to
            // the client pivot table — including demoting a numeric/date field to string.
            if (param.DataSourceSettings != null)
            {
                param.DataSourceSettings.FieldMapping = definition.FieldMappings.Select(m => new FieldOptions
                {
                    Name = m.FieldName,
                    DataType = m.Type switch
                    {
                        Configuration.Models.MappingDataType.Number => "number",
                        Configuration.Models.MappingDataType.Date => "datetime",
                        _ => "string"
                    }
                }).ToList();
            }
            // Mapping affects engine field types, so cached engines per hash must be rebuilt.
            _indexManager.EvictEngineCache(indexName);
        }

        private async Task<EngineProperties> GetEngine(FetchData param, string indexName)
        {
            isRendered = false;
            string engineKey = _indexManager.EngineCacheKey(indexName, param.Hash);
#pragma warning disable CS8603 // Possible null reference return.
            return await _cache.GetOrCreateAsync(engineKey,
                async (cacheEntry) =>
                {
                    isRendered = true;
                    cacheEntry.SetSize(1);
                    cacheEntry.AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60);
                    PivotEngine.Data = await GetData(indexName).ConfigureAwait(false);
                    return await PivotEngine.GetEngine(param).ConfigureAwait(false);
                }).ConfigureAwait(false);
#pragma warning restore CS8603
        }

        private async Task<object> GetData(string indexName)
        {
            // Data cache is owned by the IndexManager (key: data:{index}); reloads and
            // enable/disable evict it there so pivot engines always see fresh data.
            return await _indexManager.GetDataAsync(indexName).ConfigureAwait(false);
        }

        private async Task<object> GetMembers(FetchData param, string indexName)
        {
            EngineProperties engine = await GetEngine(param, indexName).ConfigureAwait(false);
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

        private async Task<object> GetRawData(FetchData param, string indexName)
        {
            EngineProperties engine = await GetEngine(param, indexName).ConfigureAwait(false);
            return PivotEngine.GetRawData(param, engine);
        }

        private async Task<object> GetPivotValues(FetchData param, string indexName)
        {
            EngineProperties engine = await GetEngine(param, indexName).ConfigureAwait(false);
            if (param.IsGroupingUpdated)
            {
                engine.Data = await GetData(indexName).ConfigureAwait(false);
            }
            if (!isRendered)
            {
                engine = await PivotEngine.PerformAction(engine, param).ConfigureAwait(false);
            }
            string engineKey = _indexManager.EngineCacheKey(indexName, param.Hash);
            _cache.Remove(engineKey);
            _cache.Set(engineKey, engine, new MemoryCacheEntryOptions().SetSize(1).SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddMinutes(60)));
            return PivotEngine.GetSerializedPivotValues();
        }
    }
}
