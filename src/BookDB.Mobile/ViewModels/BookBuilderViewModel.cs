using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// The second step of the wizard: a checklist for the book whose ISBN was just read. Front and back are the
/// expected two, spine and dust jacket are there if wanted, and none of them is required — the ISBN alone is
/// a complete set. Each slot hands off to the platform's document scanner, and its crop comes back to a
/// review card before anything is kept.
/// </summary>
public sealed partial class BookBuilderViewModel : PageViewModel
{
    private readonly IDocumentScanner _scanner;
    private readonly ICaptureAvailabilityProbe _probe;
    private readonly IConnectionService _connection;
    private readonly Action<StagedBook, StageAction> _onStaged;
    private readonly string _clientItemId;

    private CaptureSlotViewModel? _activeSlot;

    /// <param name="editing">A set already staged, re-opened to re-take, add or drop a photo. It keeps its id,
    /// so finishing replaces that book rather than staging a second copy of it.</param>
    public BookBuilderViewModel(
        string isbn,
        IDocumentScanner scanner,
        ICaptureAvailabilityProbe probe,
        IConnectionService connection,
        Action<StagedBook, StageAction> onStaged,
        StagedBook? editing = null)
    {
        Isbn = isbn;
        _scanner = scanner;
        _probe = probe;
        _connection = connection;
        _onStaged = onStaged;

        // Minted once per book, not once per press: finishing the same book twice must not stage it twice.
        _clientItemId = editing?.ClientItemId ?? Guid.NewGuid().ToString("N");

        Slots =
        [
            new CaptureSlotViewModel(ScanImageType.FrontCover, Resources.Builder_SlotFrontCover, isOptional: false),
            new CaptureSlotViewModel(ScanImageType.BackCover, Resources.Builder_SlotBackCover, isOptional: false),
            new CaptureSlotViewModel(ScanImageType.Spine, Resources.Builder_SlotSpine, isOptional: true),
            new CaptureSlotViewModel(ScanImageType.DustJacket, Resources.Builder_SlotDustJacket, isOptional: true),
        ];

        foreach (var image in editing?.Images ?? [])
        {
            var slot = Slots.FirstOrDefault(candidate => candidate.Type == image.Type);
            if (slot is not null)
                slot.Jpeg = image.Jpeg;
        }
    }

    public override string Title => Resources.Builder_Title;

    public string Isbn { get; }

    public string IsbnLine => string.Format(CultureInfo.CurrentCulture, Resources.Builder_IsbnLabel, Isbn);

    public IReadOnlyList<CaptureSlotViewModel> Slots { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureCommand))]
    private bool _isCapturing;

    /// <summary>The crop waiting to be accepted or re-taken, straight from the scanner and not yet sized.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReview))]
    [NotifyPropertyChangedFor(nameof(ReviewLabel))]
    private byte[]? _reviewImage;

    /// <summary>Why capture did not happen — a device without the scanner, or a module that has not been
    /// delivered yet.</summary>
    [ObservableProperty]
    private string? _message;

    /// <summary>Only for failures a second attempt could clear; a device without Google Play services is not
    /// one of them.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCaptureCommand))]
    private bool _canRetryCapture;

    public bool HasReview => ReviewImage is not null;

    public string ReviewLabel => _activeSlot?.Label ?? string.Empty;

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private async Task CaptureAsync(CaptureSlotViewModel? slot)
    {
        if (slot is null)
            return;

        _activeSlot = slot;
        Message = null;
        CanRetryCapture = false;

        // Asked before the scanner is opened, so a device that cannot capture says so instead of failing
        // behind a camera screen.
        var availability = _probe.Check();
        if (availability != CaptureAvailability.Available)
        {
            Message = availability == CaptureAvailability.PlayServicesOutdated
                ? Resources.Builder_PlayServicesOutdated
                : Resources.Builder_PlayServicesMissing;
            return;
        }

        IsCapturing = true;
        try
        {
            var result = await _scanner.ScanPageAsync();
            switch (result.Outcome)
            {
                case DocumentScanOutcome.Captured:
                    ReviewImage = result.Page;
                    break;
                case DocumentScanOutcome.ModuleUnavailable:
                    Message = Resources.Builder_ModuleUnavailable;
                    CanRetryCapture = true;
                    break;
                case DocumentScanOutcome.Failed:
                    Message = Resources.Builder_CaptureFailed;
                    CanRetryCapture = true;
                    break;
                default:
                    // Cancelled: the user backed out on purpose and needs no explanation.
                    break;
            }
        }
        finally
        {
            IsCapturing = false;
        }
    }

    private bool CanCapture() => !IsCapturing;

    [RelayCommand(CanExecute = nameof(CanRetryCapture))]
    private Task RetryCaptureAsync() => CaptureAsync(_activeSlot);

    /// <summary>Keeps the crop: sizes it to the computer's settings and fills the slot.</summary>
    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (ReviewImage is not { } page || _activeSlot is null)
            return;

        var settings = _connection.ServerInfo?.Capture;

        // Decoding and re-encoding a full-resolution crop is slow enough to be felt on the frame it runs on.
        var jpeg = await Task.Run(() => ScanImageEncoder.Encode(page, settings));

        _activeSlot.Jpeg = jpeg;
        ReviewImage = null;
    }

    /// <summary>Throws the crop away and opens the scanner again. Backing out of that second attempt is also
    /// how a photo gets abandoned altogether.</summary>
    [RelayCommand]
    private async Task RetakeAsync()
    {
        ReviewImage = null;
        await CaptureAsync(_activeSlot);
    }

    [RelayCommand]
    private void Done() => Stage(StageAction.NextBook);

    [RelayCommand]
    private void SendNow() => Stage(StageAction.SendNow);

    [RelayCommand]
    private void SaveToBatch() => Stage(StageAction.SaveToBatch);

    private void Stage(StageAction action)
    {
        var images = Slots
            .Where(slot => slot.Jpeg is not null)
            .Select(slot => new StagedImage { Type = slot.Type, Jpeg = slot.Jpeg! })
            .ToList();

        _onStaged(
            new StagedBook
            {
                ClientItemId = _clientItemId,
                Isbn = Isbn,
                Images = images,
            },
            action);
    }
}
