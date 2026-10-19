using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Controllers
{
    /// <summary>Data Storage settings API per spec §2.6: GET/PUT /api/config/datastorage.</summary>
    [ApiController]
    [Route("api/config")]
    public class DataStorageConfigController : ControllerBase
    {
        private readonly DataStorageSettingsService _dataStorage;

        public DataStorageConfigController(DataStorageSettingsService dataStorage)
        {
            _dataStorage = dataStorage;
        }

        /// <summary>Returns the current data storage settings.</summary>
        [HttpGet("datastorage")]
        public ActionResult<DataStorageSettings> Get() => Ok(_dataStorage.Current);

        /// <summary>
        /// Cache observability (spec §2.3): which indexes are currently cached vs evicted,
        /// and the effective CacheSizeLimit. Use after loading more indexes than the limit
        /// to see LRU eviction in the EvictedIndexes list.
        /// </summary>
        [HttpGet("datastorage/cache-stats")]
        public ActionResult<CacheStatsResponse> GetCacheStats([FromServices] Indexing.IIndexManager indexManager)
            => Ok(indexManager.GetCacheStats());

        /// <summary>
        /// Merge-patch of data storage settings. RetryAttempts/KeepDataOnRefresh apply
        /// immediately; CacheSizeLimit/MaxCacheItemSizeBytes require restart.
        /// </summary>
        [HttpPut("datastorage")]
        public IActionResult Update([FromBody] JsonElement body)
        {
            if (body.ValueKind != JsonValueKind.Object)
            {
                return UnprocessableEntity(new { error = "Body must be a JSON object." });
            }
            int? retryAttempts = null;
            if (body.TryGetProperty("retryAttempts", out var rt) && rt.ValueKind == JsonValueKind.Number)
            {
                if (!rt.TryGetInt32(out var v))
                {
                    return UnprocessableEntity(new { error = "retryAttempts must be an integer." });
                }
                retryAttempts = v;
            }
            int? cacheSizeLimit = null;
            if (body.TryGetProperty("cacheSizeLimit", out var cs) && cs.ValueKind == JsonValueKind.Number)
            {
                if (!cs.TryGetInt32(out var v))
                {
                    return UnprocessableEntity(new { error = "cacheSizeLimit must be an integer." });
                }
                cacheSizeLimit = v;
            }
            bool? keepDataOnRefresh = null;
            if (body.TryGetProperty("keepDataOnRefresh", out var kd) && kd.ValueKind == JsonValueKind.True || kd.ValueKind == JsonValueKind.False)
            {
                keepDataOnRefresh = kd.GetBoolean();
            }
            long? maxCacheItemSizeBytes = null;
            if (body.TryGetProperty("maxCacheItemSizeBytes", out var ms) && ms.ValueKind == JsonValueKind.Number)
            {
                if (!ms.TryGetInt64(out var v))
                {
                    return UnprocessableEntity(new { error = "maxCacheItemSizeBytes must be an integer." });
                }
                maxCacheItemSizeBytes = v;
            }
            var (settings, requiresRestart, errors) = _dataStorage.ApplyPatch(
                retryAttempts, cacheSizeLimit, keepDataOnRefresh, maxCacheItemSizeBytes);
            if (errors.Count > 0)
            {
                return UnprocessableEntity(new { errors });
            }
            return Ok(new { settings, requiresRestart });
        }
    }
}