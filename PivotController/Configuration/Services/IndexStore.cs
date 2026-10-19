using Newtonsoft.Json;
using PivotController.Configuration.Models;
using PivotController.Core.Persistence;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Loads index definitions from appsettings.json (DataServer:Indexes) at startup and
    /// persists runtime changes to {DataFolder}/indexes.json. appsettings indexes are
    /// merged with (but not overridden by) indexes.json entries created via the API.
    /// </summary>
    public class IndexStore
    {
        private readonly AtomicJsonStore _store;
        private readonly string _indexesPath;
        private readonly ILogger<IndexStore> _logger;
        private readonly object _sync = new();

        public IndexStore(AtomicJsonStore store, DataServerSettings settings, ILogger<IndexStore> logger, IWebHostEnvironment environment)
        {
            _store = store;
            _logger = logger;
            // Data files live under {ContentRoot}/DataSource (sample data files); runtime index
            // metadata is persisted to {DataFolder}/indexes.json.
            DataFolder = Path.Combine(environment.ContentRootPath, "DataSource");
            _indexesPath = Path.Combine(DataFolder, "indexes.json");
        }

        public string DataFolder { get; }

        public List<IndexDefinition> LoadAll()
        {
            lock (_sync)
            {
                return _store.Load(_indexesPath, new List<IndexDefinition>());
            }
        }

        public void SaveAll(List<IndexDefinition> definitions)
        {
            lock (_sync)
            {
                _store.Save(_indexesPath, definitions);
            }
        }

        public void EnsureFiles()
        {
            Directory.CreateDirectory(DataFolder);
            if (!File.Exists(_indexesPath))
            {
                _store.Save(_indexesPath, new List<IndexDefinition>());
            }
            _logger.LogInformation("Data Server index store initialized at {Path}", Path.GetFullPath(_indexesPath));
        }
    }
}
