using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;
using PivotController.Configuration.Models;
using PivotController.Data.Providers;
using PivotController.Indexing;

namespace PivotController.Configuration.Controllers
{
    [ApiController]
    [Route("api/indexes")]
    public class IndexController : ControllerBase
    {
        private readonly IIndexManager _indexManager;
        private readonly IDataSourceProviderFactory _providerFactory;
        private readonly Microsoft.Extensions.Options.IOptions<DatabaseQuerySettings> _querySettings;
        private readonly IConfiguration _configuration;

        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions();
            options.Converters.Add(new CsvDecimalSeparatorConverter());
            options.Converters.Add(new CsvThousandsSeparatorConverter());
            options.Converters.Add(new JsonStringEnumConverter());
            options.PropertyNameCaseInsensitive = true;
            return options;
        }

        public IndexController(IIndexManager indexManager, IDataSourceProviderFactory providerFactory, Microsoft.Extensions.Options.IOptions<DatabaseQuerySettings> querySettings, IConfiguration configuration)
        {
            _indexManager = indexManager;
            _providerFactory = providerFactory;
            _querySettings = querySettings;
            _configuration = configuration;
        }

        [HttpGet]
        public ActionResult<List<IndexStatusResponse>> List() => Ok(_indexManager.ListIndexes());

        /// <summary>List every field of the index's data source with detected and mapped (Data Server) data types.</summary>
        [HttpGet("{index}/fields")]
        public ActionResult<List<IndexFieldDescription>> GetFields(string index)
        {
            try
            {
                return Ok(_indexManager.GetFieldMappings(index));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        /// <summary>Set the data type of one field in the Data Server (field mapping). Replaces any existing mapping for the field.</summary>
        [HttpPut("{index}/fields/{fieldName}")]
        public async Task<ActionResult<IndexStatusResponse>> SetFieldType(string index, string fieldName, [FromBody] SetFieldTypeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Type))
            {
                return UnprocessableEntity(new { error = "Body must be { \"type\": \"Number\" | \"String\" | \"Date\" }." });
            }
            if (!Enum.TryParse<MappingDataType>(request.Type, ignoreCase: true, out var type)
                || !Enum.IsDefined(type))
            {
                return UnprocessableEntity(new { error = $"Unknown type '{request.Type}'. Valid types: Number, String, Date." });
            }
            try
            {
                return Ok(await _indexManager.SetFieldMappingAsync(index, fieldName, type).ConfigureAwait(false));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return UnprocessableEntity(new { error = ex.Message });
            }
        }

