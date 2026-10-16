using System.Collections.Concurrent;
using System.Dynamic;
using Microsoft.Extensions.Caching.Memory;
using PivotController.Configuration.Models;
using PivotController.Core.Retry;
using PivotController.Core.Persistence;
using PivotController.Data.Providers;

namespace PivotController.Indexing;

public sealed class IndexManager : IIndexManager
{
    private readonly IMemoryCache _cache;
    private readonly string _dataDirectory;
    private readonly string _indexesFilePath;
    private readonly string _initializationScript;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly ConcurrentDictionary<string, IndexRuntime> _runtimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<IndexManager> _logger;

    public DataServerConfiguration Config { get; private set; }

    public IndexManager(DataServerConfiguration config, IMemoryCache cache, string dataDirectory,
        string indexesFilePath, string initializationScript, ILogger<IndexManager> logger)
    {
        Config = config;
        _cache = cache;
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _indexesFilePath = indexesFilePath;
        _initializationScript = initializationScript;
        _logger = logger;
    }

    private sealed class IndexRuntime
    {
        public IndexStatus Status = IndexStatus.Disabled;
        public int? RowCount;
        public DateTime? LoadedAt;
        public string? LastError;
        public IDataSourceProvider? Provider;
    }

    // ---------- Persistence ----------

    public void PersistIndexes()
    {
        var document = new
        {
            configVersion = Config.ConfigVersion,
            defaultIndexId = Config.DefaultIndexId,
            indexes = Config.Indexes
        };
        AtomicJsonStore.Save(_indexesFilePath, document);
    }

    public void LoadRuntimeIndexes(List<IndexDefinition> indexes, string defaultIndexId)
    {
        Config.Indexes = indexes;
        Config.DefaultIndexId = defaultIndexId;
        foreach (IndexDefinition definition in indexes)
        {
            var runtime = new IndexRuntime { Status = definition.Enabled ? IndexStatus.Refreshing : IndexStatus.Disabled };
            _runtimes[definition.Index] = runtime;
        }
    }

