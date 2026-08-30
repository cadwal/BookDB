using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;
using BookDB.Mobile.Views;

namespace BookDB.Mobile;

public partial class App : Application
{
    /// <summary>Set by the platform head before Avalonia starts, so the shared app can reach app-private
    /// storage and the barcode scanner.</summary>
    public static IMobilePlatform? Platform { get; set; }

    /// <summary>Set by the headless UI-test harness so app init loads the theme, resources and
    /// <see cref="ViewLocator"/> but stops short of composing the live shell, which needs a platform head.
    /// The tests build the screens they are rendering themselves.</summary>
    public static bool HeadlessTestMode { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (HeadlessTestMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // The mobile heads run single-view; the shell view hosts the navigation frame.
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var shell = BuildShell();
            singleView.MainView = new MainView { DataContext = shell };
            shell.StartConnectionHeartbeat();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainViewModel BuildShell()
    {
        var platform = Platform ?? throw new InvalidOperationException("App.Platform must be set by the head before startup.");

        var identityStore = new FileIdentityStore(platform.AppDataDirectory);
        var channelFactory = new CompanionChannelFactory();
        var coordinator = new PairingCoordinator(channelFactory, identityStore);
        var connection = new ConnectionService(identityStore, channelFactory, new UdpDesktopLocator());
        var scanner = platform.CreateBarcodeScanner();
        var documentScanner = platform.CreateDocumentScanner();
        var captureProbe = platform.CreateCaptureAvailabilityProbe();
        var deviceSettings = platform.CreateDeviceSettings();
        var staging = new FileStagingStore(platform.AppDataDirectory);
        var uploads = new UploadService(staging, connection);

        return new MainViewModel(identityStore, connection, new PageFactories
        {
            Pairing = navigator => new PairingViewModel(scanner, coordinator, deviceSettings, navigator),
            Hub = navigator => new HubViewModel(navigator, staging),
            Settings = navigator => new SettingsViewModel(connection, identityStore, navigator),
            Browse = Browsing,
            Scan = Wizard,
            Batch = Tray,
        });

        PageViewModel Browsing(INavigator navigator)
        {
            // The screen has nothing to show until the computer answers, so it starts asking on the way in.
            var page = new BrowseViewModel(
                connection, scanner, deviceSettings, (bookId, title) => OpenDetail(navigator, bookId, title));
            page.LoadCommand.Execute(null);
            return page;
        }

        void OpenDetail(INavigator navigator, int bookId, string title)
        {
            var page = new BookDetailViewModel(connection, bookId, title);
            page.LoadCommand.Execute(null);
            navigator.NavigateTo(page);
        }

        PageViewModel Wizard(INavigator navigator) => new IsbnScanViewModel(
            scanner, connection, staging, deviceSettings,
            isbn => navigator.NavigateTo(Builder(navigator, isbn)));

        PageViewModel Builder(INavigator navigator, string isbn, StagedBook? editing = null) =>
            new BookBuilderViewModel(isbn, documentScanner, captureProbe, connection, (book, action) =>
            {
                staging.Save(book);

                // Next book replaces the builder rather than stacking another screen per book; the other two
                // end the run — one straight into the sending, one at the tray it waits in.
                navigator.Replace(action switch
                {
                    StageAction.NextBook => Wizard(navigator),
                    StageAction.SendNow => Sending(navigator),
                    _ => Tray(navigator),
                });
            },
            editing);

        PageViewModel Tray(INavigator navigator) => new BatchTrayViewModel(
            staging, connection, navigator, item => Reopen(navigator, item), () => navigator.NavigateTo(Sending(navigator)));

        PageViewModel Sending(INavigator navigator)
        {
            // The screen starts working as soon as it is on: arriving here is the decision to send.
            var page = new UploadViewModel(staging, uploads, navigator);
            page.RunCommand.Execute(null);
            return page;
        }

        // Re-opening a staged book reads its covers back off disk, which is where they have been since it
        // was staged — the tray only ever holds thumbnails.
        PageViewModel Reopen(INavigator navigator, StagedItem item) => Builder(
            navigator,
            item.Isbn,
            new StagedBook
            {
                ClientItemId = item.ClientItemId,
                Isbn = item.Isbn,
                Images = [.. item.ImageTypes
                    .Select(type => (Type: type, Jpeg: staging.ReadImage(item.ClientItemId, type)))
                    .Where(image => image.Jpeg is not null)
                    .Select(image => new StagedImage { Type = image.Type, Jpeg = image.Jpeg! })],
            });
    }
}
