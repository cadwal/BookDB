namespace BookDB.Logic.Helpers;

/// <summary>
/// Builds case-insensitive <c>LIKE</c> patterns that behave the same on both backends. Pair the result
/// with the three-argument <c>EF.Functions.Like(column.ToLower(), pattern, <see cref="Escape"/>)</c>
/// overload: SQLite <c>LIKE</c> folds ASCII case by default but Postgres <c>LIKE</c> is case-sensitive, so
/// both sides are lower-cased; and user input is escaped against the <c>%</c>/<c>_</c> wildcards (the old
/// <c>[%]</c> bracket form was SQL-Server-only and matched literally on SQLite/Postgres).
/// </summary>
internal static class LikePattern
{
    /// <summary>The escape character to pass as the third argument of <c>EF.Functions.Like</c>.</summary>
    public const string Escape = "\\";

    /// <summary>A lower-cased <c>%value%</c> contains-pattern with <c>\</c>, <c>%</c> and <c>_</c> escaped.</summary>
    public static string Contains(string value) =>
        "%" + value.ToLowerInvariant()
            .Replace(Escape, Escape + Escape)
            .Replace("%", Escape + "%")
            .Replace("_", Escape + "_") + "%";
}
