using Newtonsoft.Json;

namespace PivotController.Core.Persistence
{
    /// <summary>Atomic JSON file store: temp-file-rename writes, corruption quarantine on read.</summary>
    public class AtomicJsonStore
    {
        private readonly ILogger<AtomicJsonStore> _logger;

        public AtomicJsonStore(ILogger<AtomicJsonStore> logger)
        {
            _logger = logger;
        }

        public T Load<T>(string filePath, T defaultValue)
        {
            if (!File.Exists(filePath))
            {
                return defaultValue;
            }
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<T>(json) ?? defaultValue;
            }
            catch (Exception ex)
            {
                var quarantine = $"{filePath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
                try { File.Move(filePath, quarantine); } catch { /* best effort */ }
                _logger.LogWarning(ex, "Corrupt JSON file '{File}' quarantined as '{Quarantine}'; defaults used.", filePath, quarantine);
                return defaultValue;
            }
        }

        public void Save<T>(string filePath, T data)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temp = filePath + ".tmp";
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(temp, json);
            if (File.Exists(filePath))
            {
                File.Replace(temp, filePath, null);
            }
            else
            {
                File.Move(temp, filePath);
            }
        }
    }
}
