using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using BookDB.Desktop.Views;
using BookDB.Logic.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// The release-notes offer end to end: opening the main window after an update raises the prompt by
/// itself, Enter accepts into the "what's new" viewer and records the version, Esc defers and leaves
/// the version unrecorded so the next start asks again.
/// </summary>
public class ReleaseNotesPromptTests : HeadlessTest
{
    private const string LastSeenKey = "ReleaseNotes.LastSeenVersion";

    private static IReleaseNotesService UpdatedTo(string version, string notes)
    {
        var service = Substitute.For<IReleaseNotesService>();
        service.CurrentVersion.Returns(version);
        service.GetNotes(version, Arg.Any<CultureInfo?>()).Returns(notes);
        return service;
    }

    private static async Task<MainWindow> ShowMainWindowAsync(TestHost host)
    {
        var main = host.Resolve<MainWindow>();
        await ((MainWindowViewModel)main.DataContext!).InitializeAsync();
        main.Show();
        Ui.Pump();
        return main;
    }

    [Fact]
    public async Task OpeningAfterAnUpdate_PromptsByItself_AndEnterOpensTheViewer()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(UpdatedTo("9.9.9", "Brand new things.")));
            var settings = host.Resolve<ISettingsService>();
            await settings.SetAsync(LastSeenKey, "1.0.0", ct);

            var main = await ShowMainWindowAsync(host);
            await Ui.PumpUntil(() => main.OwnedWindows.OfType<MessageDialog>().Any(), ct);

            var prompt = main.OwnedWindows.OfType<MessageDialog>().Single();
            prompt.Press(PhysicalKey.Enter);
            await Ui.PumpUntil(() => main.OwnedWindows.OfType<ReleaseNotesWindow>().Any(), ct);

            Assert.Equal("9.9.9", await settings.GetAsync(LastSeenKey, ct));

            var viewer = main.OwnedWindows.OfType<ReleaseNotesWindow>().Single();
            viewer.Press(PhysicalKey.Escape);
            await Ui.PumpUntil(() => !main.OwnedWindows.OfType<ReleaseNotesWindow>().Any(), ct);
            main.Close();
        });
    }

    [Fact]
    public async Task EscDefers_NoViewer_AndTheVersionStaysUnrecorded()
    {
        var ct = TestContext.Current.CancellationToken;

        await RunUi(async () =>
        {
            using var host = TestHost.Create(s => s.AddSingleton(UpdatedTo("9.9.9", "Brand new things.")));
            var settings = host.Resolve<ISettingsService>();
            await settings.SetAsync(LastSeenKey, "1.0.0", ct);

            var main = await ShowMainWindowAsync(host);
            await Ui.PumpUntil(() => main.OwnedWindows.OfType<MessageDialog>().Any(), ct);

            main.OwnedWindows.OfType<MessageDialog>().Single().Press(PhysicalKey.Escape);
            await Ui.PumpUntil(() => !main.OwnedWindows.OfType<MessageDialog>().Any(), ct);
            Ui.Pump();

            Assert.Empty(main.OwnedWindows.OfType<ReleaseNotesWindow>());
            Assert.Equal("1.0.0", await settings.GetAsync(LastSeenKey, ct));
            main.Close();
        });
    }
}
