using Microsoft.Extensions.Logging;
using PivotController.Configuration.Services;
using PivotController.Core.Persistence;
using PivotController.Core.Security;
using PivotController.Security.Models;

namespace PivotController.Security.Services
{
    /// <summary>
    /// User management (spec §5.4): CRUD with PBKDF2 password hashing. Users are persisted
    /// to {DataFolder}/users.json; password hashes are never returned to API clients.
    /// </summary>
    public class UserService
    {
        private const string FileName = "users.json";

        private readonly AtomicJsonStore _store;
        private readonly GeneralSettingsService _general;
        private readonly PBKDF2PasswordHasher _hasher;
        private readonly ILogger<UserService> _logger;
        private readonly object _sync = new();
        private List<User> _users;

        public UserService(AtomicJsonStore store, GeneralSettingsService general,
            PBKDF2PasswordHasher hasher, ILogger<UserService> logger)
        {
            _store = store;
            _general = general;
            _hasher = hasher;
            _logger = logger;
            _users = new List<User>();
        }

        private string UsersFilePath => Path.Combine(_general.DataFolderPath, FileName);

        /// <summary>Bootstrap: load persisted users at startup.</summary>
        public void Load()
        {
            lock (_sync)
            {
                _users = _store.Load(UsersFilePath, new List<User>());
            }
        }

        public bool AnyUsers()
        {
            lock (_sync) return _users.Count > 0;
        }

        /// <summary>First-user bootstrap (spec §5.2.5): anonymous only while no users exist.</summary>
        public (User? User, List<string> Errors) Bootstrap(string username, string password)
        {
            lock (_sync)
            {
                if (_users.Count > 0)
                {
                    return (null, new List<string> { "Bootstrap is only available while no users exist." });
                }
                return CreateNoLock(username, password);
            }
        }

        public (User? User, List<string> Errors) Create(string username, string password)
        {
            lock (_sync)
            {
                if (_users.Count == 0)
                {
                    // No users yet: creating the first user is the bootstrap flow (allowed anonymous).
                    return CreateNoLock(username, password);
                }
                return CreateNoLock(username, password);
            }
        }

        private (User? User, List<string> Errors) CreateNoLock(string username, string password)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(username) || username.Length > 64)
            {
                errors.Add("Username must be 1-64 characters");
            }
            if (_users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"User '{username}' already exists");
            }
            if (string.IsNullOrEmpty(password) || password.Length < 6)
            {
                errors.Add("Password must be at least 6 characters");
            }
            if (errors.Count > 0) return (null, errors);

            var user = new User { Username = username, PasswordHash = _hasher.Hash(password) };
            _users.Add(user);
            PersistNoLock();
            _logger.LogInformation("User '{Username}' created.", username);
            return (user, errors);
        }

        public List<string> ListUsernames()
        {
            lock (_sync) return _users.Select(u => u.Username).ToList();
        }

        public bool Delete(string username)
        {
            lock (_sync)
            {
                var user = _users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null) return false;
                _users.Remove(user);
                PersistNoLock();
                return true;
            }
        }

        /// <summary>Verifies Basic Auth credentials (spec §5.2.3/5.2.4). Returns null when invalid.</summary>
        public User? VerifyCredentials(string username, string password)
        {
            lock (_sync)
            {
                var user = _users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                if (user == null)
                {
                    // Hash anyway to keep timing uniform
                    _hasher.Verify(password, "PBKDF2$100000$SHA256$AAAA$AAAA");
                    return null;
                }
                return _hasher.Verify(password, user.PasswordHash) ? user : null;
            }
        }

        private void PersistNoLock()
        {
            _store.Save(UsersFilePath, _users);
        }
    }
}