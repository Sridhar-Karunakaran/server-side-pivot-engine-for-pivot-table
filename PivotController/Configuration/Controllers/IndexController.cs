using System.Dynamic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PivotController.Configuration.Models;
using PivotController.Indexing;

namespace PivotController.Configuration.Controllers;

[ApiController]
[Route("api/indexes")]
public sealed class IndexController : ControllerBase
{
    private readonly IIndexManager _indexes;
    private readonly IMemoryCache _cache;

    public IndexController(IIndexManager indexes, IMemoryCache cache)
    {
        _indexes = indexes;
        _cache = cache;
    }

    [HttpGet]
    public IActionResult List() => Ok(_indexes.List());

    [HttpGet("{index}")]
    public IActionResult Get(string index)
        => _indexes.Find(index) is { } snapshot ? Ok(snapshot) : NotFoundProblem(index);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] IndexDefinition definition)
    {
        try
        {
            IndexSnapshot snapshot = await _indexes.CreateAsync(definition).ConfigureAwait(false);
            return Created($"/api/indexes/{definition.Index}", snapshot);
        }
        catch (IndexConflictException exception)
        {
            return Conflict(Problem("IndexConflict", 409, exception.Message));
        }
        catch (IndexValidationException exception)
        {
            return UnprocessableEntity(Problem("IndexValidation", 422, exception.Message, exception.Field));
        }
        catch (NotSupportedException exception)
        {
            return UnprocessableEntity(Problem("UnsupportedProvider", 422, exception.Message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
        {
            return Problem(statusCode: 502, title: "IndexLoadFailed", detail: exception.Message);
        }
    }

    [HttpPut("{index}")]
    public async Task<IActionResult> Edit(string index, [FromBody] IndexDefinition patch)
    {
        if (!string.Equals(patch.SourceType.ToString(), _indexes.Find(index)?.SourceType, StringComparison.OrdinalIgnoreCase))
        {
            return UnprocessableEntity(Problem("IndexValidation", 422, "SourceType cannot be changed after creation."));
        }

        try
        {
            IndexSnapshot snapshot = await _indexes.EditAsync(index, existing =>
            {
                existing.Name = patch.Name;
                existing.SourcePath = patch.SourcePath;
                existing.CsvDelimiter = patch.CsvDelimiter;
                existing.CsvDecimalSeparator = patch.CsvDecimalSeparator;
                existing.CsvThousandsSeparator = patch.CsvThousandsSeparator;
                existing.DatabaseType = patch.DatabaseType;
                existing.ConnectionStringName = patch.ConnectionStringName;
                existing.Query = patch.Query;
                existing.AutoReload = patch.AutoReload;
                existing.ReloadIntervalMinutes = patch.ReloadIntervalMinutes;
                existing.MappingEnabled = patch.MappingEnabled;
                existing.FieldMappings = patch.FieldMappings;
                existing.Enabled = existing.Enabled; // enable/disable goes through dedicated endpoints
            }).ConfigureAwait(false);
            return Ok(snapshot);
        }
        catch (IndexNotFoundException exception)
        {
            return NotFoundProblem(index, exception.ValidIndexes);
        }
        catch (IndexValidationException exception)
        {
            return UnprocessableEntity(Problem("IndexValidation", 422, exception.Message, exception.Field));
        }
        catch (IndexDisabledException)
        {
            // Disabled indexes edit fine but don't reload; return the metadata.
            return Ok(_indexes.Find(index));
        }
    }

    [HttpPost("{index}/reload")]
    public async Task<IActionResult> Reload(string index)
    {
        try
        {
            return Ok(await _indexes.ReloadAsync(index).ConfigureAwait(false));
        }
        catch (IndexNotFoundException exception)
        {
            return NotFoundProblem(index, exception.ValidIndexes);
        }
        catch (IndexDisabledException)
        {
            return StatusCode(423, Problem("IndexDisabled", 423, $"Index '{index}' is disabled."));
        }
        catch (NotSupportedException exception)
        {
            return UnprocessableEntity(Problem("UnsupportedProvider", 422, exception.Message));
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException or TimeoutException)
        {
            return Problem(statusCode: 502, title: "IndexLoadFailed", detail: exception.Message);
        }
    }

    [HttpPost("{index}/enable")]
    public async Task<IActionResult> Enable(string index)
    {
        try
        {
            return Ok(await _indexes.SetEnabledAsync(index, true).ConfigureAwait(false));
        }
        catch (IndexNotFoundException exception)
        {
            return NotFoundProblem(index, exception.ValidIndexes);
        }
    }

    [HttpPost("{index}/disable")]
    public async Task<IActionResult> Disable(string index)
    {
        try
        {
            return Ok(await _indexes.SetEnabledAsync(index, false).ConfigureAwait(false));
        }
        catch (IndexNotFoundException exception)
        {
            return NotFoundProblem(index, exception.ValidIndexes);
        }
    }

    [HttpPost("{index}/test-connection")]
    public async Task<IActionResult> TestConnection(string index)
    {
        try
        {
            (bool success, long latencyMs, string? error) = await _indexes.TestConnectionAsync(index).ConfigureAwait(false);
            return success
                ? Ok(new { success, latencyMs })
                : StatusCode(502, new { success, error });
        }
        catch (IndexNotFoundException exception)
        {
            return NotFoundProblem(index, exception.ValidIndexes);
        }
        catch (InvalidOperationException exception)
        {
            return UnprocessableEntity(Problem("NotADatabaseIndex", 422, exception.Message));
        }
        catch (NotSupportedException exception)
        {
            return UnprocessableEntity(Problem("UnsupportedProvider", 422, exception.Message));
        }
    }

    [HttpDelete("{index}")]
    public IActionResult Delete(string index)
    {
        try
        {
            _indexes.Delete(index);
            return NoContent();
        }
        catch (IndexNotFoundException exception)
        {
            return NotFoundProblem(index, exception.ValidIndexes);
        }
    }

    private IActionResult NotFoundProblem(string index, IEnumerable<string>? validIndexes = null)
    {
        ProblemDetails problem = new()
        {
            Title = "IndexNotFound",
            Status = 404,
            Detail = $"Index '{index}' was not found."
        };
        if (validIndexes is not null)
        {
            problem.Extensions["validIndexes"] = validIndexes;
        }
        return NotFound(problem);
    }

    private ProblemDetails Problem(string title, int status, string detail, string? field = null)
    {
        ProblemDetails problem = new()
        {
            Title = title,
            Status = status,
            Detail = detail
        };
        if (field is not null)
        {
            problem.Extensions["errors"] = new Dictionary<string, string[]> { [field] = [detail] };
        }
        return problem;
    }
}
