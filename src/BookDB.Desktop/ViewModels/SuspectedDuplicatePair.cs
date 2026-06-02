namespace BookDB.Desktop.ViewModels;

public sealed class SuspectedDuplicatePair(PersonRow left, PersonRow right)
{
    public PersonRow Left { get; } = left;
    public PersonRow Right { get; } = right;
    public string DisplayText => $"{Left.DisplayName} / {Right.DisplayName}";
}
