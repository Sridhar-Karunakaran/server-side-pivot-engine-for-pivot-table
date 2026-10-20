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
            return Path.Combine(dataFolder, sourcePath);
        }
    }
}
