namespace PivotController.Configuration.Models
{
    /// <summary>Request body for PUT /api/indexes/{index}/fields/{fieldName}.</summary>
    public class SetFieldTypeRequest
    {
        /// <summary>One of: Number, String, Date.</summary>
        public string? Type { get; set; }
    }
}