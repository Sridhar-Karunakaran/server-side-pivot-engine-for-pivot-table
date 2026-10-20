using System.Dynamic;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    /// <summary>
    /// CSV provider honoring CsvDelimiter, CsvDecimalSeparator, CsvThousandsSeparator (spec §0.6)
    /// and optional per-field type mapping (spec §0.7).
    /// </summary>
    public class CsvDataProvider : IDataSourceProvider
    {
        public DataSourceType SourceType => DataSourceType.Csv;

        public async Task<List<ExpandoObject>> LoadAsync(IndexDefinition definition, string dataFolder, CancellationToken ct)
        {
            var path = JsonDataProvider.ResolvePath(definition.SourcePath!, dataFolder);
            using var reader = new StreamReader(path);
            var rows = new List<List<string>>();
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    rows.Add(SplitCsvLine(line, definition.CsvDelimiter));
                }
            }
            if (rows.Count == 0)
            {
                return new List<ExpandoObject>();
            }

            var header = rows[0];
            var mappings = definition.MappingEnabled == true
                ? definition.FieldMappings?.ToDictionary(m => m.FieldName, m => m.Type, StringComparer.OrdinalIgnoreCase)
                : null;

            var result = new List<ExpandoObject>(rows.Count - 1);
            for (int i = 1; i < rows.Count; i++)
            {
                dynamic row = new ExpandoObject();
                var dict = (IDictionary<string, object?>)row;
                for (int c = 0; c < header.Count; c++)
                {
                    var field = header[c];
                    var raw = c < rows[i].Count ? rows[i][c] : null;
                    dict[field] = ConvertValue(raw, field, mappings, definition);
                }
                result.Add(row);
            }
            return result;
        }

        internal static List<string> SplitCsvLine(string line, string delimiter)
        {
            var values = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            var delim = delimiter.Length == 0 ? "," : delimiter;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (!inQuotes && i + delim.Length <= line.Length && line.Substring(i, delim.Length) == delim)
                {
                    values.Add(current.ToString());
                    current.Clear();
                    i += delim.Length - 1;
                }
                else
                {
                    current.Append(ch);
                }
            }
            values.Add(current.ToString());
            return values;
        }

        internal static object? ConvertValue(string? raw, string field,
            Dictionary<string, MappingDataType>? mappings, IndexDefinition definition)
        {
            if (raw == null) return null;
            if (mappings != null && mappings.TryGetValue(field, out var mapped))
            {
                try
                {
                    return mapped switch
                    {
                        MappingDataType.Number => ParseNumber(raw, definition),
                        MappingDataType.Date => DateTime.Parse(raw),
                        _ => raw
                    };
                }
                catch
                {
                    return raw;
                }
            }
            return AutoDetect(raw, definition);
        }

        private static object? AutoDetect(string raw, IndexDefinition definition)
        {
            if (long.TryParse(raw, out var l)) return l;
            // Numeric detection must honor the configured separators, e.g. "1.234,56"
            // (dec=Comma, thou=Dot) or "1 234 567,89" (thou=Space).
            if (TryParseNumber(raw, definition, out var configured))
            {
                return configured;
            }
            if (double.TryParse(raw, out var d)) return d;
            if (DateTime.TryParse(raw, out var dt)) return dt;
            return raw;
        }

        internal static bool TryParseNumber(string raw, IndexDefinition definition, out double value)
        {
            value = 0;
            try
            {
                value = ParseNumber(raw, definition);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static double ParseNumber(string raw, IndexDefinition definition)
        {
            var dec = definition.CsvDecimalSeparator.ToChar();
            var thou = definition.CsvThousandsSeparator.ToChar();
            // Strip the thousands separator first, then normalize the decimal separator.
            if (thou == dec)
            {
                // Same symbol configured for both (e.g. "," for both): treat groups as thousands
                // only when the remaining digits after the last separator are exactly 3.
                return ParseAmbiguous(raw, dec);
            }
            raw = raw.Replace(thou.ToString(), string.Empty);
            if (dec != '.')
            {
                raw = raw.Replace(".", string.Empty).Replace(dec, '.');
            }
            return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static double ParseAmbiguous(string raw, char separator)
        {
            var last = raw.LastIndexOf(separator);
            if (last > -1 && raw.Length - last - 1 == 3 && raw.Length - 1 != 3)
            {
                // "1,234,567" style — all occurrences are thousands separators.
                raw = raw.Replace(separator.ToString(), string.Empty);
                return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            if (separator != '.')
            {
                raw = raw.Replace(separator, '.');
            }
            return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
