using System.Dynamic;
using Microsoft.Extensions.Logging;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    public interface IDataSourceProviderFactory
    {
        IDataSourceProvider Create(DataSourceType sourceType);
    }

    public class DataSourceProviderFactory : IDataSourceProviderFactory
    {
        private readonly DatabaseDataProvider _databaseProvider;

        public DataSourceProviderFactory(
            DatabaseQuerySettings querySettings,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            ILoggerFactory? loggerFactory = null)
        {
            _databaseProvider = new DatabaseDataProvider(
                new DatabaseQueryProviderRegistry(),
                querySettings,
                configuration,
                loggerFactory?.CreateLogger<DatabaseDataProvider>());
        }

        public IDataSourceProvider Create(DataSourceType sourceType)
        {
            return sourceType switch
            {
                DataSourceType.Json => new JsonDataProvider(),
                DataSourceType.Csv => new CsvDataProvider(),
                DataSourceType.Sqlite or DataSourceType.PostgreSQL
                    or DataSourceType.SqlServer or DataSourceType.MySql
                    or DataSourceType.MariaDb or DataSourceType.Oracle => _databaseProvider,
                _ => throw new NotSupportedException($"SourceType '{sourceType}' is not supported."),
            };
        }
    }
}
