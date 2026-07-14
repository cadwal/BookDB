using System.Threading.Tasks;
using BookDB.Desktop.Services;
using BookDB.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Closing the main window with unsaved edits must not silently drop them: the aggregate
/// shutdown confirmation prompts the dirty inline pane and every guarded secondary window,
/// and any KeepEditing refusal keeps the app open.
/// </summary>
public class MainWindowShutdownGuardTests : HeadlessTest
{
    [Fact]
    public async Task DirtyInlinePane_KeepEditingAbortsClose_DiscardProceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            var windowService = Substitute.For<IWindowService>();
            windowService.ConfirmCloseGuardedWindowsAsync().Returns(Task.FromResult(true));
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var book = await SeedData.AddBookAsync(host, "Inline Edit", ct);
            var vm = host.Resolve<MainWindowViewModel>();
            await vm.InitializeAsync(ct);
            await vm.BookDetail.LoadBookAsync(book.BookId);
            await vm.BookDetail.EnterEditModeCommand.ExecuteAsync(null);
            vm.BookDetail.EditTitle = "Inline Edit — changed";
            Assert.True(vm.BookDetail.HasUnsavedChanges);

            // KeepEditing on the inline pane aborts the whole close.
            windowService.ShowUnsavedChangesDialogAsync(Arg.Any<string>())
                .Returns(UnsavedChangesResult.KeepEditing);
            Assert.False(await vm.ConfirmShutdownAsync());

            // Discard lets it proceed.
            windowService.ShowUnsavedChangesDialogAsync(Arg.Any<string>())
                .Returns(UnsavedChangesResult.Discard);
            Assert.True(await vm.ConfirmShutdownAsync());
        });
    }

    [Fact]
    public async Task GuardedSecondaryWindowRefusal_AbortsClose()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunUi(async () =>
        {
            var windowService = Substitute.For<IWindowService>();
            windowService.ConfirmCloseGuardedWindowsAsync().Returns(Task.FromResult(false));
            using var host = TestHost.Create(s => s.AddSingleton(windowService));
            var vm = host.Resolve<MainWindowViewModel>();
            await vm.InitializeAsync(ct);

            // No batch, clean inline pane — only the guarded-windows step decides, and it refuses.
            Assert.False(await vm.ConfirmShutdownAsync());
            await windowService.Received(1).ConfirmCloseGuardedWindowsAsync();
            windowService.DidNotReceive().CloseAllSecondaryWindows();
        });
    }
}
