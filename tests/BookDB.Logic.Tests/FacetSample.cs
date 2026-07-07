using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Tests;

/// <summary>
/// Seeds three books that span every facet dimension: one "Solo Book" holding a unique value in each field, plus
/// two "Shared Book" rows that share a second value. Every facet therefore has exactly two values with counts 1
/// (solo) and 2 (shared), so a facet reading the wrong field shows up as a wrong name or count.
/// </summary>
public static class FacetSample
{
    public const string SoloTitle = "Solo Book";
    public const string SharedTitleOne = "Shared Book One";
    public const string SharedTitleTwo = "Shared Book Two";

    public static async Task SeedAsync(IDbContextFactory<BookDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var soloSeries = new Series { Name = "Solo Series" };
        var sharedSeries = new Series { Name = "Shared Series" };
        var soloPublisher = new Publisher { Name = "Solo Publisher" };
        var sharedPublisher = new Publisher { Name = "Shared Publisher" };
        var soloCategory = new Category { Name = "Solo Category" };
        var sharedCategory = new Category { Name = "Shared Category" };
        var soloFormat = new Format { Name = "Solo Format" };
        var sharedFormat = new Format { Name = "Shared Format" };
        var soloLanguage = new Language { Name = "Solo Language" };
        var sharedLanguage = new Language { Name = "Shared Language" };
        var soloRating = new Rating { Name = "Solo Rating" };
        var sharedRating = new Rating { Name = "Shared Rating" };
        var soloStatus = new Status { Name = "Solo Status" };
        var sharedStatus = new Status { Name = "Shared Status" };
        var soloLocation = new Location { Name = "Solo Location" };
        var sharedLocation = new Location { Name = "Shared Location" };
        var soloOwner = new Owner { Name = "Solo Owner" };
        var sharedOwner = new Owner { Name = "Shared Owner" };
        var soloPerson = new Person { DisplayName = "Ann Author", SortName = "Ann Author" };
        var sharedPerson = new Person { DisplayName = "Bob Writer", SortName = "Bob Writer" };

        db.Series.AddRange(soloSeries, sharedSeries);
        db.Publishers.AddRange(soloPublisher, sharedPublisher);
        db.Categories.AddRange(soloCategory, sharedCategory);
        db.Formats.AddRange(soloFormat, sharedFormat);
        db.Languages.AddRange(soloLanguage, sharedLanguage);
        db.Ratings.AddRange(soloRating, sharedRating);
        db.Statuses.AddRange(soloStatus, sharedStatus);
        db.Locations.AddRange(soloLocation, sharedLocation);
        db.Owners.AddRange(soloOwner, sharedOwner);
        db.People.AddRange(soloPerson, sharedPerson);
        await db.SaveChangesAsync(ct);

        var authorRole = await db.ContributorRoles.FirstAsync(r => r.Code == "Author", ct);

        var solo = new Book
        {
            Title = SoloTitle,
            SeriesId = soloSeries.SeriesId,
            PublisherId = soloPublisher.PublisherId,
            FormatId = soloFormat.FormatId,
            LanguageId = soloLanguage.LanguageId,
            RatingId = soloRating.RatingId,
            StatusId = soloStatus.StatusId,
            LocationId = soloLocation.LocationId,
            OwnerId = soloOwner.OwnerId,
        };
        var sharedOne = new Book
        {
            Title = SharedTitleOne,
            SeriesId = sharedSeries.SeriesId,
            PublisherId = sharedPublisher.PublisherId,
            FormatId = sharedFormat.FormatId,
            LanguageId = sharedLanguage.LanguageId,
            RatingId = sharedRating.RatingId,
            StatusId = sharedStatus.StatusId,
            LocationId = sharedLocation.LocationId,
            OwnerId = sharedOwner.OwnerId,
        };
        var sharedTwo = new Book
        {
            Title = SharedTitleTwo,
            SeriesId = sharedSeries.SeriesId,
            PublisherId = sharedPublisher.PublisherId,
            FormatId = sharedFormat.FormatId,
            LanguageId = sharedLanguage.LanguageId,
            RatingId = sharedRating.RatingId,
            StatusId = sharedStatus.StatusId,
            LocationId = sharedLocation.LocationId,
            OwnerId = sharedOwner.OwnerId,
        };
        db.Books.AddRange(solo, sharedOne, sharedTwo);
        await db.SaveChangesAsync(ct);

        db.BookCategories.AddRange(
            new BookCategory { BookId = solo.BookId, CategoryId = soloCategory.CategoryId },
            new BookCategory { BookId = sharedOne.BookId, CategoryId = sharedCategory.CategoryId },
            new BookCategory { BookId = sharedTwo.BookId, CategoryId = sharedCategory.CategoryId });
        db.BookContributors.AddRange(
            new BookContributor { BookId = solo.BookId, PersonId = soloPerson.PersonId, ContributorRoleId = authorRole.ContributorRoleId, SortOrder = 0 },
            new BookContributor { BookId = sharedOne.BookId, PersonId = sharedPerson.PersonId, ContributorRoleId = authorRole.ContributorRoleId, SortOrder = 0 },
            new BookContributor { BookId = sharedTwo.BookId, PersonId = sharedPerson.PersonId, ContributorRoleId = authorRole.ContributorRoleId, SortOrder = 0 });
        await db.SaveChangesAsync(ct);
    }
}
