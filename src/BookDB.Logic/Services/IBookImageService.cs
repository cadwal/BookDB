using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;

namespace BookDB.Logic.Services;

public interface IBookImageService
{
    Task<byte[]?> GetBookPrimaryCoverBytesAsync(int bookId, CancellationToken ct = default);
    Task SavePrimaryBookImageAsync(int bookId, byte[] imageData, CancellationToken ct = default);
    Task RemovePrimaryBookImageAsync(int bookId, CancellationToken ct = default);
    Task<IReadOnlyList<BookImage>> GetBookImagesAsync(int bookId, CancellationToken ct = default);
    Task SaveBookImageByTypeAsync(int bookId, int imageTypeId, byte[] data, CancellationToken ct = default);
    Task RemoveBookImageByTypeAsync(int bookId, int imageTypeId, CancellationToken ct = default);
    Task<byte[]?> GetBookThumbnailBytesAsync(int bookId, CancellationToken ct = default);
    Task ReorderBookImageAsync(int bookId, int imageId, int newDisplayOrder, CancellationToken ct = default);
    Task ReassignBookImageTypeAsync(int bookId, int imageId, int newTypeId, CancellationToken ct = default);
    Task<byte[]?> GetBookImageBytesAsync(int bookId, int imageId, CancellationToken ct = default);
    Task RemoveBookImageByIdAsync(int bookId, int imageId, CancellationToken ct = default);
}
