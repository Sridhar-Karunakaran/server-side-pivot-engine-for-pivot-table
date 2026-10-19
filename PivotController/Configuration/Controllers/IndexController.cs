using Microsoft.AspNetCore.Mvc;
using PivotController.Configuration.Models;
using PivotController.Indexing;

namespace PivotController.Configuration.Controllers
{
    [ApiController]
    [Route("api/indexes")]
    public class IndexController : ControllerBase
    {
        private readonly IIndexManager _indexManager;

        public IndexController(IIndexManager indexManager)
        {
            _indexManager = indexManager;
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

        [HttpPost]
        public async Task<ActionResult<IndexStatusResponse>> Create([FromBody] IndexDefinition definition)
        {
            try
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
        public async Task<ActionResult<IndexStatusResponse>> Edit(string index, [FromBody] IndexDefinition patch)
        {
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
