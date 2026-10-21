using System.Text.Json.Serialization;

namespace PivotController.Configuration.Models
{
    /// <summary>Probe payload for POST /api/indexes/test-connection (spec §3.x.5).</summary>
    public class TestConnectionRequest
    {
        [JsonPropertyName("databaseType")]
        [JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
        public DatabaseType? DatabaseType { get; set; }

        [JsonPropertyName("connectionStringName")]
        public string? ConnectionStringName { get; set; }

        [JsonPropertyName("query")]
        public string? Query { get; set; }
    }
}
