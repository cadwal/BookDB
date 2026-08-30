using System.Globalization;
using BookDB.Contracts;
using BookDB.Mobile.Localization;

namespace BookDB.Mobile.ViewModels;

/// <summary>One choice in the collection filter. The "everything" choice is one of these with no collection
/// behind it, so the picker has no special first item to handle.</summary>
public sealed class CollectionFilterViewModel
{
    private readonly CollectionRef? _collection;

    private CollectionFilterViewModel(CollectionRef? collection) => _collection = collection;

    public static CollectionFilterViewModel All() => new(null);

    public static CollectionFilterViewModel For(CollectionRef collection) => new(collection);

    public int? CollectionId => _collection?.CollectionId;

    /// <summary>Resolved on demand rather than at construction, so a language change is reflected.</summary>
    public string Label => _collection is null
        ? Resources.Browse_AllCollections
        : string.Format(
            CultureInfo.CurrentCulture,
            Resources.Browse_CollectionWithCount,
            _collection.Name,
            _collection.BookCount);
}
