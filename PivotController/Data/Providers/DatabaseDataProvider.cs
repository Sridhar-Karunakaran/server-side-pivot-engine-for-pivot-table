using System.Data;
using System.Data.Common;
using System.Dynamic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    /// <summary>
    /// Executes an index's SQL query against the database selected by DatabaseType and maps
    /// results to ExpandoObject rows — the identical shape produced by JsonDataProvider/
    /// CsvDataProvider — so the downstream IndexManager pipeline (filtering, sorting,
    /// aggregation, caching, report generation) works unchanged (spec §3.x).
    /// </summary>
    public class DatabaseDataProvider : IDataSourceProvider
    {
        private readonly IDatabaseQueryProviderFactory _providerFactory;
        private readonly DatabaseQuerySettings _querySettings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseDataProvider>? _logger;

        public DatabaseDataProvider(
            IDatabaseQueryProviderFactory providerFactory,
            IOptions<DatabaseQuerySettings> querySettings,
            IConfiguration configuration,
            ILogger<DatabaseDataProvider>? logger = null)
        {
            _providerFactory = providerFactory;
            _querySettings = querySettings.Value;
            _configuration = configuration;
            _logger = logger;
        }

        public DataSourceType SourceType => DataSourceType.Json; // never routed via SourceType; registered per DB source type

        public async Task<List<ExpandoObject>> LoadAsync(IndexDefinition definition, string dataFolder, CancellationToken ct)
        {
            if (definition.DatabaseType is not { } dbType)
            {
                throw new InvalidOperationException("DatabaseType is required for database indexes.");
            }
            if (string.IsNullOrWhiteSpace(definition.Query))
            {
                throw new InvalidOperationException("Query is required for database indexes.");
            }
            ValidateQuery(definition.Query);

            var provider = _providerFactory.Create(dbType);
            var connectionString = ResolveConnectionString(definition.ConnectionStringName, definition.ConnectionString);

            await using var connection = provider.CreateConnection(connectionString);
            if (connection == null) throw new InvalidOperationException("Provider returned no connection.");

            _logger?.LogInformation("Opening {DatabaseType} connection for index '{Index}'", dbType, definition.Index);
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = definition.Query;
            if (_querySettings.CommandTimeoutEnabled)
            {
                command.CommandTimeout = _querySettings.CommandTimeLimit; // 0 = infinite
            }

            try
            {
                var rows = await ReadRowsAsync(command, ct).ConfigureAwait(false);
                _logger?.LogInformation("Index '{Index}' fetched {RowCount} rows from {DatabaseType}", definition.Index, rows.Count, dbType);
                return rows;
            }
            catch (Exception ex) when (IsTimeout(ex))
            {
                throw new TimeoutException($"Query for index '{definition.Index}' exceeded the command timeout ({_querySettings.CommandTimeLimit}s).", ex);
            }
        }

        private static async Task<List<ExpandoObject>> ReadRowsAsync(DbCommand command, CancellationToken ct)
        {
            var rows = new List<ExpandoObject>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            do
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    rows.Add(MapRow(reader));
                }
            } while (await reader.NextResultAsync(ct).ConfigureAwait(false));
            return rows;
        }

        private static ExpandoObject MapRow(DbDataReader reader)
        {
            dynamic row = new ExpandoObject();
            var dict = (IDictionary<string, object?>)row;
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException($"Query returned a blank column name at ordinal {i}.");
                }
                if (dict.ContainsKey(name))
                {
                    throw new InvalidDataException($"Query returned duplicate column name '{name}'.");
                }
                dict[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            return row;
        }

        /// <summary>
        /// Resolves the connection string: inline ConnectionString on the index takes precedence
        /// (Flexmonster-style, spec §3.x.2), falling back to ConnectionStringName from the
        /// ConnectionStrings section. Values are never logged or echoed in errors.
        /// </summary>
        public string ResolveConnectionString(string? connectionStringName, string? inlineConnectionString = null)
        {
            if (!string.IsNullOrWhiteSpace(inlineConnectionString))
            {
                return inlineConnectionString;
            }
            if (string.IsNullOrWhiteSpace(connectionStringName))
            {
                throw new InvalidOperationException("ConnectionString or ConnectionStringName is required for database indexes.");
            }
            var value = _configuration.GetConnectionString(connectionStringName);
            if (string.IsNullOrWhiteSpace(value))
            {
                // Never echo any connection string value in errors (spec §3.x.2.2).
                throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured in ConnectionStrings.");
            }
            return value;
        }

        /// <summary>Single read-only SELECT only; no multi-statements; no DML/DDL (spec §3.x.3).</summary>
        public static void ValidateQuery(string query)
        {
            var q = query.Trim();
            if (q.Contains(';'))
            {
                throw new InvalidOperationException("Query must be a single read-only SELECT statement.");
            }
            var startsWithSelect = q.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                                   || q.StartsWith("WITH", StringComparison.OrdinalIgnoreCase); // CTE read-only SELECT
            if (!startsWithSelect)
            {
                throw new InvalidOperationException("Query must be a single read-only SELECT statement.");
            }
            var keywords = new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "GRANT", "TRUNCATE", "REPLACE" };
            foreach (var kw in keywords)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(q, $"\\b{kw}\\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    throw new InvalidOperationException($"Query must be a single read-only SELECT statement (forbidden keyword: {kw}).");
                }
            }
        }

        private static bool IsTimeout(Exception ex)
            => ex is TimeoutException
               || ex.Message?.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0
               || (ex.InnerException != null && IsTimeout(ex.InnerException));

        /// <summary>Open connection + read the first row only; never loads or persists data (spec §3.x.5).</summary>
        public async Task<(bool Success, string? Error, long LatencyMs, List<string> Columns)> TestConnectionAsync(
            DatabaseType databaseType, string? connectionStringName, string? query, CancellationToken ct,
            string? inlineConnectionString = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var provider = _providerFactory.Create(databaseType);
                var connectionString = ResolveConnectionString(connectionStringName, inlineConnectionString);
                var validated = (query ?? "SELECT 1").Trim();
                ValidateQuery(validated);

                await using var connection = provider.CreateConnection(connectionString);
                await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = validated;
                await using var reader = await command.ExecuteReaderAsync(
                    CommandBehavior.SingleRow, ct).ConfigureAwait(false);
                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
                sw.Stop();
                return (true, null, sw.ElapsedMilliseconds, columns);
            }
            catch (DatabaseProviderUnavailableException)
            {
                throw; // surfaced as 501 by the controller
            }
            catch (Exception ex)
            {
                sw.Stop();
                // Connection strings/credentials never surfaced (only provider error text, sanitized).
                var message = ex.Message;
                if (message?.IndexOf(";", StringComparison.Ordinal) >= 0 && ex is not InvalidOperationException && ex is not DatabaseProviderUnavailableException)
                {
                    // ADO.NET connect failures sometimes embed the connection string; strip it.
                    message = ex.GetType().Name + " while connecting to the database.";
                }
                return (false, message, sw.ElapsedMilliseconds, new List<string>());
            }
        }
    }
}
