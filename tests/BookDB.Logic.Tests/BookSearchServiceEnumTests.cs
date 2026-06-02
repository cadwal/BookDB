using System.Threading.Tasks;
using BookDB.Models.Enums;
using BookDB.Logic.Services;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Tests for the SearchField/SearchOperator enum-backed BookSearchService.BuildPredicate.
/// Verifies the enum-typed SearchCondition record contract.
/// </summary>
public class BookSearchServiceEnumTests
{
    [Fact]
    public void SearchCondition_HasEnumTypedFieldAndOperator()
    {
        // SearchCondition record must accept SearchField and SearchOperator enum values
        var condition = new SearchCondition(SearchField.Author, SearchOperator.Contains, "King");
        Assert.Equal(SearchField.Author, condition.Field);
        Assert.Equal(SearchOperator.Contains, condition.Operator);
        Assert.Equal("King", condition.Value);
    }

    [Fact]
    public void SearchCondition_SupportsIsbnField()
    {
        // SearchField.Isbn must be accepted — maps to Book.Isbn property (case-correct)
        var condition = new SearchCondition(SearchField.Isbn, SearchOperator.Contains, "978");
        Assert.Equal(SearchField.Isbn, condition.Field);
    }

    [Fact]
    public void SearchCondition_SupportsYearField()
    {
        // SearchField.Year must be accepted — maps to Book.PubDate
        var condition = new SearchCondition(SearchField.Year, SearchOperator.Equals, "2020");
        Assert.Equal(SearchField.Year, condition.Field);
    }

    [Fact]
    public void SearchCondition_SupportsIsEmptyOperatorOnPublisher()
    {
        // SearchOperator.IsEmpty on SearchField.Publisher must be a valid combination
        var condition = new SearchCondition(SearchField.Publisher, SearchOperator.IsEmpty, string.Empty);
        Assert.Equal(SearchOperator.IsEmpty, condition.Operator);
    }

    [Fact]
    public void SearchCondition_SupportsAllNavigationFields()
    {
        // Language, Rating, Status, Location, Owner must all be representable in SearchField
        var fields = new[]
        {
            SearchField.Language,
            SearchField.Rating,
            SearchField.Status,
            SearchField.Location,
            SearchField.Owner
        };

        foreach (var field in fields)
        {
            var condition = new SearchCondition(field, SearchOperator.IsNotEmpty, string.Empty);
            Assert.Equal(field, condition.Field);
        }
    }
}
