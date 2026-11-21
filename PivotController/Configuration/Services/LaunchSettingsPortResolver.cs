using System.Text.Json;

namespace PivotController.Configuration.Services
{
    public static class LaunchSettingsPortResolver
    {
        public static int? ResolveApplicationUrl(string? applicationUrl)
        {
            return applicationUrl is not null && TryGetPort(applicationUrl, out var port)
                ? port
                : null;
        }

        public static int? Resolve(string launchSettingsPath, string? profileName)
        {
            if (!File.Exists(launchSettingsPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
            var root = document.RootElement;

            if (!string.IsNullOrWhiteSpace(profileName)
                && TryGetObject(root, "profiles", out var profiles)
                && TryGetObject(profiles, profileName, out var profile)
                && TryGetApplicationUrl(profile, out var profileUrl)
                && TryGetPort(profileUrl, out var profilePort))
            {
                return profilePort;
            }

            if (string.IsNullOrWhiteSpace(profileName)
                && TryGetObject(root, "profiles", out var defaultProfiles))
            {
                foreach (var candidate in defaultProfiles.EnumerateObject())
                {
                    if (TryGetApplicationUrl(candidate.Value, out var applicationUrl)
                        && TryGetPort(applicationUrl, out var port))
                    {
                        return port;
                    }
                }
            }

            if (TryGetObject(root, "iisSettings", out var iisSettings)
                && TryGetObject(iisSettings, "iisExpress", out var iisExpress))
            {
                if (TryGetApplicationUrl(iisExpress, out var iisUrl)
                    && TryGetPort(iisUrl, out var iisPort))
                {
                    return iisPort;
                }

                if (iisExpress.TryGetProperty("sslPort", out var sslPort)
                    && int.TryParse(sslPort.ToString(), out var parsedSslPort)
                    && parsedSslPort > 0)
                {
                    return parsedSslPort;
                }
            }

            if (TryGetObject(root, "profiles", out var allProfiles))
            {
                foreach (var candidate in allProfiles.EnumerateObject())
                {
                    if (TryGetApplicationUrl(candidate.Value, out var applicationUrl)
                        && TryGetPort(applicationUrl, out var port))
                    {
                        return port;
                    }
                }
            }

            return null;
        }

        private static bool TryGetObject(JsonElement parent, string propertyName, out JsonElement value)
        {
            value = default;
            return parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty(propertyName, out value)
                && value.ValueKind == JsonValueKind.Object;
        }

        private static bool TryGetApplicationUrl(JsonElement settings, out string applicationUrl)
        {
            applicationUrl = string.Empty;
            if (settings.ValueKind != JsonValueKind.Object
                || !settings.TryGetProperty("applicationUrl", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            applicationUrl = value.GetString() ?? string.Empty;
            return applicationUrl.Length > 0;
        }

        private static bool TryGetPort(string applicationUrl, out int port)
        {
            foreach (var address in applicationUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri)
                    && uri.Port > 0)
                {
                    port = uri.Port;
                    return true;
                }
            }

            port = 0;
            return false;
        }
    }
}
