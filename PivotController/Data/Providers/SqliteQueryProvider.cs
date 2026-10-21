using System.Data.Common;
using System.Dynamic;
using Microsoft.Data.Sqlite;
using PivotController.Configuration.Models;
using PivotController.Configuration.Services;

namespace PivotController.Data.Providers
{
    /// <summary>
    /// SQLite data provider (spec §3, clarified scope: SQLite is the mandatory and only
    /// Phase 1 provider; SQL Server/PostgreSQL/MySQL/MariaDB/Oracle are future providers).
    /// Executes IndexDefinition.Query against the SQLite database and returns typed rows.
    /// Command timeout (spec §3.2) is applied when CommandTimeoutEnabled is set.
    /// </summary>
    public class SqliteQueryProvider : IDataSourceProvider
    {
        public const string IndexSourceMarker = "Data Source=";

        private readonly DatabaseQuerySettings _dbQuerySettings;

        public SqliteQueryProvider(DatabaseQuerySettings dbQuerySettings)
        {
            _dbQuerySettings = dbQuerySettings;
        }

        public DataSourceType SourceType => DataSourceType.Sqlite;

        public async Task<List<ExpandoObject>> LoadAsync(IndexDefinition definition, string dataFolder, CancellationToken ct)
        {
            var dbPath = JsonDataProvider.ResolvePath(definition.SourcePath!, dataFolder);
            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = definition.Query ?? "SELECT * FROM " + (definition.ConnectionStringName ?? "Sales");
            if (_dbQuerySettings.CommandTimeoutEnabled && _dbQuerySettings.CommandTimeLimit > 0)
            {
                command.CommandTimeout = _dbQuerySettings.CommandTimeLimit;
            }

            var rows = new List<ExpandoObject>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            do
            {
                while (await reader.ReadAsync(ct))
                {
                    dynamic row = new ExpandoObject();
                    var dict = (IDictionary<string, object?>)row;
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        dict[reader.GetName(i)] = NormalizeValue(value);
                    }
                    rows.Add(row);
                }
            } while (await reader.NextResultAsync(ct));
            return rows;
        }

        /// <summary>Timeout enforcement (spec §3.2.3): long-running queries are canceled.</summary>
        public async Task<int> ExecuteWithTimeoutAsync(string dbPath, string query, CancellationToken ct)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            if (_dbQuerySettings.CommandTimeoutEnabled)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_dbQuerySettings.CommandTimeLimit));
                try
                {
                    return await command.ExecuteNonQueryAsync(timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"Query exceeded CommandTimeLimit of {_dbQuerySettings.CommandTimeLimit} seconds");
                }
            }
            return await command.ExecuteNonQueryAsync(ct);
        }

        private static object? NormalizeValue(object? value)
        {
            if (value == null) return null;
            // SQLite stores integers and floats natively; dates may arrive as text.
            if (value is string s && DateTime.TryParse(s, out var dt))
            {
                return dt;
            }
            return value;
        }
    }
}