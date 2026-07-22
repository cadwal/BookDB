using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BookDB.Desktop.Behaviors;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class BookRowViewModel : ObservableObject, IHoverImageLoader
{
    public int BookId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? AuthorDisplay { get; init; }
    public string? SeriesDisplay { get; init; }
    public string? PublisherName { get; init; }
    public string? Year { get; init; }
    public string? FormatName { get; init; }
    public bool HasCoverImage { get; init; }
    public bool HasDuplicateImageTypes { get; init; }
    public bool HasMultipleImages => HasDuplicateImageTypes;
    public string ImageCountBadge => Resources.BookList_Thumbnail_Badge_Text;
    public string ImageCountTooltip => Resources.BookList_Thumbnail_Badge_Tooltip;
    public int? FormatId { get; init; }
    public int? SeriesId { get; init; }
    public int? PublisherId { get; init; }
    public int? LanguageId { get; init; }
    public int? RatingId { get; init; }
    public int? StatusId { get; init; }
    public int? LocationId { get; init; }
    public int? OwnerId { get; init; }
    public int CollectionId { get; init; }
    public string? Isbn { get; init; }
    public IReadOnlyList<int> AuthorPersonIds { get; init; } = [];
    public IReadOnlyList<int> CategoryIds { get; init; } = [];
    public string? RatingDisplay { get; init; }
    public string? StatusDisplay { get; init; }

    /// <summary>
    /// Re-raises the status-badge binding so its <c>StatusBadgeColorConverter</c> re-runs and picks up the new
    /// palette after a live theme switch. The value is unchanged — only the converter's colour output changes.
    /// </summary>
    public void RefreshThemedBrushes() => OnPropertyChanged(nameof(StatusDisplay));

    public bool IsLoaned { get; init; }
    public bool IsOverdue { get; init; }
    public string? LoanedToName { get; init; }

    public int RowNumber { get; set; }

    [ObservableProperty]
    private Bitmap? _coverThumbnail;

    [ObservableProperty]
    private Bitmap? _tooltipBitmap;

    [ObservableProperty]
    private long? _tooltipBitmapSizeBytes;

    /// <summary>Set by the list VM; fetches the full-size tooltip bitmap for this row on demand.</summary>
    public Func<BookRowViewModel, Task>? TooltipLoader { get; set; }

    private bool _tooltipLoadInFlight;

    public void RequestHoverImageLoad()
    {
        if (_tooltipLoadInFlight || TooltipLoader is null) return;
        _tooltipLoadInFlight = true;
        _ = LoadAsync();

        async Task LoadAsync()
        {
            try { await TooltipLoader(this); }
            finally { _tooltipLoadInFlight = false; }
        }
    }

    // Factory method from BookService.BookListRow
    public static BookRowViewModel FromListRow(BookService.BookListRow row)
    {
        return new BookRowViewModel
        {
            BookId = row.BookId,
            Title = row.Title,
            AuthorDisplay = row.AuthorDisplay,
            SeriesDisplay = row.SeriesDisplay,
            PublisherName = row.PublisherName,
            Year = row.Year,
            FormatName = row.FormatName,
            HasCoverImage = row.HasCoverImage,
            FormatId = row.FormatId,
            SeriesId = row.SeriesId,
            PublisherId = row.PublisherId,
            LanguageId = row.LanguageId,
            RatingId = row.RatingId,
            StatusId = row.StatusId,
            LocationId = row.LocationId,
            OwnerId = row.OwnerId,
            CollectionId = row.CollectionId ?? 0,
            Isbn = row.Isbn,
            AuthorPersonIds = row.AuthorPersonIds,
            CategoryIds = row.CategoryIds,
            RatingDisplay = row.RatingDisplay,
            StatusDisplay = row.StatusDisplay,
            HasDuplicateImageTypes = row.HasDuplicateImageTypes,
            IsLoaned = row.IsLoaned,
            IsOverdue = row.IsOverdue,
            LoanedToName = row.LoanedToName,
        };
    }
}
