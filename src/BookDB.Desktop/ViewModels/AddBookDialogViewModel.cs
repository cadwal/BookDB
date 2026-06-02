using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class AddBookDialogViewModel : ObservableObject
{
    private readonly IBookService _bookService;
    private readonly IBookImageService _bookImageService;
    private readonly ILookupService _lookupService;
    private readonly IFilePickerService _filePickerService;
    private readonly IHttpClientFactory _httpClientFactory;

    // Callback to close the dialog window with a result
    public Action<bool>? CloseDialog { get; set; }

    private int? _defaultCollectionId;
    // Bytes of the selected cover (null if none chosen)
    private byte[]? _coverBytes;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _isbn = string.Empty;

    [ObservableProperty]
    private int? _selectedFormatId;

    [ObservableProperty]
    private string _publisher = string.Empty;

    [ObservableProperty]
    private string _year = string.Empty;

    [ObservableProperty]
    private Bitmap? _coverBitmap;

    [ObservableProperty]
    private string _urlInput = string.Empty;

    [ObservableProperty]
    private int? _selectedCollectionId;

    public ObservableCollection<Format> Formats { get; } = [];
    public ObservableCollection<CollectionItem> Collections { get; } = [];

    private bool CanSave => !string.IsNullOrWhiteSpace(Title);

    public AddBookDialogViewModel(
        IBookService bookService,
        IBookImageService bookImageService,
        ILookupService lookupService,
        IFilePickerService filePickerService,
        IHttpClientFactory httpClientFactory)
    {
        _bookService = bookService;
        _bookImageService = bookImageService;
        _lookupService = lookupService;
        _filePickerService = filePickerService;
        _httpClientFactory = httpClientFactory;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var formats = await _lookupService.GetAllAsync<Format>();
            Formats.Clear();
            foreach (var f in formats)
                Formats.Add(f);

            var collections = await _lookupService.GetCollectionsAsync();
            Collections.Clear();
            foreach (var c in collections)
                Collections.Add(new CollectionItem(c.CollectionId, c.Name));

            SelectedCollectionId = _defaultCollectionId ?? Collections.FirstOrDefault()?.Id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load data for Add Book dialog");
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        try
        {
            var book = new Book
            {
                Title = Title,
                Isbn = string.IsNullOrWhiteSpace(Isbn) ? null : Isbn,
                FormatId = SelectedFormatId,
                PubDate = string.IsNullOrWhiteSpace(Year) ? null : Year,
                CollectionId = SelectedCollectionId,
            };

            var authorNames = string.IsNullOrWhiteSpace(Author)
                ? Array.Empty<string>()
                : Author.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var savedBook = await _bookService.AddBookWithContributorsAsync(book, authorNames);

            // Store cover as BookImage BLOB after book has been saved and has a BookId
            if (_coverBytes is { Length: > 0 })
                await _bookImageService.SavePrimaryBookImageAsync(savedBook.BookId, _coverBytes);

            CloseDialog?.Invoke(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save new book");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseDialog?.Invoke(false);
    }

    [RelayCommand]
    private async Task BrowseCoverAsync()
    {
        try
        {
            var path = await _filePickerService.PickFileAsync(
                Localization.Resources.FilePicker_SelectCoverImage,
                new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" });
            if (path != null)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                _coverBytes = bytes;
                using var ms = new MemoryStream(bytes);
                CoverBitmap = new Bitmap(ms);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to browse for cover image");
        }
    }

    [RelayCommand]
    private async Task DownloadCoverAsync()
    {
        if (string.IsNullOrWhiteSpace(UrlInput)) return;
        try
        {
            using var client = _httpClientFactory.CreateClient();
            var bytes = await client.GetByteArrayAsync(UrlInput);
            _coverBytes = bytes;
            using var ms = new MemoryStream(bytes);
            CoverBitmap = new Bitmap(ms);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download cover from URL: {Url}", UrlInput);
        }
    }

    public void Reset(int? defaultCollectionId = null)
    {
        _defaultCollectionId = defaultCollectionId;
        _coverBytes = null;
        Title = string.Empty;
        Author = string.Empty;
        Isbn = string.Empty;
        SelectedFormatId = null;
        Publisher = string.Empty;
        Year = string.Empty;
        CoverBitmap = null;
        UrlInput = string.Empty;
    }
}
