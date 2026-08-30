namespace BookDB.Mobile.ViewModels;

/// <summary>One labelled line of the detail view. Only fields the book actually has become one of these, so
/// the view needs no per-field visibility of its own.</summary>
public sealed record DetailFieldViewModel(string Label, string Value);
