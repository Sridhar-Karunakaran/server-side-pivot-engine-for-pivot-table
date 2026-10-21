using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    /// <summary>
    /// Provider-agnostic registry keyed by DatabaseType (spec §3.x.1). Optional providers
    /// (Oracle, MySQL/MariaDB, MSSQL) return 501 via DatabaseProviderUnavailableException when
    /// their NuGet package is not referenced by this build.
    /// </summary>
    public class DatabaseQueryProviderRegistry : IDatabaseQueryProviderFactory
    {
        public IDatabaseQueryProvider Create(DatabaseType databaseType)
        {
            return databaseType switch
            {
                DatabaseType.Sqlite => new SqliteQueryProvider(),
                DatabaseType.PostgreSQL => new PostgresqlQueryProvider(),
                _ => throw new DatabaseProviderUnavailableException(databaseType),
            };
        }
    }

    public interface IDatabaseQueryProviderFactory
    {
        IDatabaseQueryProvider Create(DatabaseType databaseType);
    }

    public class SqliteQueryProvider : IDatabaseQueryProvider
    {
        public DatabaseType DatabaseType => DatabaseType.Sqlite;

        public DbConnection CreateConnection(string connectionString)
            => new SqliteConnection(connectionString);
    }

    public class PostgresqlQueryProvider : IDatabaseQueryProvider
    {
        public DatabaseType DatabaseType => DatabaseType.PostgreSQL;

        public DbConnection CreateConnection(string connectionString)
            => new NpgsqlConnection(connectionString);
    }

    public class DatabaseProviderUnavailableException : System.Exception
    {
        public DatabaseProviderUnavailableException(DatabaseType dbType)
            : base(ProviderMessage(dbType)) { }

        private static string ProviderMessage(DatabaseType dbType) => dbType switch
        {
            DatabaseType.MSSQL => "Microsoft SQL Server provider requires package 'Microsoft.Data.SqlClient'.",
            DatabaseType.MySql => "MySQL provider requires package 'MySqlConnector'.",
            DatabaseType.MariaDB => "MariaDB provider requires package 'MySqlConnector'.",
            DatabaseType.Oracle => "Oracle provider requires package 'Oracle.ManagedDataAccess.Core'.",
            _ => $"Database provider '{dbType}' is not supported in this build.",
        };
    }
}
