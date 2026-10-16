using PivotController.Configuration.Models;

namespace PivotController.Indexing;

public enum IndexStatus
{
    Ready,
    Refreshing,
    Failed,
    Disabled
}

public sealed class IndexSnapshot
{
    public required string Index { get; init; }
    public required string Name { get; init; }
    public required string SourceType { get; init; }
    public string? SourcePath { get; init; }
    public string? DatabaseType { get; init; }
    public string? ConnectionStringName { get; init; }
    public string? Query { get; init; }
    public required bool MappingEnabled { get; init; }
    public List<IndexFieldMapping>? FieldMappings { get; init; }
    public required bool AutoReload { get; init; }
    public required int ReloadIntervalMinutes { get; init; }
    public required bool Enabled { get; init; }
    public required IndexStatus Status { get; init; }
    public int? RowCount { get; init; }
    public DateTime? LoadedAt { get; init; }
    public string? LastError { get; init; }
}

public interface IIndexManager
{
    DataServerConfiguration Config { get; }
    IndexSnapshot? Resolve(string? indexName);
    IndexSnapshot? Find(string indexName);
    IReadOnlyList<IndexSnapshot> List();
    Task<IndexSnapshot> CreateAsync(IndexDefinition definition);
    Task<IndexSnapshot> EditAsync(string indexName, Action<IndexDefinition> mutate);
    Task<IndexSnapshot> ReloadAsync(string indexName);
    Task<IndexSnapshot> SetEnabledAsync(string indexName, bool enabled);
    void Delete(string indexName);
    Task<(bool Success, long LatencyMs, string? Error)> TestConnectionAsync(string indexName);
    Task<IReadOnlyList<System.Dynamic.ExpandoObject>> GetDataAsync(string indexName, CancellationToken cancellationToken = default);
    string DataKey(string indexName);
    string EngineKeyPrefix(string indexName);
}
