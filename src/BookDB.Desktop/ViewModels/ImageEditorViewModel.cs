using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class ImageEditorViewModel : ObservableObject
{
    private readonly IBookImageService _bookService;
    private readonly IFilePickerService _filePickerService;
    private readonly IHttpClientFactory _httpClientFactory;

    private Dictionary<int, BookImage> _imagesByTypeId = [];
    private Dictionary<int, byte[]?> _pendingImagesByTypeId = [];
    private Dictionary<int, BookImage> _editStartImagesByTypeId = [];
    private List<BookImage> _editStartImagesList = [];
    private readonly List<int> _pendingDeleteImageIds = [];
    private CancellationTokenSource? _bitmapLoadCts;
    private CancellationTokenSource? _loadCts;
    private int _currentBookId;

    [ObservableProperty] private Bitmap? _coverBitmap;
    [ObservableProperty] private int _selectedImageTypeId;

    partial void OnCoverBitmapChanging(Bitmap? oldValue, Bitmap? newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue))
            oldValue.Dispose();
    }

    partial void OnCoverBitmapChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(NativeImageWidth));
        OnPropertyChanged(nameof(NativeImageHeight));
        OnPropertyChanged(nameof(ImageInfoLabel));
    }

    public double? NativeImageWidth => CoverBitmap?.PixelSize.Width;
    public double? NativeImageHeight => CoverBitmap?.PixelSize.Height;

    public string? ImageInfoLabel =>
        CoverBitmap is { } bmp
            ? $"{bmp.PixelSize.Width} × {bmp.PixelSize.Height} px" +
              (CoverBitmapSizeBytes is { } sz ? $"  —  {sz / 1024} KB" : "")
            : null;

    public ObservableCollection<ImageTypeButtonViewModel> ImageTypeButtons { get; } = [];
    public ObservableCollection<ManageImageItemViewModel> ImageItems { get; } = [];
    public bool HasMultipleImages => ImageItems
        .GroupBy(x => x.SelectedTypeId)
        .Any(g => g.Count() > 1);

    public IReadOnlyList<ImageTypeOption> AvailableImageTypes { get; } =
    [
        new(BookImageTypeId.FrontCover, Resources.BookImageType_Cover),
        new(BookImageTypeId.Thumbnail,  Resources.BookImageType_Thumbnail),
        new(BookImageTypeId.BackCover,  Resources.BookImageType_BackCover),
        new(BookImageTypeId.Spine,      Resources.BookImageType_Spine),
        new(BookImageTypeId.DustJacket, Resources.BookImageType_DustJacket),
    ];

    public string SelectedTypeEmptyLabel => SelectedImageTypeId switch
    {
        BookImageTypeId.FrontCover => Resources.ImageEditor_NoImage_Front,
        BookImageTypeId.Thumbnail  => Resources.ImageEditor_NoImage_Thumb,
        BookImageTypeId.BackCover  => Resources.ImageEditor_NoImage_Back,
        BookImageTypeId.Spine      => Resources.ImageEditor_NoImage_Spine,
        BookImageTypeId.DustJacket => Resources.ImageEditor_NoImage_DustJacket,
        _ => Resources.ImageEditor_NoImage_Generic
    };

    public long? CoverBitmapSizeBytes =>
        _imagesByTypeId.TryGetValue(SelectedImageTypeId, out var imageRow)
            ? imageRow.ImageData?.LongLength
            : null;

    public ImageEditorViewModel(
        IBookImageService bookService,
        IFilePickerService filePickerService,
        IHttpClientFactory httpClientFactory)
    {
        _bookService = bookService;
        _filePickerService = filePickerService;
        _httpClientFactory = httpClientFactory;
    }

    // Called by parent when a book is opened for viewing or editing
    public async Task LoadForBookAsync(Book book)
    {
        // Cancel any in-flight load. Switching books mid-load otherwise lets the newer load
        // rebuild ImageTypeButtons/ImageItems while the older load is suspended at an await
        // inside a foreach — which throws "Collection was modified" when it resumes.
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        SelectedImageTypeId = 0;
        _currentBookId = book.BookId;
        var images = await _bookService.GetBookImagesAsync(book.BookId);
        if (ct.IsCancellationRequested) return;
        // When multiple images share a type (duplicates), keep the lowest-DisplayOrder one
        // for the per-type preview/edit panel. The manage section shows all images.
        _imagesByTypeId = images.GroupBy(i => i.BookImageTypeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.DisplayOrder).First());
        _pendingImagesByTypeId.Clear();
        _editStartImagesByTypeId = new Dictionary<int, BookImage>(_imagesByTypeId);
        RebuildImageTypeButtons();
        _editStartImagesList = images.ToList();
        RebuildImageItems(images);
        await LoadManageThumbnailsAsync(ct);
        if (ct.IsCancellationRequested) return;
        await LoadThumbnailBitmapsAsync(ct);
        if (ct.IsCancellationRequested) return;
        await LoadBitmapForSelectedTypeAsync();
    }

    // Called to reset to cleared state when book is deselected (bookId == null path)
    public void ClearForNoBook()
    {
        _loadCts?.Cancel();
        CoverBitmap = null;
        foreach (var button in ImageTypeButtons)
            button.Bitmap = null;
        ImageTypeButtons.Clear();
        _imagesByTypeId.Clear();
        _pendingImagesByTypeId.Clear();
        _editStartImagesByTypeId.Clear();
        SelectedImageTypeId = 0;
    }

    // Called by parent SaveAsync() — flushes _pendingImagesByTypeId to DB
    public async Task FlushPendingAsync(int bookId)
    {
        foreach (var (typeId, bytes) in _pendingImagesByTypeId)
        {
            if (bytes != null)
                await _bookService.SaveBookImageByTypeAsync(bookId, typeId, bytes);
            else
                await _bookService.RemoveBookImageByTypeAsync(bookId, typeId);
        }
        _pendingImagesByTypeId.Clear();

        // 2. Delete staged-for-removal images FIRST so they cannot cause type conflicts
        foreach (var imageId in _pendingDeleteImageIds)
            await _bookService.RemoveBookImageByIdAsync(bookId, imageId);
        _pendingDeleteImageIds.Clear();

        // Type-conflict guard: block flush if two items from DIFFERENT original type groups
        // both get reassigned to the same SelectedTypeId (would create ambiguity in the DB).
        // Items within the same original group sharing a SelectedTypeId are fine (reorder only).
        var duplicateTypes = ImageItems
            .GroupBy(x => x.SelectedTypeId)
            .Where(g => g.Select(x => x.OriginalTypeId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateTypes.Count > 0)
            throw new InvalidOperationException(
                $"Cannot save: image types {string.Join(", ", duplicateTypes)} are assigned to more than one image. Reassign or remove the conflict before saving.");

        foreach (var item in ImageItems.Where(x => x.IsDirty))
        {
            await _bookService.ReorderBookImageAsync(bookId, item.BookImageId, item.DisplayOrder);
            if (item.SelectedTypeId != item.OriginalTypeId)
                await _bookService.ReassignBookImageTypeAsync(bookId, item.BookImageId, item.SelectedTypeId);
            item.IsDirty = false;
        }
    }

    // True when pending image changes exist
    public bool HasPendingChanges => _pendingImagesByTypeId.Count > 0;

    // Called on Cancel to revert to the snapshot taken at LoadForBookAsync
    public async Task ResetToSaved()
    {
        _pendingDeleteImageIds.Clear();
        _pendingImagesByTypeId.Clear();
        _imagesByTypeId = new Dictionary<int, BookImage>(_editStartImagesByTypeId);
        RebuildImageItems(_editStartImagesList);
        await LoadManageThumbnailsAsync();
        await LoadBitmapForSelectedTypeAsync();
    }

    private async Task LoadManageThumbnailsAsync(CancellationToken ct = default)
    {
        // Snapshot: a superseded load may rebuild ImageItems while we await below.
        foreach (var item in ImageItems.ToList())
        {
            if (ct.IsCancellationRequested) return;
            var bytes = await _bookService.GetBookImageBytesAsync(_currentBookId, item.BookImageId);
            if (bytes is { Length: > 0 })
                item.Thumbnail = new Bitmap(new MemoryStream(bytes));
        }
    }

    [RelayCommand]
    private async Task AttachCoverFromFileAsync()
    {
        try
        {
            var path = await _filePickerService.PickFileAsync(
                Localization.Resources.FilePicker_SelectCoverImage,
                new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" });
            if (path != null)
                await AttachCoverFromPathAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to attach cover from file");
        }
    }

    [RelayCommand]
    private async Task AttachCoverFromUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // Reject non-HTTP(S) schemes to prevent local file reads via file:// URIs.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Log.Warning("Rejected non-HTTP URL in AttachCoverFromUrlAsync: {Url}", url);
            return;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var bytes = await httpClient.GetByteArrayAsync(uri);
            if (bytes.Length > 0)
                await SaveImageBytesForSelectedTypeAsync(bytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to attach cover from URL: {Url}", url);
        }
    }

    [RelayCommand]
    private async Task AttachCoverFromPathAsync(string filePath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath);
            if (bytes.Length > 0)
                await SaveImageBytesForSelectedTypeAsync(bytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to attach cover from path: {Path}", filePath);
        }
    }

    [RelayCommand]
    private async Task SelectImageTypeAsync(int bookImageTypeId)
    {
        SelectedImageTypeId = bookImageTypeId;

        foreach (var button in ImageTypeButtons)
            button.IsSelected = button.BookImageTypeId == bookImageTypeId;

        OnPropertyChanged(nameof(SelectedTypeEmptyLabel));
        await LoadBitmapForSelectedTypeAsync();
    }

    [RelayCommand]
    private Task RemoveCoverForSelectedTypeAsync()
    {
        _pendingImagesByTypeId[SelectedImageTypeId] = null;
        _imagesByTypeId.Remove(SelectedImageTypeId);
        CoverBitmap = null;
        var button = ImageTypeButtons.FirstOrDefault(b => b.BookImageTypeId == SelectedImageTypeId);
        if (button is not null) button.Bitmap = null;
        OnPropertyChanged(nameof(SelectedTypeEmptyLabel));
        OnPropertyChanged(nameof(CoverBitmapSizeBytes));
        OnPropertyChanged(nameof(ImageInfoLabel));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task MoveUpAsync(ManageImageItemViewModel item)
    {
        if (!item.CanMoveUp) return;
        var sameType = ImageItems.Where(x => x.SelectedTypeId == item.SelectedTypeId)
            .OrderBy(x => x.DisplayOrder).ToList();
        var idx = sameType.IndexOf(item);
        if (idx <= 0) return;
        var prev = sameType[idx - 1];
        (item.DisplayOrder, prev.DisplayOrder) = (prev.DisplayOrder, item.DisplayOrder);
        RecalculateCanMove(item.SelectedTypeId);
        await RefreshTypePreviewsFromManageAsync();
    }

    [RelayCommand]
    private async Task MoveDownAsync(ManageImageItemViewModel item)
    {
        if (!item.CanMoveDown) return;
        var sameType = ImageItems.Where(x => x.SelectedTypeId == item.SelectedTypeId)
            .OrderBy(x => x.DisplayOrder).ToList();
        var idx = sameType.IndexOf(item);
        if (idx >= sameType.Count - 1) return;
        var next = sameType[idx + 1];
        (item.DisplayOrder, next.DisplayOrder) = (next.DisplayOrder, item.DisplayOrder);
        RecalculateCanMove(item.SelectedTypeId);
        await RefreshTypePreviewsFromManageAsync();
    }

    [RelayCommand]
    private async Task RemoveManageItemAsync(ManageImageItemViewModel item)
    {
        item.PropertyChanged -= OnManageItemPropertyChanged;
        ImageItems.Remove(item);
        _pendingDeleteImageIds.Add(item.BookImageId);
        item.Thumbnail?.Dispose();
        OnPropertyChanged(nameof(HasMultipleImages));
        await RefreshTypePreviewsFromManageAsync();
    }

    // A manage-list edit (reorder/remove/type reassignment) changes which existing image represents
    // each type at the top of the view. Re-derive the per-type previews from the current manage state,
    // overlaying any pending per-type panel changes, then refresh the type buttons and selected preview.
    private async Task RefreshTypePreviewsFromManageAsync()
    {
        var bytesById = _editStartImagesList
            .GroupBy(i => i.BookImageId)
            .ToDictionary(g => g.Key, g => g.First().ImageData);

        var rebuilt = new Dictionary<int, BookImage>();
        foreach (var group in ImageItems.GroupBy(x => x.SelectedTypeId))
        {
            var representative = group.OrderBy(x => x.DisplayOrder).First();
            if (bytesById.TryGetValue(representative.BookImageId, out var data) && data is { Length: > 0 })
            {
                rebuilt[group.Key] = new BookImage
                {
                    BookImageId = representative.BookImageId,
                    BookImageTypeId = group.Key,
                    ImageData = data,
                    IsPrimary = group.Key == BookImageTypeId.FrontCover,
                };
            }
        }

        // Per-type panel changes (attach/remove) live outside the manage list — apply them last.
        foreach (var (typeId, bytes) in _pendingImagesByTypeId)
        {
            if (bytes is null)
                rebuilt.Remove(typeId);
            else
                rebuilt[typeId] = new BookImage
                {
                    BookImageTypeId = typeId,
                    ImageData = bytes,
                    IsPrimary = typeId == BookImageTypeId.FrontCover,
                };
        }

        _imagesByTypeId = rebuilt;
        await LoadThumbnailBitmapsAsync();
        await LoadBitmapForSelectedTypeAsync();
        OnPropertyChanged(nameof(CoverBitmapSizeBytes));
        OnPropertyChanged(nameof(ImageInfoLabel));
    }

    private void OnManageItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // The type picker rebinds SelectedTypeId; reflect the reassignment at the top of the view.
        if (e.PropertyName != nameof(ManageImageItemViewModel.SelectedTypeId)) return;
        foreach (var typeId in ImageItems.Select(x => x.SelectedTypeId).Distinct().ToList())
            RecalculateCanMove(typeId);
        OnPropertyChanged(nameof(HasMultipleImages));
        _ = RefreshTypePreviewsFromManageAsync();
    }

    private void RecalculateCanMove(int typeId)
    {
        var sameType = ImageItems.Where(x => x.SelectedTypeId == typeId)
            .OrderBy(x => x.DisplayOrder).ToList();
        for (int i = 0; i < sameType.Count; i++)
        {
            sameType[i].CanMoveUp = i > 0;
            sameType[i].CanMoveDown = i < sameType.Count - 1;
            sameType[i].NotifyCanMoveChanged();
        }
    }

    private void RebuildImageItems(IReadOnlyList<BookImage> images)
    {
        foreach (var evicted in ImageItems)
        {
            evicted.PropertyChanged -= OnManageItemPropertyChanged;
            evicted.Thumbnail?.Dispose();
        }
        ImageItems.Clear();
        var flat = images
            .OrderBy(i => i.BookImageTypeId)
            .ThenBy(i => i.DisplayOrder)
            .ToList();

        for (int i = 0; i < flat.Count; i++)
        {
            var img = flat[i];
            var sameType = flat.Where(x => x.BookImageTypeId == img.BookImageTypeId).ToList();
            var item = new ManageImageItemViewModel
            {
                BookImageId = img.BookImageId,
                OriginalTypeId = img.BookImageTypeId,
                OriginalDisplayOrder = img.DisplayOrder,
                SelectedTypeId = img.BookImageTypeId,
                DisplayOrder = img.DisplayOrder,
                TypeName = img.BookImageTypeId switch
                {
                    BookImageTypeId.FrontCover => Resources.BookImageType_Cover,
                    BookImageTypeId.Thumbnail  => Resources.BookImageType_Thumbnail,
                    BookImageTypeId.BackCover  => Resources.BookImageType_BackCover,
                    BookImageTypeId.Spine      => Resources.BookImageType_Spine,
                    BookImageTypeId.DustJacket => Resources.BookImageType_DustJacket,
                    _ => img.BookImageType?.TypeName ?? string.Empty
                },
                CanMoveUp = img.DisplayOrder > sameType.Min(x => x.DisplayOrder),
                CanMoveDown = img.DisplayOrder < sameType.Max(x => x.DisplayOrder),
                IsThumbnailType = img.BookImageTypeId == 1,
            };
            item.PropertyChanged += OnManageItemPropertyChanged;
            ImageItems.Add(item);
        }
        OnPropertyChanged(nameof(HasMultipleImages));
    }

    private void RebuildImageTypeButtons()
    {
        foreach (var button in ImageTypeButtons)
            button.Bitmap = null;
        ImageTypeButtons.Clear();

        ImageTypeButtons.Add(new ImageTypeButtonViewModel { BookImageTypeId = BookImageTypeId.FrontCover, Label = Resources.ImageEditor_TypeButton_Front });
        ImageTypeButtons.Add(new ImageTypeButtonViewModel { BookImageTypeId = BookImageTypeId.Thumbnail,  Label = Resources.ImageEditor_TypeButton_Thumb });
        ImageTypeButtons.Add(new ImageTypeButtonViewModel { BookImageTypeId = BookImageTypeId.BackCover,  Label = Resources.ImageEditor_TypeButton_Back });
        ImageTypeButtons.Add(new ImageTypeButtonViewModel { BookImageTypeId = BookImageTypeId.Spine,      Label = Resources.ImageEditor_TypeButton_Spine });
        ImageTypeButtons.Add(new ImageTypeButtonViewModel { BookImageTypeId = BookImageTypeId.DustJacket, Label = Resources.ImageEditor_TypeButton_Jacket });

        foreach (var button in ImageTypeButtons)
            button.IsSelected = button.BookImageTypeId == SelectedImageTypeId;
    }

    private async Task LoadThumbnailBitmapsAsync(CancellationToken ct = default)
    {
        // Snapshot: a superseded load may rebuild ImageTypeButtons while we await below.
        foreach (var button in ImageTypeButtons.ToList())
        {
            if (ct.IsCancellationRequested) return;
            Bitmap? bitmap = null;
            long? sizeBytes = null;
            if (_imagesByTypeId.TryGetValue(button.BookImageTypeId, out var imageRow)
                && imageRow.ImageData?.Length > 0)
            {
                sizeBytes = imageRow.ImageData.LongLength;
                try
                {
                    bitmap = await Task.Run(() =>
                    {
                        using var stream = new System.IO.MemoryStream(imageRow.ImageData);
                        return new Bitmap(stream);
                    });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load thumbnail bitmap for image type {TypeId}", button.BookImageTypeId);
                    bitmap = null; // explicit — do not show a broken image
                    sizeBytes = null;
                }
            }
            button.BitmapSizeBytes = sizeBytes;
            button.Bitmap = bitmap;
        }
    }

    private async Task LoadBitmapForSelectedTypeAsync()
    {
        _bitmapLoadCts?.Cancel();
        _bitmapLoadCts?.Dispose();
        _bitmapLoadCts = new CancellationTokenSource();
        var ct = _bitmapLoadCts.Token;

        if (_imagesByTypeId.TryGetValue(SelectedImageTypeId, out var imageRow)
            && imageRow.ImageData?.Length > 0)
        {
            Bitmap? bmp = null;
            try
            {
                bmp = await Task.Run(() =>
                {
                    using var ms = new System.IO.MemoryStream(imageRow.ImageData);
                    return new Bitmap(ms);
                }, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to decode preview bitmap for image type {TypeId}", SelectedImageTypeId);
                CoverBitmap = null; // do not show a broken image
                OnPropertyChanged(nameof(CoverBitmapSizeBytes));
                OnPropertyChanged(nameof(ImageInfoLabel));
                return;
            }

            if (ct.IsCancellationRequested) { bmp?.Dispose(); return; }
            CoverBitmap = bmp;
        }
        else
        {
            CoverBitmap = null;
        }
        OnPropertyChanged(nameof(CoverBitmapSizeBytes));
        OnPropertyChanged(nameof(ImageInfoLabel));
    }

    private async Task SaveImageBytesForSelectedTypeAsync(byte[] bytes)
    {
        _pendingImagesByTypeId[SelectedImageTypeId] = bytes;
        _imagesByTypeId[SelectedImageTypeId] = new BookImage
        {
            BookImageTypeId = SelectedImageTypeId,
            ImageData = bytes,
            IsPrimary = SelectedImageTypeId == BookImageTypeId.FrontCover
        };
        await LoadBitmapForSelectedTypeAsync();
    }
}

public record ImageTypeOption(int BookImageTypeId, string Name);
