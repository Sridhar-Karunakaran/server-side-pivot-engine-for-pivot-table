using System.Data.Common;
using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;

namespace PivotController.Data;

public sealed class DynamicDataLoader
{
    private readonly IConfiguration configuration;
    private readonly string dataDirectory;

    public DynamicDataLoader(IConfiguration configuration, string dataDirectory)
    {
        this.configuration = configuration;
        this.dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public string SourceIdentity
    {
        get
        {
            string type = GetRequired("Type");
            string detail = type.ToUpperInvariant() switch
            {
                "JSON" => GetRequired("JsonPath"),
                "CSV" => $"{GetRequired("CsvPath")}|{GetCsvDelimiter()}",
                "DATABASE" => $"{GetRequired("DatabasePath")}|{GetRequired("Query")}",
                _ => throw new InvalidOperationException($"Unsupported PivotDataSource type '{type}'.")
            };
            return $"{type}:{detail}";
        }
    }

    public Task<IReadOnlyList<ExpandoObject>> LoadAsync(CancellationToken cancellationToken = default)
    {
        return GetRequired("Type").ToUpperInvariant() switch
        {
            "JSON" => LoadJsonAsync(ResolveDataPath(GetRequired("JsonPath")), cancellationToken),
            "CSV" => Task.FromResult<IReadOnlyList<ExpandoObject>>(LoadCsv(
                ResolveDataPath(GetRequired("CsvPath")), GetCsvDelimiter())),
            "DATABASE" => LoadDatabaseAsync(cancellationToken),
            string type => throw new InvalidOperationException($"Unsupported PivotDataSource type '{type}'. Use Json, Csv, or Database.")
        };
    }

    private async Task<IReadOnlyList<ExpandoObject>> LoadJsonAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("JSON pivot data must be a flat array of objects.");
        }

        List<ExpandoObject> rows = [];
        string[]? expectedFields = null;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Every JSON pivot record must be an object.");
            }

            ExpandoObject record = new();
            IDictionary<string, object?> values = record;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || values.ContainsKey(property.Name))
                {
                    throw new InvalidDataException("JSON field names must be unique and non-empty.");
                }
                values[property.Name] = ConvertJsonValue(property.Value, property.Name);
            }

            expectedFields ??= values.Keys.ToArray();
            if (!expectedFields.SequenceEqual(values.Keys, StringComparer.Ordinal))
            {
                throw new InvalidDataException("Every JSON record must have the same fields in the same order.");
            }
            rows.Add(record);
        }

        EnsureRows(rows);
        return rows;
    }

    private static object? ConvertJsonValue(JsonElement value, string fieldName) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidDataException($"Field '{fieldName}' contains a nested value. Pivot data must be flat.")
    };

    private static IReadOnlyList<ExpandoObject> LoadCsv(string path, string delimiter)
    {
        using TextFieldParser parser = new(path);
        parser.SetDelimiters(delimiter);
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        string[] headers = parser.ReadFields() ?? throw new InvalidDataException("CSV data must contain a header row.");
        ValidateHeaders(headers);
        List<string?[]> rawRows = [];
        while (!parser.EndOfData)
        {
            string[]? fields = parser.ReadFields();
            if (fields is null || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }
            if (fields.Length != headers.Length)
            {
                throw new InvalidDataException($"CSV row has {fields.Length} values but the header has {headers.Length} fields.");
            }
            rawRows.Add(fields);
        }
        EnsureRows(rawRows);

        Func<string?, object?>[] converters = Enumerable.Range(0, headers.Length)
            .Select(index => CreateCsvConverter(rawRows.Select(row => row[index])))
            .ToArray();
        return rawRows.Select(row =>
        {
            ExpandoObject record = new();
            IDictionary<string, object?> values = record;
            for (int index = 0; index < headers.Length; index++)
            {
                values[headers[index]] = converters[index](row[index]);
            }
            return record;
        }).ToList();
    }

    private static Func<string?, object?> CreateCsvConverter(IEnumerable<string?> sourceValues)
    {
        string[] values = sourceValues.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
        if (values.Length > 0 && values.All(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return value => string.IsNullOrWhiteSpace(value) ? null : long.Parse(value, CultureInfo.InvariantCulture);
        }
        if (values.Length > 0 && values.All(value => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)))
        {
            return value => string.IsNullOrWhiteSpace(value) ? null : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        }
        if (values.Length > 0 && values.All(value => bool.TryParse(value, out _)))
        {
            return value => string.IsNullOrWhiteSpace(value) ? null : bool.Parse(value);
        }
        if (values.Length > 0 && values.All(value => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        {
            return value => string.IsNullOrWhiteSpace(value) ? null : DateTime.Parse(value, CultureInfo.InvariantCulture);
        }
        return value => string.IsNullOrEmpty(value) ? null : value;
    }

    private async Task<IReadOnlyList<ExpandoObject>> LoadDatabaseAsync(CancellationToken cancellationToken)
    {
        string databasePath = ResolveDataPath(GetRequired("DatabasePath"));
        await InitializeDatabaseIfNeededAsync(databasePath, cancellationToken);
        string query = GetRequired("Query").Trim();
        if (!query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || query.Contains(';'))
        {
            throw new InvalidOperationException("The configured database query must be one read-only SELECT statement.");
        }

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = query;
        await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        string[] headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        ValidateHeaders(headers);
        List<ExpandoObject> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            ExpandoObject record = new();
            IDictionary<string, object?> values = record;
            for (int index = 0; index < headers.Length; index++)
            {
                values[headers[index]] = await reader.IsDBNullAsync(index, cancellationToken) ? null : reader.GetValue(index);
            }
            rows.Add(record);
        }
        EnsureRows(rows);
        return rows;
    }

    private async Task InitializeDatabaseIfNeededAsync(string databasePath, CancellationToken cancellationToken)
    {
        if (File.Exists(databasePath))
        {
            return;
        }
        string? scriptPath = configuration["PivotDataSource:InitializationScript"];
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new FileNotFoundException("The configured SQLite database does not exist and no initialization script was provided.", databasePath);
        }

        string resolvedScriptPath = ResolveDataPath(scriptPath);
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        await using SqliteConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = await File.ReadAllTextAsync(resolvedScriptPath, cancellationToken);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string GetRequired(string key) => configuration[$"PivotDataSource:{key}"] is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"PivotDataSource:{key} is required.");

    private string GetCsvDelimiter()
    {
        string delimiter = configuration["PivotDataSource:CsvDelimiter"] ?? ",";
        if (delimiter.Length != 1 || delimiter[0] is '\r' or '\n' or '"')
        {
            throw new InvalidOperationException(
                "PivotDataSource:CsvDelimiter must be one character and cannot be a quote or line break.");
        }
        return delimiter;
    }

    private string ResolveDataPath(string configuredPath)
    {
        string fullPath = Path.GetFullPath(configuredPath, dataDirectory);
        string relativePath = Path.GetRelativePath(dataDirectory, fullPath);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Pivot data files must be located inside the configured data directory.");
        }
        return fullPath;
    }

    private static void ValidateHeaders(string[] headers)
    {
        if (headers.Length == 0 || headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
        {
            throw new InvalidDataException("Pivot field names must be unique and non-empty.");
        }
    }

    private static void EnsureRows<T>(IReadOnlyCollection<T> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidDataException("The pivot data source contains no records.");
        }
    }
}
