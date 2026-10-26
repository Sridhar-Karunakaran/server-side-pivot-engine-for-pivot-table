using System.Collections.Concurrent;
using System.Dynamic;
using Microsoft.Extensions.Caching.Memory;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;
using PivotController.Data.Providers;

namespace PivotController.Indexing
{
    public interface IIndexManager
    {
        Task RegisterAllAsync();
        Task<List<ExpandoObject>> GetDataAsync(string indexName);
        Task<IndexStatusResponse> CreateIndexAsync(IndexDefinition definition);
        Task<IndexStatusResponse> EditIndexAsync(string indexName, IndexDefinition patch);
        Task<IndexStatusResponse> ReloadIndexAsync(string indexName, bool manual = true);
        Task<IndexStatusResponse> EnableIndexAsync(string indexName);
        Task<IndexStatusResponse> DisableIndexAsync(string indexName);
        Task DeleteIndexAsync(string indexName);
        IndexStatusResponse GetIndexStatus(string indexName);
        List<IndexStatusResponse> ListIndexes();
        bool TryGetDefinition(string indexName, out IndexDefinition definition);
        string DataCacheKey(string indexName);
        string EngineCacheKey(string indexName, string hash);
        void EvictEngineCache(string indexName);
        /// <summary>
        /// Cached server-response lookup with per-index LRU cap (Flexmonster semantics):
        /// at most CacheSizeLimit engine responses are cached per index; 0 disables
        /// response caching for all indexes.
        /// </summary>
        Task<T> GetOrCreateEngineAsync<T>(string indexName, string hash, Func<Task<T>> factory) where T : class;
        /// <summary>Cache observability: which indexes are cached vs evicted (spec §2.3 testing aid).</summary>
        CacheStatsResponse GetCacheStats();
        List<IndexFieldDescription> GetFieldMappings(string indexName);
        Task<IndexStatusResponse> SetFieldMappingAsync(string indexName, string fieldName, MappingDataType type);
        Task<IndexStatusResponse> RemoveFieldMappingAsync(string indexName, string fieldName);
    }

    public class IndexManager : IIndexManager
    {
        private readonly IMemoryCache _cache;
        private readonly IDataSourceProviderFactory _providerFactory;
        private readonly IndexStore _store;
        private readonly DataServerSettings _settings;
        private readonly PivotController.Configuration.Models.DataStorageSettings _dataStorage;
        private readonly ILogger<IndexManager> _logger;

        private readonly ConcurrentDictionary<string, IndexDefinition> _indexes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, IndexStatusResponse> _statuses = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _engineLocks = new(StringComparer.OrdinalIgnoreCase);
        // Per-index LRU tracking of cached engine responses (Flexmonster cacheSizeLimit semantics).
        private readonly ConcurrentDictionary<string, LinkedList<string>> _engineLru = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Dictionary<string, LinkedListNode<string>>> _engineLruIndex = new(StringComparer.OrdinalIgnoreCase);

        public IndexManager(IMemoryCache cache, IDataSourceProviderFactory providerFactory, IndexStore store,
            DataServerSettings settings, PivotController.Configuration.Models.DataStorageSettings dataStorage,
            ILogger<IndexManager> logger, bool panelMode = false)
        {
            _cache = cache;
            _providerFactory = providerFactory;
            _store = store;
            _settings = settings;
            _dataStorage = dataStorage;
            _logger = logger;
            _panelMode = panelMode;
        }

        /// <summary>D1/CL-1: in panel mode the panel file is the sole index authority —
        /// a stale persisted indexes.json from a standalone run must not shadow it.</summary>
        private readonly bool _panelMode;

        public string DataCacheKey(string indexName) => $"data:{indexName}";
        public string EngineCacheKey(string indexName, string hash) => $"engine:{indexName}:{hash}";

        public void EvictEngineCache(string indexName)
        {
            // Evict all tracked engine responses for this index (used on reload/mapping change).
            if (_engineLruIndex.TryRemove(indexName, out var nodes))
            {
                foreach (var key in nodes.Keys)
                {
                    _cache.Remove(key);
                }
            }
            _engineLru.TryRemove(indexName, out _);
        }

