using System.Data.Common;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    /// <summary>
    /// Provider-agnostic abstraction for creating ADO.NET connections to a database system.
    /// One implementation per DatabaseType; everything downstream (timeout, retry,
    /// row mapping, indexing, caching) is shared DatabaseDataProvider code.
    /// Per design.md §9 and spec §3.x.
    /// </summary>
    public interface IDatabaseQueryProvider
    {
        DatabaseType DatabaseType { get; }

        /// <summary>Creates a connection from the connection string. Must not open it.</summary>
        DbConnection CreateConnection(string connectionString);
    }
}
