using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Configuration.Services
{
    /// <summary>
    /// Watches {DataFolder}/config.json for external changes (spec §1.3.5) and applies
    /// MinimumLogLevel immediately. Port/DataFolder changes in the file are logged as
    /// requiring restart.
    /// </summary>
    public class GeneralSettingsFileWatcher : BackgroundService
    {
        private readonly GeneralSettingsService _general;
        private readonly GeneralSettings _settings;
        private readonly ILogger<GeneralSettingsFileWatcher> _logger;

        public GeneralSettingsFileWatcher(GeneralSettingsService general, GeneralSettings settings,
            ILogger<GeneralSettingsFileWatcher> logger)
        {
            _general = general;
            _settings = settings;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var path = _general.ConfigFilePath;
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            using var watcher = new FileSystemWatcher(directory)
            {
                Filter = Path.GetFileName(path),
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, _) => OnConfigChanged();
            _logger.LogInformation("Watching general config file '{Path}'.", path);
            // Keep the watcher alive until shutdown. The watcher is disposed by the using
            // statement when the task completes; Task.Delay(-1) blocks until cancellation.
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested.
            }
        }

        private void OnConfigChanged()
        {
            try
            {
                var json = File.ReadAllText(_general.ConfigFilePath);
                var file = JsonConvert.DeserializeObject<GeneralSettingsService.GeneralSettingsConfigFile>(json);
                if (file == null)
                {
                    return;
                }
                if (Enum.TryParse<LogLevelOption>(file.MinimumLogLevel, ignoreCase: true, out var level)
                    && Enum.IsDefined(level) && level != _settings.MinimumLogLevel)
                {
                    _general.ApplyLogLevel(level);
                    _logger.LogInformation("MinimumLogLevel hot-reloaded to {Level} from config.json.", level);
                }
                if (file.Port != _settings.Port || !string.Equals(file.DataFolder, _settings.DataFolder, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("config.json change to Port/DataFolder requires a restart to take effect.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to hot-reload general settings from config.json.");
            }
        }
    }
}