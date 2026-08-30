using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.Tests;
using BookDB.Mobile.ViewModels;
using BookDB.Mobile.Views;

namespace BookDB.Mobile.UITests;

/// <summary>A screen the smoke layer renders: its name, and how to build it with a real view model.</summary>
internal sealed record Screen(string Name, Func<Task<Control>> BuildAsync);

/// <summary>
/// Every screen the phone can show, built the way <c>App.BuildShell</c> builds it but against the fakes — a
/// computer that answers, a camera, a batch with a book in it. Each is rendered by <see cref="SmokeTests"/>,
/// and <see cref="ScreenCoverageTests"/> fails if a view is added without an entry here.
/// </summary>
internal static class ScreenRegistry
{
    public const string Isbn = "9780441013593";

    /// <summary>A real JPEG, so the smokes decode a photo rather than skip the empty-slot path. Declared
    /// before <see cref="All"/>: static initializers run in declaration order, and the screens read it.</summary>
    public static byte[] Jpeg { get; } = TestJpeg.OnePixel();

    public static IReadOnlyList<Screen> All { get; } =
    [
        new("Shell", () =>
        {
            // The shell renders whichever page is current through the app's ViewLocator, so this covers the
            // frame, the top bar and the chip at once.
            var shell = new MainViewModel(
                new FakeIdentityStore(paired: true), Connected(), Pages());
            return Control(new MainView { DataContext = shell });
        }),

        new("Pairing", () => Control(new PairingView
        {
            DataContext = new PairingViewModel(
                new FakeBarcodeScanner(), new FakePairingCoordinator(), new FakeDeviceSettings(),
                new FakeNavigator()),
        })),

        new("Hub", () => Control(new HubView
        {
            DataContext = new HubViewModel(new FakeNavigator(), Batch()),
        })),

        new("IsbnScan", () => Control(new IsbnScanView
        {
            DataContext = new IsbnScanViewModel(
                new FakeBarcodeScanner(), Connected(), new FakeStagingStore(), new FakeDeviceSettings(), _ => { }),
        })),

        new("BookBuilder", () => Control(new BookBuilderView
        {
            DataContext = new BookBuilderViewModel(
                Isbn, new FakeDocumentScanner(), new FakeCaptureProbe(), Connected(), (_, _) => { }),
        })),

        new("BatchTray", () => Control(new BatchTrayView
        {
            DataContext = new BatchTrayViewModel(
                Batch(), Connected(), new FakeNavigator(), _ => new HubViewModel(new FakeNavigator(), Batch()),
                () => { }),
        })),

        new("Upload", () => Control(new UploadView
        {
            DataContext = new UploadViewModel(Batch(), new StubUploadService(), new FakeNavigator()),
        })),

        new("Browse", async () =>
        {
            var desktop = new StubScannerService();
            desktop.Pages.Add(new BookPage
            {
                TotalCount = 1,
                Books = [new BookSummary { BookId = 7, Title = "Kallocain", Authors = "Karin Boye", Thumbnail = Jpeg }],
            });
            desktop.Collections.Add(new CollectionRef { CollectionId = 1, Name = "Fiction", BookCount = 1 });

            var page = new BrowseViewModel(
                Connected(desktop), new FakeBarcodeScanner(), new FakeDeviceSettings(), (_, _) => { },
                _ => Task.CompletedTask);
            await page.LoadCommand.ExecuteAsync(null);
            return (Control)new BrowseView { DataContext = page };
        }),

        new("BookDetail", async () =>
        {
            var desktop = new StubScannerService();
            desktop.Details[7] = new BookDetail
            {
                Found = true,
                BookId = 7,
                Title = "Kallocain",
                Subtitle = "En roman om framtiden",
                Authors = "Karin Boye",
                Format = "Hardcover",
                FormatKey = "Format_Hardcover",
                Images = [ScanImageType.FrontCover],
            };
            desktop.Images[(7, ScanImageType.FrontCover)] = Jpeg;

            var page = new BookDetailViewModel(Connected(desktop), 7, "Kallocain");
            await page.LoadCommand.ExecuteAsync(null);
            return (Control)new BookDetailView { DataContext = page };
        }),

        new("Settings", () => Control(new SettingsView
        {
            DataContext = new SettingsViewModel(
                Connected(), new FakeIdentityStore(paired: true), new FakeNavigator()),
        })),

        new("CameraAccessNotice", () =>
        {
            // The one piece of chrome with a state of its own: rendered refused, which is when it is on screen.
            var notice = new CameraAccessViewModel(new FakeDeviceSettings());
            notice.Explains(BarcodeScan.PermissionDenied);
            return Control(new CameraAccessNotice { DataContext = notice });
        }),
    ];

    public static Screen ByName(string name) =>
        All.FirstOrDefault(screen => screen.Name == name)
        ?? throw new InvalidOperationException($"No screen registered as '{name}'.");

    /// <summary>A batch with one staged book in it, so the tray and the hub render their populated state
    /// rather than only their empty one.</summary>
    public static FakeStagingStore Batch()
    {
        var store = new FakeStagingStore();
        store.Save(new StagedBook
        {
            ClientItemId = "a1",
            Isbn = Isbn,
            Images = [new StagedImage { Type = ScanImageType.FrontCover, Jpeg = Jpeg }],
        });
        return store;
    }

    public static FakeConnectionService Connected(StubScannerService? desktop = null)
    {
        var connection = new FakeConnectionService { Connection = new StubConnection(desktop ?? new StubScannerService()) };
        connection.Report(ConnectionStatus.Connected);
        return connection;
    }

    private static Task<Control> Control(Control control) => Task.FromResult(control);

    public static PageFactories Pages() => new()
    {
        Pairing = navigator => new PairingViewModel(
            new FakeBarcodeScanner(), new FakePairingCoordinator(), new FakeDeviceSettings(), navigator),
        Hub = navigator => new HubViewModel(navigator, Batch()),
        Settings = navigator => new SettingsViewModel(Connected(), new FakeIdentityStore(paired: true), navigator),
        Browse = _ => new BrowseViewModel(
            Connected(), new FakeBarcodeScanner(), new FakeDeviceSettings(), (_, _) => { }, _ => Task.CompletedTask),
        Scan = _ => new IsbnScanViewModel(
            new FakeBarcodeScanner(), Connected(), Batch(), new FakeDeviceSettings(), _ => { }),
        Batch = navigator => new BatchTrayViewModel(
            Batch(), Connected(), navigator, _ => new HubViewModel(navigator, Batch()), () => { }),
    };
}
