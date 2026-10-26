// Standalone Data Server entry point (D1): no argument = standalone mode
// (appsettings.json + {DataFolder} overlays); a panel config path argument or
// the PIVOTCONTROLLER_PANEL_CONFIG environment variable selects panel mode.
using PivotController;

var envPanelConfig = Environment.GetEnvironmentVariable(DataServerHostBuilder.PanelModeEnvVar);
var panelConfigArg = args.Length > 0 ? args[0] : null;
await DataServerHostBuilder.RunAsync(panelConfigArg ?? envPanelConfig, args);
