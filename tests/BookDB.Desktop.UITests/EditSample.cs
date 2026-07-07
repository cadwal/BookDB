using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Desktop.UITests;

/// <summary>
/// Seeds one fully-populated book plus an A/B pair of every lookup the edit form binds (and a second category), so
/// an edit test can retype the text fields, flip every selector from A to B, and assert the whole form round-trips.
/// The book starts on the "A" value of each lookup with one author and one selected category. Returns its id.
/// </summary>
public static class EditSample
{
    public const string OriginalTitle = "Original Title";
    public const string OriginalIsbn = "1111111111";

    public static async Task<int> SeedAsync(TestHost host, CancellationToken ct)
    {
        var factory = host.Resolve<IDbContextFactory<BookDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);

        var formatA = new Format { Name = "Format A" };
        var formatB = new Format { Name = "Format B" };
        var publisherA = new Publisher { Name = "Publisher A" };
        var publisherB = new Publisher { Name = "Publisher B" };
        var seriesA = new Series { Name = "Series A" };
        var seriesB = new Series { Name = "Series B" };
        var languageA = new Language { Name = "Language A" };
        var languageB = new Language { Name = "Language B" };
        var editionA = new Edition { Name = "Edition A" };
        var editionB = new Edition { Name = "Edition B" };
        var ratingA = new Rating { Name = "Rating A" };
        var ratingB = new Rating { Name = "Rating B" };
        var conditionA = new Condition { Name = "Condition A" };
        var conditionB = new Condition { Name = "Condition B" };
        var statusA = new Status { Name = "Status A" };
        var statusB = new Status { Name = "Status B" };
        var readingLevelA = new ReadingLevel { Name = "ReadingLevel A" };
        var readingLevelB = new ReadingLevel { Name = "ReadingLevel B" };
        var locationA = new Location { Name = "Location A" };
        var locationB = new Location { Name = "Location B" };
        var ownerA = new Owner { Name = "Owner A" };
        var ownerB = new Owner { Name = "Owner B" };
        var purchasePlaceA = new PurchasePlace { Name = "PurchasePlace A" };
        var purchasePlaceB = new PurchasePlace { Name = "PurchasePlace B" };
        var sourceA = new Source { Name = "Source A" };
        var sourceB = new Source { Name = "Source B" };
        var categoryA = new Category { Name = "Category A" };
        var categoryB = new Category { Name = "Category B" };
        var author = new Person { DisplayName = "Orig Author", SortName = "Orig Author" };

        db.Formats.AddRange(formatA, formatB);
        db.Publishers.AddRange(publisherA, publisherB);
        db.Series.AddRange(seriesA, seriesB);
        db.Languages.AddRange(languageA, languageB);
        db.Editions.AddRange(editionA, editionB);
        db.Ratings.AddRange(ratingA, ratingB);
        db.Conditions.AddRange(conditionA, conditionB);
        db.Statuses.AddRange(statusA, statusB);
        db.ReadingLevels.AddRange(readingLevelA, readingLevelB);
        db.Locations.AddRange(locationA, locationB);
        db.Owners.AddRange(ownerA, ownerB);
        db.PurchasePlaces.AddRange(purchasePlaceA, purchasePlaceB);
        db.Sources.AddRange(sourceA, sourceB);
        db.Categories.AddRange(categoryA, categoryB);
        db.People.Add(author);
        await db.SaveChangesAsync(ct);

        var authorRole = await db.ContributorRoles.FirstAsync(r => r.Code == "Author", ct);

        var book = new Book
        {
            Title = OriginalTitle,
            Isbn = OriginalIsbn,
            FormatId = formatA.FormatId,
            PublisherId = publisherA.PublisherId,
            SeriesId = seriesA.SeriesId,
            LanguageId = languageA.LanguageId,
            EditionId = editionA.EditionId,
            RatingId = ratingA.RatingId,
            ConditionId = conditionA.ConditionId,
            StatusId = statusA.StatusId,
            ReadingLevelId = readingLevelA.ReadingLevelId,
            LocationId = locationA.LocationId,
            OwnerId = ownerA.OwnerId,
            PurchasePlaceId = purchasePlaceA.PurchasePlaceId,
            SourceId = sourceA.SourceId,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);

        db.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = author.PersonId,
            ContributorRoleId = authorRole.ContributorRoleId,
            SortOrder = 0,
        });
        db.BookCategories.Add(new BookCategory { BookId = book.BookId, CategoryId = categoryA.CategoryId });
        await db.SaveChangesAsync(ct);

        return book.BookId;
    }
}