        /// <summary>Remove a field's data type mapping; the field falls back to the detected type.</summary>
        [HttpDelete("{index}/fields/{fieldName}")]
        public async Task<ActionResult<IndexStatusResponse>> RemoveFieldType(string index, string fieldName)
        {
            try
            {
                return Ok(await _indexManager.RemoveFieldMappingAsync(index, fieldName).ConfigureAwait(false));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpGet("{index}")]
        public ActionResult<IndexStatusResponse> Get(string index)
        {
            if (!_indexManager.TryGetDefinition(index, out _))
            {
                return NotFound(new { error = $"Index '{index}' not found.", validIndexes = _indexManager.ListIndexes().Select(s => s.Index).ToArray() });
            }
            return Ok(_indexManager.GetIndexStatus(index));
        }

        /// <summary>Tests database connectivity + query execution without loading or persisting data (spec §3.x.5).</summary>
        [HttpPost("test-connection")]
        public async Task<ActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            if (request.DatabaseType is not { } dbType)
            {
                return UnprocessableEntity(new { success = false, error = "databaseType is required." });
            }
            var dbProvider = (DatabaseDataProvider)_providerFactory.Create(ToDataSourceType(dbType));
            try
            {
                return await ExecuteTestAsync(dbProvider, dbType, request.ConnectionStringName, request.ConnectionString, request.Query).ConfigureAwait(false);
            }
            catch (DatabaseProviderUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, error = ex.Message });
            }
        }

        /// <summary>Per-index variant: uses the index's stored database settings (spec §3.x.5).</summary>
        [HttpPost("{index}/test-connection")]
        public async Task<ActionResult> TestConnectionForIndex(string index)
        {
            if (!_indexManager.TryGetDefinition(index, out var definition))
            {
                return NotFound(new { error = $"Index '{index}' not found." });
            }
            if (definition.DatabaseType is not { } dbType)
            {
                return UnprocessableEntity(new { success = false, error = $"Index '{index}' is not a database index." });
            }
            var dbProvider = (DatabaseDataProvider)_providerFactory.Create(definition.SourceType);
            return await ExecuteTestAsync(dbProvider, dbType, definition.ConnectionStringName, definition.ConnectionString, definition.Query).ConfigureAwait(false);
        }

        private async Task<ActionResult> ExecuteTestAsync(DatabaseDataProvider dbProvider, DatabaseType dbType, string? connectionStringName, string? inlineConnectionString, string? query)
        {
            try
            {
                var (success, error, latencyMs, columns) = await dbProvider.TestConnectionAsync(dbType, connectionStringName, query, HttpContext.RequestAborted, inlineConnectionString).ConfigureAwait(false);
                if (!success)
                {
                    return StatusCode(StatusCodes.Status502BadGateway, new { success = false, error });
                }
                return Ok(new { success = true, latencyMs, columns });
            }
            catch (DatabaseProviderUnavailableException ex)
            {
                return StatusCode(StatusCodes.Status501NotImplemented, new { success = false, error = ex.Message });
            }
        }

        private static DataSourceType ToDataSourceType(DatabaseType dbType) => dbType switch
        {
            DatabaseType.Sqlite => DataSourceType.Sqlite,
            DatabaseType.PostgreSQL => DataSourceType.PostgreSQL,
            DatabaseType.MSSQL => DataSourceType.SqlServer,
            DatabaseType.MySql => DataSourceType.MySql,
            DatabaseType.MariaDB => DataSourceType.MariaDb,
            DatabaseType.Oracle => DataSourceType.Oracle,
            _ => throw new NotSupportedException($"Database type '{dbType}' is not supported."),
        };

        [HttpPost]
        public async Task<ActionResult<IndexStatusResponse>> Create([FromBody] IndexDefinition definition)
        {            try
            {
                var status = await _indexManager.CreateIndexAsync(definition).ConfigureAwait(false);
                return StatusCode(StatusCodes.Status201Created, status);
            }
            catch (ArgumentException ex)
            {
                return UnprocessableEntity(new { error = ex.Message });
            }
            catch (InvalidOperationException ex) when (ex.Message == "DUPLICATE")
            {
                return Conflict(new { error = $"Index '{definition.Index}' already exists." });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        [HttpPut("{index}")]
        public async Task<ActionResult<IndexStatusResponse>> Edit(string index, [FromBody] JsonElement patchElement)
        {
            IndexDefinition patch;
            try
            {
                patch = patchElement.Deserialize<IndexDefinition>(JsonOptions);
            }
            catch (System.Text.Json.JsonException ex)
            {
                return UnprocessableEntity(new { error = ex.Message });
            }
            if (patch == null)
            {
                return UnprocessableEntity(new { error = "Body must be an IndexDefinition JSON object." });
            }
            // SourceType is a non-nullable enum, so its absence cannot round-trip through
            // model binding. When omitted from the patch body, back-fill it from the
            // existing definition so EditIndexAsync's strict immutability check passes;
            // a supplied-but-different value is rejected there with 422.
            bool sourceTypeSpecified = patchElement.ValueKind == JsonValueKind.Object
                && patchElement.TryGetProperty("sourceType", out var st)
                && st.ValueKind != JsonValueKind.Null;
            if (!sourceTypeSpecified)
            {
                if (!_indexManager.TryGetDefinition(index, out var current))
                {
                    return NotFound(new { error = $"Index '{index}' not found." });
                }
                patch.SourceType = current.SourceType;
            }
            try
            {
                var status = await _indexManager.EditIndexAsync(index, patch).ConfigureAwait(false);
                return Ok(status);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return UnprocessableEntity(new { error = ex.Message });
            }
            catch (ReloadFailedKeepDataException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message, status = ex.Status });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        [HttpPost("{index}/reload")]
        public async Task<ActionResult> Reload(string index)
        {
            try
            {
                var status = await _indexManager.ReloadIndexAsync(index).ConfigureAwait(false);
                return Ok(new { status = status.Status.ToString(), rowCount = status.RowCount, loadedAt = status.LoadedAt, lastError = status.LastError });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ReloadFailedKeepDataException ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message, status = ex.Status });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        [HttpPost("{index}/enable")]
        public async Task<ActionResult> Enable(string index)
        {
            try
            {
                var status = await _indexManager.EnableIndexAsync(index).ConfigureAwait(false);
                return Ok(new { status = status.Status.ToString(), rowCount = status.RowCount });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        [HttpPost("{index}/disable")]
        public async Task<ActionResult> Disable(string index)
        {
            try
            {
                var status = await _indexManager.DisableIndexAsync(index).ConfigureAwait(false);
                return Ok(new { status = status.Status.ToString() });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpDelete("{index}")]
        public async Task<ActionResult> Delete(string index)
        {
            try
            {
                await _indexManager.DeleteIndexAsync(index).ConfigureAwait(false);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }
    }
}
