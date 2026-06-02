using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Services;

public interface IMetadataLookupService
{
    Task<IReadOnlyList<BookMetadata>> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default);
}
