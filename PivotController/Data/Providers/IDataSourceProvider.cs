using System.Dynamic;
using PivotController.Configuration.Models;

namespace PivotController.Data.Providers
{
    public interface IDataSourceProvider
    {
        DataSourceType SourceType { get; }
        Task<List<ExpandoObject>> LoadAsync(IndexDefinition definition, string dataFolder, CancellationToken ct);
    }
}
