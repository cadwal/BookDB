using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// One book, read-only. The metadata arrives already resolved to text — the phone holds none of the desktop's
/// lookup tables — and the photos follow one at a time, so a book with four of them does not arrive as one
/// large message.
/// </summary>
public sealed partial class BookDetailViewModel : PageViewModel, IDisposable
{
    /// <summary>Plenty for a phone screen, and a fraction of what the stored originals weigh.</summary>
    public const int ImageLongEdgePx = 1200;

    private readonly IConnectionService _connection;
    private readonly int _bookId;
    private readonly string _fallbackTitle;

    private CancellationTokenSource? _pending;

    public BookDetailViewModel(IConnectionService connection, int bookId, string fallbackTitle)
    {
        _connection = connection;
        _bookId = bookId;
        _fallbackTitle = fallbackTitle;
        _bookTitle = fallbackTitle;
    }

    /// <summary>The row's title stands in until the detail arrives, so the bar is never blank on the way in.</summary>
    public override string Title => BookTitle;

    /// <summary>The shell disposes a page it navigates away from. Photos arrive one at a time and each is a
    /// screen-sized JPEG, so a fetch left running holds this page and everything it has already decoded.</summary>
    public void Dispose()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    public ObservableCollection<DetailFieldViewModel> Fields { get; } = [];

    public ObservableCollection<DetailImageViewModel> Images { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private string _bookTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSubtitle))]
    private string? _subtitle;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The book was listed but is no longer there — deleted on the computer since the list was drawn.</summary>
    [ObservableProperty]
    private bool _isMissing;

    [ObservableProperty]
    private bool _isUnreachable;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool HasImages => Images.Count > 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        var token = _pending.Token;

        IsBusy = true;
        IsMissing = false;
        IsUnreachable = false;

        try
        {
            var connection = await _connection.EnsureConnectedAsync(token);
            if (connection is null)
            {
                IsUnreachable = true;
                return;
            }

            var detail = await connection.Service.GetBookDetailAsync(
                new BookRef { BookId = _bookId },
                new CallContext(new CallOptions(cancellationToken: token)));

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!detail.Found)
            {
                IsMissing = true;
                return;
            }

            Apply(detail);

            // The metadata is the point of the screen; the photos fill in under it rather than behind a
            // spinner. Deliberately sequential — four covers at once is four full-size decodes in flight.
            IsBusy = false;
            foreach (var image in Images)
            {
                await LoadImageAsync(connection, _bookId, image, token);
            }
        }
        catch (Exception ex) when (
            ex is RpcException or HttpRequestException or IOException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
            {
                IsUnreachable = true;
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    [RelayCommand]
    private async Task RetryAsync() => await LoadAsync();

    private void Apply(BookDetail detail)
    {
        BookTitle = string.IsNullOrWhiteSpace(detail.Title) ? _fallbackTitle : detail.Title;
        Subtitle = detail.Subtitle;

        Fields.Clear();
        Add(Resources.Detail_Field_Authors, detail.Authors);
        Add(Resources.Detail_Field_Series, detail.Series);
        Add(Resources.Detail_Field_Publisher, detail.Publisher);
        Add(Resources.Detail_Field_Published, detail.PubDate);
        Add(Resources.Detail_Field_Format, SeededLookupNames.Localize(detail.FormatKey, detail.Format));
        Add(Resources.Detail_Field_Language, SeededLookupNames.Localize(detail.LanguageKey, detail.Language));
        Add(Resources.Detail_Field_Pages, detail.Pages?.ToString(CultureInfo.CurrentCulture));
        Add(Resources.Detail_Field_Isbn, detail.Isbn);
        Add(Resources.Detail_Field_Collection, detail.Collection);
        Add(Resources.Detail_Field_Comments, detail.Comments);

        Images.Clear();
        foreach (var type in detail.Images)
        {
            Images.Add(new DetailImageViewModel(type));
        }

        OnPropertyChanged(nameof(HasImages));

        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Fields.Add(new DetailFieldViewModel(label, value!));
            }
        }
    }

    private static async Task LoadImageAsync(
        ICompanionConnection connection, int bookId, DetailImageViewModel image, CancellationToken token)
    {
        var bytes = await connection.Service.GetBookImageAsync(
            new BookImageRef { BookId = bookId, Type = image.Type, MaxLongEdgePx = ImageLongEdgePx },
            new CallContext(new CallOptions(cancellationToken: token)));

        if (!token.IsCancellationRequested)
        {
            image.Jpeg = bytes.Jpeg;
        }
    }
}
