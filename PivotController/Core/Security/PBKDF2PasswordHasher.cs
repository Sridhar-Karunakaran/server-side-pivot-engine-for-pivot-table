using System.Security.Cryptography;

namespace PivotController.Core.Security
{
    /// <summary>
    /// PBKDF2 password hashing (spec §5.4.1): hash format
    /// PBKDF2$100000$SHA256$saltB64$hashB64; constant-time verification.
    /// </summary>
    public class PBKDF2PasswordHasher
    {
        private const int Iterations = 100_000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        public string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return $"PBKDF2${Iterations}$SHA256${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>Verifies a password against a stored hash in constant time.</summary>
        public bool Verify(string password, string storedHash)
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 5 || parts[0] != "PBKDF2") return false;
            if (!int.TryParse(parts[1], out var iterations)) return false;
            if (parts[2] != "SHA256") return false;
            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[3]);
                expected = Convert.FromBase64String(parts[4]);
            }
            catch (FormatException)
            {
                return false;
            }
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return ConstantTimeComparer.AreEqual(actual, expected);
        }
    }

    /// <summary>Constant-time byte-array comparison to prevent timing attacks.</summary>
    public static class ConstantTimeComparer
    {
        public static bool AreEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}