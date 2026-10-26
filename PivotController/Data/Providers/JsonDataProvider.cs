using System.Dynamic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    public class JsonDataProvider : IDataSourceProvider
    {
        public DataSourceType SourceType => DataSourceType.Json;

        public async Task<List<ExpandoObject>> LoadAsync(IndexDefinition definition, string dataFolder, CancellationToken ct)
        {
            var path = ResolvePath(definition.SourcePath!, dataFolder);
            using var reader = new StreamReader(path);
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var token = JToken.Parse(json);
            if (token is not JArray array)
            {
                throw new InvalidDataException($"JSON source '{definition.SourcePath}' must be an array of objects.");
            }
            var result = new List<ExpandoObject>(array.Count);
            foreach (var item in array.OfType<JObject>())
            {
                dynamic row = new ExpandoObject();
                var dict = (IDictionary<string, object?>)row;
                foreach (var prop in item.Properties())
                {
                    dict[prop.Name] = ConvertToken(prop.Value);
                }
                result.Add(row);
            }
            return result;
        }

        private static object? ConvertToken(JToken token)
        {
            if (token is JValue value && value.Value is not null)
            {
                if (value.Type == JTokenType.Integer) return Convert.ToInt64(value.Value);
                if (value.Type == JTokenType.Float) return Convert.ToDouble(value.Value);
                if (value.Type == JTokenType.Date) return value.Value;
                return value.Value;
            }
            return token?.ToString();
        }

        internal static string ResolvePath(string sourcePath, string dataFolder)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(sourcePath, @"^https?://", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return sourcePath;
            }
            if (Path.IsPathRooted(sourcePath))
            {
                return sourcePath;
            }
            var copied = Path.Combine(dataFolder, sourcePath);
            // Sample files are copied to the app output at build time. Editing the
            // project DataSource file (or any ancestor copy) while the server is
            // running would otherwise be invisible to Reload and to Stop/Start.
            // Prefer the newest existing copy of the same relative path.
            return NewestExisting(copied, sourcePath);
        }

        /// <summary>
        /// Walks up from the process directory looking for the same relative path
        /// (project DataSource next to the .csproj) and returns whichever copy was
        /// written last. Falls back to the output-directory path.
        /// </summary>
        internal static string NewestExisting(string outputPath, string relativePath)
        {
            var best = outputPath;
            DateTime bestWrite = File.Exists(outputPath) ? File.GetLastWriteTimeUtc(outputPath) : DateTime.MinValue;
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, relativePath);
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }
                    var write = File.GetLastWriteTimeUtc(candidate);
                    if (write > bestWrite)
                    {
                        best = candidate;
                        bestWrite = write;
                    }
                }
            }
            catch (IOException)
            {
                // Unreadable ancestor: keep the output copy.
            }
            return best;
        }
    }
}
