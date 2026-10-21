namespace PivotController.Configuration.Models
{
    /// <summary>Database query settings per spec §3.1 / §3.x.</summary>
    public class DatabaseQuerySettings
    {
        public bool CommandTimeoutEnabled { get; set; } = false;
        public int CommandTimeLimit { get; set; } = 150; // seconds
    }
}