    public async Task PreloadEnabledAsync(CancellationToken cancellationToken = default)
    {
        foreach (IndexDefinition definition in Config.Indexes.Where(i => i.Enabled))
        {
            try
            {
                await LoadAsync(definition, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Index {Index} failed to preload at startup.", definition.Index);
            }
        }
    }

    // ---------- Resolution ----------

    public IndexSnapshot? Resolve(string? indexName)
    {
        string name = string.IsNullOrWhiteSpace(indexName) ? Config.DefaultIndexId : indexName.Trim();
        return Find(name);
    }

    public IndexSnapshot? Find(string indexName)
    {
        IndexDefinition? definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase));
        return definition is null ? null : Snapshot(definition);
    }

    public IReadOnlyList<IndexSnapshot> List() => Config.Indexes.Select(Snapshot).ToList();

    private IndexSnapshot Snapshot(IndexDefinition definition)
    {
        IndexRuntime runtime = _runtimes.GetOrAdd(definition.Index, _ => new IndexRuntime
        {
            Status = definition.Enabled ? IndexStatus.Refreshing : IndexStatus.Disabled
        });

        return new IndexSnapshot
        {
            Index = definition.Index,
            Name = definition.Name,
            SourceType = definition.SourceType.ToString(),
            SourcePath = definition.SourcePath,
            DatabaseType = definition.DatabaseType?.ToString(),
            ConnectionStringName = definition.ConnectionStringName,
            Query = definition.Query,
            MappingEnabled = definition.MappingEnabled,
            FieldMappings = definition.FieldMappings,
            AutoReload = definition.AutoReload,
            ReloadIntervalMinutes = definition.ReloadIntervalMinutes,
            Enabled = definition.Enabled,
            Status = runtime.Status,
            RowCount = runtime.RowCount,
            LoadedAt = runtime.LoadedAt,
            LastError = runtime.LastError
        };
    }

    // ---------- Lifecycle ----------

    public async Task<IndexSnapshot> CreateAsync(IndexDefinition definition)
    {
        Validate(definition, isNew: true);

        if (Config.Indexes.Any(i => string.Equals(i.Index, definition.Index, StringComparison.OrdinalIgnoreCase)))
        {
            throw new IndexConflictException(definition.Index);
        }

        Config.Indexes.Add(definition);
        PersistIndexes();
        _logger.LogInformation("Index {Index} created ({SourceType}).", definition.Index, definition.SourceType);

        var runtime = new IndexRuntime { Status = IndexStatus.Refreshing };
        _runtimes[definition.Index] = runtime;
        if (definition.Enabled)
        {
            await LoadAsync(definition, CancellationToken.None).ConfigureAwait(false);
        }
        else
        {
            runtime.Status = IndexStatus.Disabled;
        }
        return Snapshot(definition);
    }

    public async Task<IndexSnapshot> EditAsync(string indexName, Action<IndexDefinition> mutate)
    {
        IndexDefinition? definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new IndexNotFoundException(indexName, Config.Indexes.Select(i => i.Index));

        IndexDefinition before = System.Text.Json.JsonSerializer.Deserialize<IndexDefinition>(
            System.Text.Json.JsonSerializer.Serialize(definition)) ?? definition;

        mutate(definition);

        try
        {
            Validate(definition, isNew: false);
        }
        catch
        {
            // Restore previous state on failed validation.
            int position = Config.Indexes.IndexOf(definition);
            Config.Indexes[position] = before;
            throw;
        }

        definition.UpdatedAt = DateTime.UtcNow;
        PersistIndexes();
        _logger.LogInformation("Index {Index} edited.", definition.Index);

        if (definition.Enabled)
        {
            await ReloadAsync(definition.Index).ConfigureAwait(false);
        }
        return Snapshot(definition);
    }

    public async Task<IndexSnapshot> ReloadAsync(string indexName)
    {
        IndexDefinition definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new IndexNotFoundException(indexName, Config.Indexes.Select(i => i.Index));

        if (!definition.Enabled)
        {
            throw new IndexDisabledException(definition.Index);
        }

        return await LoadAsync(definition, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<IndexSnapshot> SetEnabledAsync(string indexName, bool enabled)
    {
        IndexDefinition definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new IndexNotFoundException(indexName, Config.Indexes.Select(i => i.Index));

        definition.Enabled = enabled;
        definition.UpdatedAt = DateTime.UtcNow;
        PersistIndexes();

        IndexRuntime runtime = _runtimes.GetOrAdd(definition.Index, _ => new IndexRuntime());
        if (!enabled)
        {
            Evict(definition.Index);
            runtime.Status = IndexStatus.Disabled;
            runtime.RowCount = null;
            runtime.LoadedAt = null;
            _logger.LogInformation("Index {Index} disabled; cache evicted.", definition.Index);
            return Snapshot(definition);
        }

        _logger.LogInformation("Index {Index} enabled; reloading.", definition.Index);
        return await LoadAsync(definition, CancellationToken.None).ConfigureAwait(false);
    }

    public void Delete(string indexName)
    {
        IndexDefinition? definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new IndexNotFoundException(indexName, Config.Indexes.Select(i => i.Index));

        Config.Indexes.Remove(definition);
        PersistIndexes();
        Evict(definition.Index);
        _runtimes.TryRemove(definition.Index, out _);
        _logger.LogInformation("Index {Index} deleted; cache evicted.", definition.Index);
    }

    public async Task<(bool Success, long LatencyMs, string? Error)> TestConnectionAsync(string indexName)
    {
        IndexDefinition definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new IndexNotFoundException(indexName, Config.Indexes.Select(i => i.Index));

        if (definition.SourceType != DataSourceType.Database)
        {
            throw new InvalidOperationException($"Index '{indexName}' is not a database index.");
        }

        if (definition.DatabaseType is not null and not Configuration.Models.DatabaseType.Sqlite)
        {
            throw new NotSupportedException(
                $"Database type '{definition.DatabaseType}' requires the optional provider package.");
        }

        var provider = new SqliteDataProvider(definition, _dataDirectory, _initializationScript, null!);
        return await provider.TestConnectionAsync().ConfigureAwait(false);
    }

    // ---------- Data access ----------

    public string DataKey(string indexName) => $"data:{indexName}";

    public string EngineKeyPrefix(string indexName) => $"engine:{indexName}:";

    public async Task<IReadOnlyList<ExpandoObject>> GetDataAsync(string indexName, CancellationToken cancellationToken = default)
    {
        IndexDefinition definition = Config.Indexes.FirstOrDefault(i => string.Equals(i.Index, indexName, StringComparison.OrdinalIgnoreCase))
            ?? throw new IndexNotFoundException(indexName, Config.Indexes.Select(i => i.Index));

        if (!definition.Enabled)
        {
            throw new IndexDisabledException(definition.Index);
        }

        if (_cache.TryGetValue(DataKey(definition.Index), out IReadOnlyList<ExpandoObject>? cached) && cached is not null)
        {
            return cached;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(DataKey(definition.Index), out cached) && cached is not null)
            {
                return cached;
            }
            await LoadAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loadLock.Release();
        }

        return _cache.Get<IReadOnlyList<ExpandoObject>>(DataKey(definition.Index))
            ?? throw new InvalidOperationException($"The data source for index '{indexName}' could not be loaded.");
    }

    // ---------- Internals ----------

    private async Task<IndexSnapshot> LoadAsync(IndexDefinition definition, CancellationToken cancellationToken)
    {
        IndexRuntime runtime = _runtimes.GetOrAdd(definition.Index, _ => new IndexRuntime());
        bool keepDataOnRefresh = Config.DataStorage.KeepDataOnRefresh;
        IReadOnlyList<ExpandoObject>? oldData = null;
        if (keepDataOnRefresh)
        {
            oldData = _cache.Get<IReadOnlyList<ExpandoObject>>(DataKey(definition.Index));
        }

        runtime.Status = IndexStatus.Refreshing;
        runtime.LastError = null;

        var provider = definition.SourceType switch
        {
            DataSourceType.Database => (IDataSourceProvider)new SqliteDataProvider(definition, _dataDirectory, _initializationScript, null!),
            _ => DataSourceProviderFactory.Create(definition, _dataDirectory, _initializationScript, null!)
        };
        runtime.Provider = provider;

        try
        {
            IReadOnlyList<ExpandoObject> data = await RetryPolicy.ExecuteAsync(
                Config.DataStorage.RetryAttempts,
                ct => provider.LoadAsync(Config, ct),
                _logger,
                definition.Index,
                cancellationToken).ConfigureAwait(false);

            _cache.Set(DataKey(definition.Index), data, new MemoryCacheEntryOptions()
                .SetSize(1)
                .SetAbsoluteExpiration(DateTimeOffset.UtcNow.AddMinutes(60)));

            runtime.Status = IndexStatus.Ready;
            runtime.RowCount = data.Count;
            runtime.LoadedAt = DateTime.UtcNow;
            _logger.LogInformation("Index {Index} loaded: {RowCount} rows.", definition.Index, data.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            runtime.Status = IndexStatus.Failed;
            runtime.LastError = exception.Message;
            if (!keepDataOnRefresh && oldData is not null)
            {
                _cache.Remove(DataKey(definition.Index));
            }
            _logger.LogWarning(exception, "Index {Index} load failed.", definition.Index);
            throw;
        }

        return Snapshot(definition);
    }

    private void Evict(string indexName)
    {
        _cache.Remove(DataKey(indexName));
        // Engine keys are removed lazily by the pivot controller through the prefix scheme;
        // MemoryCache cannot enumerate, so engine entries expire naturally (60 min) or are
        // invalidated by version-stamping below.
        _engineVersion.AddOrUpdate(indexName, 1, (_, v) => v + 1);
    }

    private readonly ConcurrentDictionary<string, int> _engineVersion = new(StringComparer.OrdinalIgnoreCase);

    public int EngineVersion(string indexName) => _engineVersion.GetOrAdd(indexName, 1);

    public IReadOnlyDictionary<string, string> GetFieldTypes(string indexName)
    {
        IReadOnlyList<ExpandoObject> data = GetDataAsync(indexName).ConfigureAwait(false).GetAwaiter().GetResult();
        if (data.Count == 0)
        {
            return new Dictionary<string, string>();
        }
        IDictionary<string, object?> row = data[0];
        return row.ToDictionary(pair => pair.Key, pair => pair.Value?.GetType().Name ?? "null");
    }

    private static void Validate(IndexDefinition definition, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(definition.Index))
        {
            throw new IndexValidationException("Index", "Index name is required.");
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(definition.Index, @"^[A-Za-z0-9._-]{1,64}$"))
        {
            throw new IndexValidationException("Index", "Index name must match [A-Za-z0-9._-]{1,64}.");
        }
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new IndexValidationException("Name", "Display name is required.");
        }
        if (definition.AutoReload && definition.ReloadIntervalMinutes <= 0)
        {
            throw new IndexValidationException("ReloadIntervalMinutes", "ReloadIntervalMinutes > 0 requires AutoReload=true; AutoReload with 0 is meaningless.");
        }
        if (!definition.AutoReload && definition.ReloadIntervalMinutes < 0)
        {
            throw new IndexValidationException("ReloadIntervalMinutes", "ReloadIntervalMinutes cannot be negative.");
        }
        if (definition.MappingEnabled && definition.FieldMappings is { Count: 0 })
        {
            throw new IndexValidationException("FieldMappings", "MappingEnabled requires at least one field mapping, or null for autodetect.");
        }
        if (!definition.MappingEnabled)
        {
            definition.FieldMappings = null; // Disabling Mapping deletes all adjustments (Flexmonster parity).
        }
        if (definition.SourceType is DataSourceType.Json or DataSourceType.Csv && string.IsNullOrWhiteSpace(definition.SourcePath))
        {
            throw new IndexValidationException("SourcePath", $"SourcePath is required for {definition.SourceType} indexes.");
        }
        if (definition.SourceType == DataSourceType.Database)
        {
            if (definition.DatabaseType is null)
            {
                throw new IndexValidationException("DatabaseType", "DatabaseType is required for database indexes.");
            }
            if (string.IsNullOrWhiteSpace(definition.Query))
            {
                throw new IndexValidationException("Query", "Query is required for database indexes.");
            }
            if (string.IsNullOrWhiteSpace(definition.ConnectionStringName) && definition.DatabaseType != Configuration.Models.DatabaseType.Sqlite)
            {
                throw new IndexValidationException("ConnectionStringName", "ConnectionStringName is required for non-Sqlite database indexes.");
            }
        }
    }
}

// ---------- Exceptions ----------

public class IndexNotFoundException : Exception
{
    public IEnumerable<string> ValidIndexes { get; }

    public IndexNotFoundException(string indexName, IEnumerable<string> validIndexes)
        : base($"Index '{indexName}' was not found.")
    {
        ValidIndexes = validIndexes;
    }
}

public class IndexDisabledException : Exception
{
    public IndexDisabledException(string indexName) : base($"Index '{indexName}' is disabled.") { }
}

public class IndexConflictException : Exception
{
    public IndexConflictException(string indexName) : base($"An index named '{indexName}' already exists.") { }
}

public class IndexValidationException : Exception
{
    public string Field { get; }
    public IndexValidationException(string field, string message) : base(message) => Field = field;
}
