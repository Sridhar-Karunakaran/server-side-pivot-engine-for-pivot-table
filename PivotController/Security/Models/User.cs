namespace PivotController.Security.Models
{
    /// <summary>A Data Server user (spec §5.4): username + PBKDF2 password hash. Never serialized to API responses.</summary>
    public class User
    {
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string Role { get; set; } = "admin";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}