using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;

namespace BookDB.Logic.Services;

public interface IBookMetadataService
{
    Task<Book?> FindBookByIsbnAsync(string isbn, CancellationToken ct = default);
    Task<Book> AddBookFromMetadataAsync(BookMetadata merged, byte[]? cover, int? collectionId, CancellationToken ct = default);
    Task UpdateBookFromMetadataAsync(int bookId, BookMetadata merged, byte[]? cover, CancellationToken ct = default);
}
