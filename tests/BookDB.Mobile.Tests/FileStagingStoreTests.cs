using System;
using System.IO;
using System.Linq;
using BookDB.Contracts;
using BookDB.Mobile.Staging;
using SkiaSharp;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The staging store against a real temp directory. What matters here is that a scanned stack
/// survives the app being killed, so most of these assert through a *second* store opened on the same
/// folder — which is exactly what a relaunch does.</summary>
public class FileStagingStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bookdb-staging-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private FileStagingStore Store() => new(_directory);

    private static StagedBook Book(string id, string isbn, params ScanImageType[] types) => new()
    {
        ClientItemId = id,
        Isbn = isbn,
        Images = [.. types.Select(type => new StagedImage { Type = type, Jpeg = ScanImageEncoderTests.Jpeg(600, 400) })],
    };

    [Fact]
    public void AFreshDevice_HasNothingStaged()
    {
        Assert.Empty(Store().Items);
    }

    [Fact]
    public void AStagedBook_SurvivesARelaunch()
    {
        Store().Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover, ScanImageType.BackCover));

        var relaunched = Store();

        var item = Assert.Single(relaunched.Items);
        Assert.Equal("9780441013593", item.Isbn);
        Assert.Equal([ScanImageType.FrontCover, ScanImageType.BackCover], item.ImageTypes);
        Assert.NotNull(relaunched.ReadImage("aaa1", ScanImageType.FrontCover));
    }

    [Fact]
    public void AWholeStack_KeepsTheOrderItWasScannedIn()
    {
        var store = Store();
        store.Save(Book("aaa1", "1111111111111"));
        store.Save(Book("bbb2", "2222222222222"));
        store.Save(Book("ccc3", "3333333333333"));

        Assert.Equal(["1111111111111", "2222222222222", "3333333333333"], Store().Items.Select(item => item.Isbn));
    }

    [Fact]
    public void AnIsbnOnlySet_IsStagedWithNoPhotosAndNoThumbnail()
    {
        Store().Save(Book("aaa1", "9780441013593"));

        var item = Assert.Single(Store().Items);
        Assert.Empty(item.ImageTypes);
        Assert.Null(item.Thumbnail);
    }

    [Fact]
    public void TheTrayThumbnail_IsSmallerThanTheCoverItComesFrom()
    {
        Store().Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));

        var item = Assert.Single(Store().Items);
        Assert.NotNull(item.Thumbnail);
        using var thumbnail = SKBitmap.Decode(item.Thumbnail);
        Assert.True(thumbnail.Width < 600, "the tray should not hold full-size covers");
    }

    /// <summary>Wireframe F wants the front cover on the row; a set that has no front should still show one.</summary>
    [Fact]
    public void WithoutAFrontCover_AnotherCoverStandsInOnTheRow()
    {
        Store().Save(Book("aaa1", "9780441013593", ScanImageType.Spine));

        Assert.NotNull(Assert.Single(Store().Items).Thumbnail);
    }

    [Fact]
    public void SavingTheSameBookAgain_ReplacesItAndKeepsItsPlace()
    {
        var store = Store();
        store.Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover, ScanImageType.BackCover));
        store.Save(Book("bbb2", "2222222222222"));

        store.Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover));

        var relaunched = Store();
        Assert.Equal(2, relaunched.Items.Count);
        Assert.Equal("1111111111111", relaunched.Items[0].Isbn);
        Assert.Equal([ScanImageType.FrontCover], relaunched.Items[0].ImageTypes);

        // The dropped cover has to be gone from disk too, not just from the manifest.
        Assert.Null(relaunched.ReadImage("aaa1", ScanImageType.BackCover));
    }

    [Fact]
    public void RemovingABook_TakesItsPhotosWithIt()
    {
        var store = Store();
        store.Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover));
        store.Save(Book("bbb2", "2222222222222"));

        store.Remove("aaa1");

        Assert.Equal("2222222222222", Assert.Single(Store().Items).Isbn);
        Assert.False(Directory.Exists(Path.Combine(_directory, "staging", "aaa1")));
    }

    [Fact]
    public void RemovingSomethingThatIsNotThere_ChangesNothing()
    {
        var store = Store();
        store.Save(Book("aaa1", "1111111111111"));
        var changes = 0;
        store.Changed += (_, _) => changes++;

        store.Remove("nothere");

        Assert.Single(store.Items);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void SavingAndRemoving_TellTheTray()
    {
        var store = Store();
        var changes = 0;
        store.Changed += (_, _) => changes++;

        store.Save(Book("aaa1", "1111111111111"));
        store.Remove("aaa1");

        Assert.Equal(2, changes);
    }

    /// <summary>The leavings of a save that was killed before it could record what it had written.</summary>
    [Fact]
    public void PhotosTheManifestDoesNotKnowAbout_AreSweptOnStartup()
    {
        Store().Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover));
        string orphan = Path.Combine(_directory, "staging", "zzz9");
        Directory.CreateDirectory(orphan);
        File.WriteAllBytes(Path.Combine(orphan, "FrontCover.jpg"), [1, 2, 3]);

        var relaunched = Store();

        Assert.Single(relaunched.Items);
        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public void ACorruptManifest_LeavesNothingStagedRatherThanFailingToStart()
    {
        Store().Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover));
        File.WriteAllText(Path.Combine(_directory, "staging", "manifest.json"), "{ not json");

        Assert.Empty(Store().Items);
    }

    [Fact]
    public void AnImageTheManifestNamesButThatIsGone_IsSimplyNotListed()
    {
        Store().Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover, ScanImageType.Spine));
        File.Delete(Path.Combine(_directory, "staging", "aaa1", "Spine.jpg"));

        Assert.Equal([ScanImageType.FrontCover], Assert.Single(Store().Items).ImageTypes);
    }

    [Fact]
    public void AnIdThatIsNotAnId_IsRefusedRatherThanFollowedAsAPath()
    {
        Assert.Throws<ArgumentException>(() => Store().Save(Book("../escape", "1111111111111")));
    }
}
