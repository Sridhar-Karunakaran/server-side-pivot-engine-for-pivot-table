using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace PivotController.Reports.Services
{
    /// <summary>
    /// Builds the client-side pivot table report structure (camelCase, string enums,
    /// no internalProperties) from the server-side FetchData request — so a stored
    /// report matches what a client would configure (spec §6.3.1, user direction).
    /// </summary>
    public static class ClientReportBuilder
    {
        /// <summary>
        /// Serializer settings: camelCase property casing (AllowLabelFilter →
        /// allowLabelFilter), enums as strings (GrandTotalsPosition → "Top"/"Bottom",
        /// not 0), nulls omitted.
        /// </summary>
        public static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Wraps a server-side DataOptions (dataSourceSettings) into the client-side
        /// report JSON: { "dataSourceSettings": { ...camelCase... } }.
        /// internalProperties is intentionally NOT included (user direction).
        /// </summary>
        public static string Build(object? serverDataSourceSettings)
        {
            var report = new JObject
            {
                ["dataSourceSettings"] =
                    serverDataSourceSettings == null
                        ? new JObject()
                        : JObject.FromObject(serverDataSourceSettings,
                            JsonSerializer.Create(DefaultSettings))
            };
            return report.ToString(Formatting.None);
        }

        private static JsonSerializerSettings DefaultSettings => Settings;
    }
}