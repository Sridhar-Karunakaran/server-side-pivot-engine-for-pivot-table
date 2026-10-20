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
        private readonly Dictionary<DataSourceType, IDataSourceProvider> _providers = new()
        {
            [DataSourceType.Json] = new JsonDataProvider(),
            [DataSourceType.Csv] = new CsvDataProvider(),
        };

        public IDataSourceProvider Create(DataSourceType sourceType)
        {
            if (_providers.TryGetValue(sourceType, out var provider))
            {
                return provider;
            }
            throw new NotSupportedException($"SourceType '{sourceType}' is not supported yet (database indexes arrive in a later phase).");
        }
    }
}
