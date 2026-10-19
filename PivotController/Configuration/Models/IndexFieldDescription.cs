using System.Dynamic;

namespace PivotController.Configuration.Models
{
    /// <summary>One field of an indexed data source with its detected and mapped data types.</summary>
    public class IndexFieldDescription
    {
        public string FieldName { get; set; } = string.Empty;
        /// <summary>Type detected from the index's loaded data ("string", "number", "datetime").</summary>
        public string DetectedType { get; set; } = "string";
        /// <summary>Type set in the Data Server via field mapping; null when unmapped (autodetect).</summary>
        public string? MappedType { get; set; }
        /// <summary>Type the pivot engine will use: the mapping when set, else the detected type.</summary>
        public string EffectiveType { get; set; } = "string";
    }
}