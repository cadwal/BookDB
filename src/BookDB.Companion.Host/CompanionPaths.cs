using System.IO;

namespace BookDB.Companion.Host;

public static class CompanionPaths
{
    /// <summary>
    /// Companion state sits in a <c>companion/</c> folder beside <c>config.json</c>, not in the library
    /// database: a pairing belongs to this desktop install, not to whichever library happens to be open.
    /// </summary>
    public static string CompanionDirectory(string configFilePath)
        => Path.Combine(Path.GetDirectoryName(configFilePath) ?? ".", "companion");
}
