using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Metadata;

namespace BookDB.MetadataSources.Sources;

public interface IMetadataSource
{
    string SourceName { get; }
    Task<BookMetadata?> FetchAsync(string isbn, CancellationToken ct = default);
}
