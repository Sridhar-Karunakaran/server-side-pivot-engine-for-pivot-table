using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers;

public interface IDataSourceProvider
{
    IndexDefinition Definition { get; }
    Task<IReadOnlyList<ExpandoObject>> LoadAsync(DataServerConfiguration config, CancellationToken cancellationToken = default);
}

public static class DataSourceProviderFactory
{
    public static IDataSourceProvider Create(IndexDefinition definition, string dataDirectory, string initializationScript, IConfiguration configuration)
        => definition.SourceType switch
        {
            DataSourceType.Json => new JsonDataProvider(definition, dataDirectory),
            DataSourceType.Csv => new CsvDataProvider(definition, dataDirectory),
            DataSourceType.Database when definition.DatabaseType is null or Configuration.Models.DatabaseType.Sqlite
                => new SqliteDataProvider(definition, dataDirectory, initializationScript, configuration),
            DataSourceType.Database => throw new NotSupportedException(
                $"Database type '{definition.DatabaseType}' requires the optional provider package (Microsoft.Data.SqlClient, Npgsql, MySqlConnector, or Oracle.ManagedDataAccess.Core)."),
            _ => throw new NotSupportedException($"Unsupported source type '{definition.SourceType}'.")
        };
}

public sealed class JsonDataProvider : IDataSourceProvider
{
    private readonly string _path;

    public IndexDefinition Definition { get; }

    public JsonDataProvider(IndexDefinition definition, string dataDirectory)
    {
        Definition = definition;
        _path = ResolvePath(PathGuard.RequireFile(definition.SourcePath, dataDirectory));
    }

    public async Task<IReadOnlyList<ExpandoObject>> LoadAsync(DataServerConfiguration config, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(_path);
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
                values[property.Name] = ApplyMapping(Definition, property.Name, ConvertJsonValue(property.Value, property.Name));
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

    private static object? ApplyMapping(IndexDefinition definition, string fieldName, object? value)
    {
        if (!definition.MappingEnabled || definition.FieldMappings is null)
        {
            return value;
        }
        IndexFieldMapping? mapping = definition.FieldMappings.FirstOrDefault(m =>
            string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
        if (mapping is null || value is null)
        {
            return value;
        }
        return mapping.Type switch
        {
            MappingDataType.Number => ToNumber(value),
            MappingDataType.String => value.ToString(),
            MappingDataType.Date => ToDate(value),
            _ => value
        };
    }

    private static object? ToNumber(object? value)
    {
        if (value is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
        {
            return l;
        }
        if (value is string s2 && decimal.TryParse(s2, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d))
        {
            return d;
        }
        return value;
    }

    private static object? ToDate(object? value)
    {
        if (value is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
        {
            return date;
        }
        return value;
    }

    private static string ResolvePath(string fullPath) => fullPath;
    private static void EnsureRows<T>(IReadOnlyCollection<T> rows)
    {
        if (rows.Count == 0)
        {
            throw new InvalidDataException("The pivot data source contains no records.");
        }
    }
}

public static class PathGuard
{
    public static string RequireFile(string? configuredPath, string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("A source path is required for file-based indexes.");
        }
        string fullPath = Path.GetFullPath(configuredPath, Path.GetFullPath(dataDirectory));
        string relativePath = Path.GetRelativePath(Path.GetFullPath(dataDirectory), fullPath);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Index data files must be located inside the configured data directory.");
        }
        return fullPath;
    }
}

public sealed class CsvDataProvider : IDataSourceProvider
{
    private readonly string _path;

    public IndexDefinition Definition { get; }

    public CsvDataProvider(IndexDefinition definition, string dataDirectory)
    {
        Definition = definition;
        _path = PathGuard.RequireFile(definition.SourcePath, dataDirectory);
    }

    public Task<IReadOnlyList<ExpandoObject>> LoadAsync(DataServerConfiguration config, CancellationToken cancellationToken = default)
        => Task.FromResult(LoadCsv(_path, Definition));

    private static IReadOnlyList<ExpandoObject> LoadCsv(string path, IndexDefinition definition)
    {
        using TextFieldParser parser = new(path);
        parser.SetDelimiters(definition.CsvDelimiter.ToString());
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
            .Select(index => CreateCsvConverter(rawRows.Select(row => row[index]), definition, headers[index]))
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

    private static Func<string?, object?> CreateCsvConverter(IEnumerable<string?> sourceValues, IndexDefinition definition, string fieldName)
    {
        IndexFieldMapping? mapping = definition.MappingEnabled
            ? definition.FieldMappings?.FirstOrDefault(m => string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase))
            : null;

        // Explicit mapping overrides autodetection (Flexmonster parity).
        if (mapping is not null)
        {
            return mapping.Type switch
            {
                MappingDataType.Number => value => string.IsNullOrWhiteSpace(value) ? null
                    : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) ? d
                    : long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l
                    : throw new InvalidDataException($"Field '{fieldName}' is mapped as Number but contains '{value}'."),
                MappingDataType.Date => value => string.IsNullOrWhiteSpace(value) ? null
                    : DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date) ? date
                    : throw new InvalidDataException($"Field '{fieldName}' is mapped as Date but contains '{value}'."),
                MappingDataType.String => value => string.IsNullOrEmpty(value) ? null : value,
                _ => value => string.IsNullOrEmpty(value) ? null : value
            };
        }

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

public sealed class SqliteDataProvider : IDataSourceProvider
{
    private readonly string _databasePath;
    private readonly string _initializationScript;
    private readonly IConfiguration _configuration;

    public IndexDefinition Definition { get; }

    public SqliteDataProvider(IndexDefinition definition, string dataDirectory, string initializationScript, IConfiguration configuration)
    {
        Definition = definition;
        _databasePath = PathGuard.RequireFile(definition.SourcePath, dataDirectory);
        _initializationScript = string.IsNullOrWhiteSpace(initializationScript) ? string.Empty : PathGuard.RequireFile(initializationScript, dataDirectory);
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<ExpandoObject>> LoadAsync(DataServerConfiguration config, CancellationToken cancellationToken = default)
    {
        string query = (Definition.Query ?? string.Empty).Trim();
        if (!query.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || query.Contains(';'))
        {
            throw new InvalidOperationException("The configured database query must be one read-only SELECT statement.");
        }

        if (!File.Exists(_databasePath) && !string.IsNullOrEmpty(_initializationScript))
        {
            await using SqliteConnection initConnection = new(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ConnectionString);
            await initConnection.OpenAsync(cancellationToken);
            await using SqliteCommand initCommand = initConnection.CreateCommand();
            initCommand.CommandText = await File.ReadAllTextAsync(_initializationScript, cancellationToken);
            await initCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = query;
        if (config.DatabaseQuery.CommandTimeoutEnabled && config.DatabaseQuery.CommandTimeLimit > 0)
        {
            command.CommandTimeout = config.DatabaseQuery.CommandTimeLimit;
        }
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

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

    public async Task<(bool Success, long LatencyMs, string? Error)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        long start = Environment.TickCount64;
        try
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return (true, Environment.TickCount64 - start, null);
        }
        catch (Exception exception) when (exception is SqliteException or IOException)
        {
            return (false, Environment.TickCount64 - start, exception.Message);
        }
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
