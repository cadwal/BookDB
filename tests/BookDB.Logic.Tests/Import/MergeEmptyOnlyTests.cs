using System;
using BookDB.Logic.Import;
using BookDB.Models.Entities;
using Xunit;

namespace BookDB.Logic.Tests.Import;

public class MergeEmptyOnlyTests
{
    [Fact]
    public void FillsNullFields()
    {
        var existing = new Book { Title = "Existing", Subtitle = null };
        var imported = new Book { Title = "Imported", Subtitle = "New Sub" };
        var changed = ImportService.MergeEmptyOnly(existing, imported);
        Assert.True(changed);
        Assert.Equal("New Sub", existing.Subtitle);
        Assert.Equal("Existing", existing.Title); // Title not in MergeEmptyOnly list
    }

    [Fact]
    public void NeverOverwritesExistingFields()
    {
        var existing = new Book { Title = "Existing", Subtitle = "Existing Sub" };
        var imported = new Book { Title = "Imported", Subtitle = "New Sub" };
        ImportService.MergeEmptyOnly(existing, imported);
        Assert.Equal("Existing Sub", existing.Subtitle);
    }

    [Fact]
    public void ReturnsFalseWhenNothingChanged()
    {
        var existing = new Book { Title = "X", Subtitle = "Y", Keywords = "k" };
        var imported = new Book { Title = "X", Subtitle = "Z", Keywords = "new" };
        var changed = ImportService.MergeEmptyOnly(existing, imported);
        Assert.False(changed);
    }

    [Fact]
    public void ReturnsTrueWhenAtLeastOneFieldFilled()
    {
        var existing = new Book { Title = "X", Keywords = null };
        var imported = new Book { Title = "X", Keywords = "sci-fi" };
        var changed = ImportService.MergeEmptyOnly(existing, imported);
        Assert.True(changed);
        Assert.Equal("sci-fi", existing.Keywords);
    }

    [Fact]
    public void FillsNullContributors()
    {
        // MergeEmptyOnly handles Book-level fields; AltTitle and AmazonAsin are import-specific
        var existing = new Book { Title = "X", AltTitle = null, AmazonAsin = null };
        var imported = new Book { Title = "X", AltTitle = "Alt", AmazonAsin = "B001XXXX" };
        var changed = ImportService.MergeEmptyOnly(existing, imported);
        Assert.True(changed);
        Assert.Equal("Alt", existing.AltTitle);
        Assert.Equal("B001XXXX", existing.AmazonAsin);
    }

    [Fact]
    public void MergeEmptyOnly_NewBookFields_MergesWhenExistingNull()
    {
        var existing = new Book { Title = "Existing" };
        var imported = new Book
        {
            Title = "Imported",
            Issn = "0001-4966",
            Lccn = "2020012345",
            DeweyDecimal = "823.914",
            CallNumber = "PR6068.O93",
            Dimensions = "21 x 14 cm",
            Weight = 0.35m,
            ItemValue = 25.00m,
            ValuationDate = DateTime.Parse("2024-01-15"),
            AmazonNewValue = 19.99m,
            AmazonUsedValue = 5.50m,
            AmazonCollectibleValue = 45.00m,
            AmazonNewCount = 12,
            AmazonUsedCount = 8,
            AmazonCollectibleCount = 3,
            SalesRank = 54321,
            LexileLevel = 890,
        };

        var changed = ImportService.MergeEmptyOnly(existing, imported);

        Assert.True(changed);
        Assert.Equal("0001-4966", existing.Issn);
        Assert.Equal("2020012345", existing.Lccn);
        Assert.Equal("823.914", existing.DeweyDecimal);
        Assert.Equal("PR6068.O93", existing.CallNumber);
        Assert.Equal("21 x 14 cm", existing.Dimensions);
        Assert.Equal(0.35m, existing.Weight);
        Assert.Equal(25.00m, existing.ItemValue);
        Assert.Equal(DateTime.Parse("2024-01-15"), existing.ValuationDate);
        Assert.Equal(19.99m, existing.AmazonNewValue);
        Assert.Equal(5.50m, existing.AmazonUsedValue);
        Assert.Equal(45.00m, existing.AmazonCollectibleValue);
        Assert.Equal(12, existing.AmazonNewCount);
        Assert.Equal(8, existing.AmazonUsedCount);
        Assert.Equal(3, existing.AmazonCollectibleCount);
        Assert.Equal(54321, existing.SalesRank);
        Assert.Equal(890, existing.LexileLevel);
    }

    [Fact]
    public void MergeEmptyOnly_NewBookFields_DoesNotOverwriteExisting()
    {
        var existing = new Book
        {
            Title = "Existing",
            Issn = "KEEP-ME",
            Lccn = "KEEP-ME",
            DeweyDecimal = "KEEP-ME",
            CallNumber = "KEEP-ME",
            Dimensions = "KEEP-ME",
            Weight = 1.00m,
            ItemValue = 100.00m,
            ValuationDate = DateTime.Parse("2023-01-01"),
            AmazonNewValue = 99.00m,
            AmazonUsedValue = 50.00m,
            AmazonCollectibleValue = 150.00m,
            AmazonNewCount = 99,
            AmazonUsedCount = 88,
            AmazonCollectibleCount = 77,
            SalesRank = 99999,
            LexileLevel = 999,
        };
        var imported = new Book
        {
            Title = "Imported",
            Issn = "OVERWRITE",
            Lccn = "OVERWRITE",
            DeweyDecimal = "OVERWRITE",
            CallNumber = "OVERWRITE",
            Dimensions = "OVERWRITE",
            Weight = 0.01m,
            ItemValue = 1.00m,
            ValuationDate = DateTime.Parse("2099-01-01"),
            AmazonNewValue = 0.01m,
            AmazonUsedValue = 0.01m,
            AmazonCollectibleValue = 0.01m,
            AmazonNewCount = 1,
            AmazonUsedCount = 1,
            AmazonCollectibleCount = 1,
            SalesRank = 1,
            LexileLevel = 1,
        };

        ImportService.MergeEmptyOnly(existing, imported);

        Assert.Equal("KEEP-ME", existing.Issn);
        Assert.Equal("KEEP-ME", existing.Lccn);
        Assert.Equal("KEEP-ME", existing.DeweyDecimal);
        Assert.Equal("KEEP-ME", existing.CallNumber);
        Assert.Equal("KEEP-ME", existing.Dimensions);
        Assert.Equal(1.00m, existing.Weight);
        Assert.Equal(100.00m, existing.ItemValue);
        Assert.Equal(DateTime.Parse("2023-01-01"), existing.ValuationDate);
        Assert.Equal(99.00m, existing.AmazonNewValue);
        Assert.Equal(50.00m, existing.AmazonUsedValue);
        Assert.Equal(150.00m, existing.AmazonCollectibleValue);
        Assert.Equal(99, existing.AmazonNewCount);
        Assert.Equal(88, existing.AmazonUsedCount);
        Assert.Equal(77, existing.AmazonCollectibleCount);
        Assert.Equal(99999, existing.SalesRank);
        Assert.Equal(999, existing.LexileLevel);
    }
}
