namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Implemented by view models whose hover-popup image is fetched on first hover instead of
/// being kept in memory for every row. <see cref="CoverHoverPopupBehavior"/> calls it on
/// pointer enter; view models without it keep their eagerly loaded image behavior.
/// </summary>
public interface IHoverImageLoader
{
    /// <summary>Start loading the hover image; must be a no-op while a load is in flight.</summary>
    void RequestHoverImageLoad();
}
