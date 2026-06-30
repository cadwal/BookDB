namespace BookDB.Data;

/// <summary>
/// Escapes the <c>LIKE</c> wildcards (<c>\</c>, <c>%</c>, <c>_</c>) in user input so they match literally, paired
/// with the three-argument <c>EF.Functions.Like/ILike(column, pattern, <see cref="EscapeChar"/>)</c> overload.
/// Engine-neutral pure string work — no case folding (each provider folds case its own way: MySQL via a
/// <c>utf8mb4_*_ci</c> collation, PostgreSQL via <c>ILIKE</c>/<c>lower()</c>, SQLite via <c>NOCASE</c>).
/// </summary>
public static class LikeEscaping
{
    /// <summary>The escape character to pass as the third argument of <c>EF.Functions.Like</c>/<c>ILike</c>.</summary>
    public const string EscapeChar = "\\";

    public static string Escape(string value) =>
        value.Replace(EscapeChar, EscapeChar + EscapeChar).Replace("%", EscapeChar + "%").Replace("_", EscapeChar + "_");
}
