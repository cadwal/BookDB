using System.Threading;
using System.Threading.Tasks;

namespace BookDB.MetadataSources.Services;

public interface IMetadataLookupService
{
    Task<MetadataLookupResult> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default);
}
