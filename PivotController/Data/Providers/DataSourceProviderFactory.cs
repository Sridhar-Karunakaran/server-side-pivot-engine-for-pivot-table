using System.Dynamic;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    public interface IDataSourceProviderFactory
    {
        IDataSourceProvider Create(DataSourceType sourceType);
    }

    public class DataSourceProviderFactory : IDataSourceProviderFactory
    {
        private readonly Dictionary<DataSourceType, IDataSourceProvider> _providers;

        public DataSourceProviderFactory(PivotController.Configuration.Models.DatabaseQuerySettings dbQuerySettings)
        {
            _providers = new Dictionary<DataSourceType, IDataSourceProvider>
            {
                [DataSourceType.Json] = new JsonDataProvider(),
                [DataSourceType.Csv] = new CsvDataProvider(),
                // SQLite is the mandatory Phase 1 database provider (spec §3, clarified scope).
                [DataSourceType.Sqlite] = new SqliteQueryProvider(dbQuerySettings),
            };
        }

        public IDataSourceProvider Create(DataSourceType sourceType)
        {
            if (_providers.TryGetValue(sourceType, out var provider))
            {
                return provider;
            }
            throw new NotSupportedException($"SourceType '{sourceType}' is not supported. Phase 1 supports Json, Csv and Sqlite; SQL Server/PostgreSQL/MySQL/MariaDB/Oracle are planned future providers.");
        }
    }
}