        public async Task<T> GetOrCreateEngineAsync<T>(string indexName, string hash, Func<Task<T>> factory) where T : class
        {
            var key = EngineCacheKey(indexName, hash);
            if (_cache.TryGetValue(key, out T cached))
            {
                TouchEngine(indexName, key);
                return cached;
            }
            var value = await factory().ConfigureAwait(false);
            if (_dataStorage.CacheSizeLimit > 0)
            {
                _cache.Set(key, value, new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(60)
                });
                TrackEngine(indexName, key, _dataStorage.CacheSizeLimit);
            }
            // CacheSizeLimit == 0: responses are not cached; every request rebuilds.
            return value;
        }

        private void TrackEngine(string indexName, string key, int limit)
        {
            var list = _engineLru.GetOrAdd(indexName, _ => new LinkedList<string>());
            var index = _engineLruIndex.GetOrAdd(indexName, _ => new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase));
            lock (list)
            {
                if (index.TryGetValue(key, out var node))
                {
                    list.Remove(node);
                    list.AddFirst(node);
                    return;
                }
                var newNode = list.AddFirst(key);
                index[key] = newNode;
                while (list.Count > limit)
                {
                    var oldest = list.Last!.Value;
                    list.RemoveLast();
                    index.Remove(oldest);
                    _cache.Remove(oldest);
                }
            }
        }

        private void TouchEngine(string indexName, string key)
        {
            if (!_engineLru.TryGetValue(indexName, out var list)) return;
            if (!_engineLruIndex.TryGetValue(indexName, out var index)) return;
            lock (list)
            {
                if (index.TryGetValue(key, out var node))
                {
                    list.Remove(node);
                    list.AddFirst(node);
                }
            }
        }

        public CacheStatsResponse GetCacheStats()
        {
            // Per-index response cache stats (Flexmonster semantics): count of cached
            // engine responses per index against the configured CacheSizeLimit.
            var cached = new List<string>();
            var evicted = new List<string>();
            foreach (var definition in _indexes.Values)
            {
                var hasData = _cache.TryGetValue(DataCacheKey(definition.Index), out object? _);
                var responseCount = _engineLruIndex.TryGetValue(definition.Index, out var nodes) ? nodes.Count : 0;
                if (hasData || responseCount > 0)
                {
                    cached.Add($"{definition.Index} ({responseCount} responses)");
                }
                else
                {
                    evicted.Add(definition.Index);
                }
            }
            return new CacheStatsResponse
            {
                CacheSizeLimit = _dataStorage.CacheSizeLimit,
                CachedCount = cached.Count,
                CachedIndexes = cached.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
                EvictedIndexes = evicted.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public List<IndexFieldDescription> GetFieldMappings(string indexName)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            var data = _cache.TryGetValue(DataCacheKey(indexName), out List<ExpandoObject> cached)
                ? cached
                : new List<ExpandoObject>();
            var detected = DetectFieldTypes(data);
            var mapped = definition.MappingEnabled == true
                ? (definition.FieldMappings ?? new List<IndexFieldMapping>()).ToDictionary(
                    m => m.FieldName, m => m.Type, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, MappingDataType>(StringComparer.OrdinalIgnoreCase);
            return detected.Select(pair => new IndexFieldDescription
            {
                FieldName = pair.Key,
                DetectedType = pair.Value,
                MappedType = mapped.TryGetValue(pair.Key, out var t) ? ToEngineType(t) : null,
                EffectiveType = mapped.TryGetValue(pair.Key, out var t2) ? ToEngineType(t2) : pair.Value
            }).OrderBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IndexStatusResponse> SetFieldMappingAsync(string indexName, string fieldName, MappingDataType type)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                throw new ArgumentException("Field name cannot be empty.");
            }
            var detected = DetectFieldTypes(GetIndexDataOrEmpty(indexName));
            if (!detected.ContainsKey(fieldName))
            {
                throw new ArgumentException($"Field '{fieldName}' not found in index '{indexName}'. Valid fields: {string.Join(", ", detected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))}");
            }
            definition.MappingEnabled = true;
            definition.FieldMappings ??= new List<IndexFieldMapping>();
            var existing = definition.FieldMappings.FirstOrDefault(m => string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                definition.FieldMappings.Add(new IndexFieldMapping { FieldName = fieldName, Type = type });
            }
            else
            {
                existing.Type = type;
            }
            definition.UpdatedAt = DateTime.UtcNow;
            Persist();
            EvictEngineCache(indexName);
            return await LoadAsync(definition, manual: true).ConfigureAwait(false);
        }

        public async Task<IndexStatusResponse> RemoveFieldMappingAsync(string indexName, string fieldName)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            var mapping = definition.FieldMappings?.FirstOrDefault(m => string.Equals(m.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));
            if (mapping == null)
            {
                throw new KeyNotFoundException($"No field mapping for '{fieldName}' in index '{indexName}'.");
            }
            definition.FieldMappings!.Remove(mapping);
            if (definition.FieldMappings.Count == 0)
            {
                definition.MappingEnabled = false;
            }
            definition.UpdatedAt = DateTime.UtcNow;
            Persist();
            EvictEngineCache(indexName);
            return await LoadAsync(definition, manual: true).ConfigureAwait(false);
        }

        private List<ExpandoObject> GetIndexDataOrEmpty(string indexName)
            => _cache.TryGetValue(DataCacheKey(indexName), out List<ExpandoObject> cached) ? cached : new List<ExpandoObject>();

        private static Dictionary<string, string> DetectFieldTypes(List<ExpandoObject> data)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in data)
            {
                foreach (var pair in (IDictionary<string, object?>)row)
                {
                    if (pair.Value == null)
                    {
                        if (!result.ContainsKey(pair.Key)) result[pair.Key] = "string";
                        continue;
                    }
                    var value = pair.Value.ToString();
                    var type = "string";
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
                    {
                        type = "number";
                    }
                    else if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                    {
                        type = "datetime";
                    }
                    if (!result.TryGetValue(pair.Key, out var current))
                    {
                        result[pair.Key] = type;
                    }
                    else if (current != type && !(current == "number" && type == "string"))
                    {
                        // Mixed or conflicting values fall back to string.
                        result[pair.Key] = "string";
                    }
                }
            }
            return result;
        }

        private static string ToEngineType(MappingDataType type) => type switch
        {
            MappingDataType.Number => "number",
            MappingDataType.Date => "datetime",
            _ => "string"
        };

        /// <summary>Bootstrap: registers indexes from appsettings.json and persisted indexes.json.</summary>
        public async Task RegisterAllAsync()
        {
            _store.EnsureFiles();
            // D1/CL-1: panel mode ignores the persisted {DataFolder}/indexes.json overlay —
            // the panel configuration file is the sole index authority there. Standalone
            // mode keeps the documented merge behavior (persisted entries win).
            var persisted = _panelMode ? new List<IndexDefinition>() : _store.LoadAll();
            foreach (var definition in persisted)
            {
                _indexes[definition.Index] = definition;
            }
            if (_panelMode && System.IO.File.Exists(_store.DataFolder is null ? "" : System.IO.Path.Combine(_store.DataFolder, "indexes.json")))
            {
                _logger.LogInformation("Panel mode: persisted indexes.json overlay ignored (panel configuration file is the sole index authority).");
            }

            foreach (var definition in _settings.Indexes)
            {
                if (_indexes.ContainsKey(definition.Index))
                {
                    _logger.LogInformation("Index '{Index}' already persisted; appsettings declaration skipped.", definition.Index);
                    continue;
                }
                _indexes[definition.Index] = definition;
                _logger.LogInformation("Index '{Index}' registered from the active configuration source (source: {SourceType}, path: {Path}). " +
                    "In panel mode this is the panel configuration file; appsettings.json index declarations are ignored.",
                    definition.Index, definition.SourceType, definition.SourcePath);
            }

            foreach (var definition in _indexes.Values)
            {
                if (!definition.Enabled)
                {
                    SetStatus(definition.Index, IndexStatus.Disabled, 0, null, null);
                    _logger.LogInformation("Index '{Index}' is disabled; data not preloaded.", definition.Index);
                    continue;
                }
                try
                {
                    await LoadAsync(definition, manual: false).ConfigureAwait(false);
                }
                catch (ReloadFailedKeepDataException)
                {
                    // Preload failed but KeepDataOnRefresh retained prior data (none at bootstrap).
                    _logger.LogWarning("Index '{Index}' preload failed; cached data retained.", definition.Index);
                }
                catch (Exception ex)
                {
                    // A bad index definition must not prevent the server from starting;
                    // the index is registered with Failed status and can be fixed/deleted via API.
                    SetStatus(definition.Index, IndexStatus.Failed, 0, null, ex.Message);
                    _logger.LogError(ex, "Index '{Index}' preload failed at startup; status set to Failed.", definition.Index);
                }
            }
        }

        public bool TryGetDefinition(string indexName, out IndexDefinition definition)
            => _indexes.TryGetValue(indexName, out definition!);

        public List<IndexStatusResponse> ListIndexes()
            => _indexes.Values.Select(d => GetIndexStatus(d.Index)).ToList();

        public IndexStatusResponse GetIndexStatus(string indexName)
        {
            if (_statuses.TryGetValue(indexName, out var status))
            {
                return status;
            }
            return new IndexStatusResponse { Index = indexName, Status = IndexStatus.Failed, LastError = "Index not found" };
        }

        public async Task<List<ExpandoObject>> GetDataAsync(string indexName)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            if (!definition.Enabled)
            {
                throw new InvalidOperationException($"Index '{indexName}' is disabled.");
            }
            if (_cache.TryGetValue(DataCacheKey(indexName), out List<ExpandoObject> cached))
            {
                return cached;
            }
            // Lazy load on cache miss (first request, KeepDataOnRefresh retention after a failed
            // reload, or LRU eviction when CacheSizeLimit is small). LoadAsync succeeds unless
            // the source is unreadable, in which case KeepDataOnRefresh already handled it.
            await LoadAsync(definition, manual: false).ConfigureAwait(false);
            if (_cache.TryGetValue(DataCacheKey(indexName), out cached))
            {
                return cached;
            }
            // CacheSizeLimit == 0 disables caching entirely: serve directly without caching.
            var data = await _providerFactory.Create(definition.SourceType)
                .LoadAsync(definition, _store.DataFolder, CancellationToken.None).ConfigureAwait(false);
            return data;
        }

        public async Task<IndexStatusResponse> CreateIndexAsync(IndexDefinition definition)
        {
            var errors = definition.Validate();
            if (errors.Count > 0)
            {
                throw new ArgumentException(string.Join("; ", errors));
            }
            if (_indexes.ContainsKey(definition.Index))
            {
                throw new InvalidOperationException("DUPLICATE");
            }
            definition.CreatedAt = DateTime.UtcNow;
            definition.UpdatedAt = DateTime.UtcNow;
            _indexes[definition.Index] = definition;
            Persist();
            var status = await LoadAsync(definition, manual: true).ConfigureAwait(false);
            return status;
        }

        public async Task<IndexStatusResponse> EditIndexAsync(string indexName, IndexDefinition patch)
        {
            if (!_indexes.TryGetValue(indexName, out var existing))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            if (patch.SourceType != existing.SourceType)
            {
                throw new ArgumentException("SourceType is immutable");
            }

            // Apply patch fields (everything except SourceType). A new Index/Name renames the
            // server key so clients posting /api/pivot/post/{index} follow the edited name.
            var requestedKey = !string.IsNullOrWhiteSpace(patch.Index) ? patch.Index.Trim()
                : !string.IsNullOrWhiteSpace(patch.Name) ? patch.Name.Trim()
                : null;
            if (requestedKey != null
                && !string.Equals(requestedKey, existing.Index, StringComparison.OrdinalIgnoreCase))
            {
                if (_indexes.Keys.Any(k => string.Equals(k, requestedKey, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"Index '{requestedKey}' already exists.");
                }
                _indexes.TryRemove(indexName, out _);
                if (_statuses.TryRemove(indexName, out var priorStatus))
                {
                    priorStatus.Index = requestedKey;
                    _statuses[requestedKey] = priorStatus;
                }
                if (_locks.TryRemove(indexName, out var priorLock))
                {
                    _locks[requestedKey] = priorLock;
                }
                _cache.Remove(DataCacheKey(indexName));
                EvictEngineCache(indexName);
                existing.Index = requestedKey;
                _indexes[requestedKey] = existing;
            }
            else if (requestedKey != null)
            {
                existing.Index = requestedKey;
            }
            existing.Name = string.IsNullOrWhiteSpace(patch.Name) ? existing.Name : patch.Name;
            existing.SourcePath = patch.SourcePath ?? existing.SourcePath;
            existing.CsvDelimiter = string.IsNullOrWhiteSpace(patch.CsvDelimiter) ? existing.CsvDelimiter : patch.CsvDelimiter;
            // Separator enums: enums cannot be null, so a patch always carries a value;
            // apply it unconditionally (defaults match the model defaults when omitted).
            existing.CsvDecimalSeparator = patch.CsvDecimalSeparator;
            existing.CsvThousandsSeparator = patch.CsvThousandsSeparator;
            existing.ConnectionStringName = patch.ConnectionStringName ?? existing.ConnectionStringName;
            existing.Query = patch.Query ?? existing.Query;
            existing.AutoReload = patch.AutoReload || (patch.ReloadIntervalMinutes > 0 && patch.AutoReload) ? patch.AutoReload : existing.AutoReload;
            if (patch.AutoReload)
            {
                existing.AutoReload = true;
                if (patch.ReloadIntervalMinutes > 0)
                {
                    existing.ReloadIntervalMinutes = patch.ReloadIntervalMinutes;
                }
            }
            else if (patch.ReloadIntervalMinutes > 0)
            {
                existing.ReloadIntervalMinutes = patch.ReloadIntervalMinutes;
            }
            if (patch.MappingEnabled.HasValue || patch.FieldMappings != null)
            {
                // Explicit false disables mapping; omitted keeps current state.
                existing.MappingEnabled = patch.MappingEnabled ?? (patch.FieldMappings != null);
                if (existing.MappingEnabled == true)
                {
                    existing.FieldMappings = patch.FieldMappings ?? existing.FieldMappings;
                }
                else
                {
                    existing.FieldMappings = null;
                }
            }
            existing.UpdatedAt = DateTime.UtcNow;
            Persist();

            if (existing.Enabled)
            {
                return await LoadAsync(existing, manual: true).ConfigureAwait(false);
            }
            return GetIndexStatus(existing.Index);
        }

        public async Task<IndexStatusResponse> ReloadIndexAsync(string indexName, bool manual = true)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            return await LoadAsync(definition, manual).ConfigureAwait(false);
        }

        public async Task<IndexStatusResponse> EnableIndexAsync(string indexName)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            if (definition.Enabled)
            {
                return GetIndexStatus(indexName);
            }
            definition.Enabled = true;
            definition.UpdatedAt = DateTime.UtcNow;
            Persist();
            try
            {
                return await LoadAsync(definition, manual: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Spec 0.4.4.3: enable failure -> remain Disabled (no data to keep).
                definition.Enabled = false;
                Persist();
                throw new InvalidOperationException($"Unable to load index '{indexName}': {ex.Message}", ex);
            }
        }

        public Task<IndexStatusResponse> DisableIndexAsync(string indexName)
        {
            if (!_indexes.TryGetValue(indexName, out var definition))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            definition.Enabled = false;
            definition.UpdatedAt = DateTime.UtcNow;
            Persist();
            _cache.Remove(DataCacheKey(indexName));
            EvictEngineCache(indexName);
            SetStatus(indexName, IndexStatus.Disabled, 0, null, null);
            _logger.LogInformation("Index '{Index}' disabled; cache evicted.", indexName);
            return Task.FromResult(GetIndexStatus(indexName));
        }

        public Task DeleteIndexAsync(string indexName)
        {
            if (!_indexes.TryRemove(indexName, out _))
            {
                throw new KeyNotFoundException($"Index '{indexName}' not found.");
            }
            _statuses.TryRemove(indexName, out _);
            _locks.TryRemove(indexName, out _);
            _cache.Remove(DataCacheKey(indexName));
            EvictEngineCache(indexName);
            Persist();
            _logger.LogInformation("Index '{Index}' deleted; metadata and cache removed.", indexName);
            return Task.CompletedTask;
        }

        /// <summary>Loads data for an index with retry and KeepDataOnRefresh semantics.</summary>
        private async Task<IndexStatusResponse> LoadAsync(IndexDefinition definition, bool manual)
        {
            var gate = _locks.GetOrAdd(definition.Index, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                SetStatus(definition.Index, IndexStatus.Refreshing, CurrentRowCount(definition.Index), null, null);
                Exception? lastError = null;
                for (int attempt = 0; attempt <= _dataStorage.RetryAttempts; attempt++)
                {
                    try
                    {
                        var provider = _providerFactory.Create(definition.SourceType);
                        var data = await provider.LoadAsync(definition, _store.DataFolder, CancellationToken.None).ConfigureAwait(false);
                        if (_dataStorage.CacheSizeLimit > 0 && _dataStorage.MaxCacheItemSizeBytes > 0 && EstimateSize(data) > _dataStorage.MaxCacheItemSizeBytes)
                        {
                            // Per-item limit (spec §2.5.2): oversize data is not cached; loaded per request.
                            _cache.Remove(DataCacheKey(definition.Index));
                            _logger.LogWarning("Index '{Index}' data exceeds MaxCacheItemSizeBytes ({Bytes}); not cached.",
                                definition.Index, _dataStorage.MaxCacheItemSizeBytes);
                        }
                        else
                        {
                            _cache.Set(DataCacheKey(definition.Index), data,
                                new MemoryCacheEntryOptions { Size = 1, AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(7) });
                        }
                        var status = SetStatus(definition.Index, IndexStatus.Ready, data.Count, DateTime.UtcNow, null);
                        // Data changed (or was just loaded). Cached pivot engines were built
                        // against the previous dataset and would keep serving stale cells
                        // after Reload, Enable, or a source-file edit. Evict them so the
                        // next pivot request rebuilds from the fresh data cache.
                        EvictEngineCache(definition.Index);
                        _logger.LogInformation("Index '{Index}' loaded {RowCount} rows ({Mode}).",
                            definition.Index, data.Count, manual ? "manual" : "auto/bootstrap");
                        return status;
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException or Newtonsoft.Json.JsonException or InvalidDataException)
                    {
                        lastError = ex;
                        if (attempt < _dataStorage.RetryAttempts)
                        {
                            var delay = TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt) * (0.8 + Random.Shared.NextDouble() * 0.4));
                            _logger.LogWarning("Index '{Index}' load attempt {Attempt}/{Attempts} failed: {Error}. Retrying in {Delay:F0} ms...",
                                definition.Index, attempt + 1, _dataStorage.RetryAttempts, ex.Message, delay.TotalMilliseconds);
                            await Task.Delay(delay).ConfigureAwait(false);
                        }
                    }
                }
                throw lastError ?? new InvalidOperationException("Index load failed.");
            }
            catch (Exception ex)
            {
                if (_dataStorage.KeepDataOnRefresh && _cache.TryGetValue(DataCacheKey(definition.Index), out List<ExpandoObject> old))
                {
                    var status = SetStatus(definition.Index, IndexStatus.Ready, old.Count, GetIndexStatus(definition.Index).LoadedAt, ex.Message);
                    _logger.LogWarning("Index '{Index}' reload failed; serving cached data ({RowCount} rows): {Error}",
                        definition.Index, old.Count, ex.Message);
                    throw new ReloadFailedKeepDataException(status, ex.Message, ex);
                }
                _cache.Remove(DataCacheKey(definition.Index));
                SetStatus(definition.Index, IndexStatus.Failed, 0, null, ex.Message);
                throw;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>Rough in-memory size estimate of an index dataset (spec §2.5).</summary>
        private static long EstimateSize(List<ExpandoObject> data)
        {
            long total = 64; // list overhead
            foreach (var row in data)
            {
                total += 64; // ExpandoObject overhead
                var dict = (IDictionary<string, object?>)row;
                foreach (var key in dict.Keys)
                {
                    total += 2 * key.Length + 8;
                    if (dict[key] is string s) { total += s.Length * 2; }
                    else { total += 16; }
                }
            }
            return total;
        }

        private int CurrentRowCount(string indexName)
            => _cache.TryGetValue(DataCacheKey(indexName), out List<ExpandoObject> data) ? data.Count : 0;

        private IndexStatusResponse SetStatus(string indexName, IndexStatus status, int rowCount, DateTime? loadedAt, string? error)
        {
            var response = new IndexStatusResponse
            {
                Index = indexName,
                Status = status,
                RowCount = rowCount,
                LoadedAt = loadedAt,
                LastError = error
            };
            _statuses[indexName] = response;
            return response;
        }

        private void Persist()
        {
            _store.SaveAll(_indexes.Values.ToList());
        }
    }

    /// <summary>Thrown when reload fails but cached data is retained (spec 0.4.3.2).</summary>
    public class ReloadFailedKeepDataException : Exception
    {
        public IndexStatusResponse Status { get; }
        public ReloadFailedKeepDataException(IndexStatusResponse status, string message, Exception inner)
            : base(message, inner)
        {
            Status = status;
        }
    }
}
