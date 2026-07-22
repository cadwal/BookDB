using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace BookDB.Desktop.Services.UpdateCheck;

/// <summary>Resolves the latest stable version available through one distribution channel. Any failure
/// (offline, rate-limited, tool missing, unparseable output) resolves to null — the caller stays silent.</summary>
public interface IReleaseVersionSource
{
    Task<UpdateVersion?> GetLatestStableAsync(CancellationToken ct = default);
}

/// <summary>Latest non-prerelease from the GitHub Releases API for <c>cadwal/BookDB</c>. Used for the
/// GitHub and AppMan channels (AM's updater re-reads the same releases).</summary>
public sealed class GitHubReleaseVersionSource(HttpClient http, ILogger<GitHubReleaseVersionSource> logger)
    : IReleaseVersionSource
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/cadwal/BookDB/releases/latest";

    public async Task<UpdateVersion?> GetLatestStableAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            // GitHub rejects requests without a User-Agent.
            request.Headers.UserAgent.ParseAdd("BookDB-update-check");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(ct);
            if (release is null || release.Draft || release.Prerelease) return null;
            return UpdateVersion.TryParse(release.TagName, out var v) ? v : null;
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "GitHub update check failed (ignored)");
            return null;
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}

/// <summary>Latest version winget offers for <c>cadwal.BookDB</c>. Windows-only; asks winget itself so a
/// winget user is only told about a version winget can actually install (its manifest can lag GitHub).</summary>
public sealed class WingetVersionSource(ILogger<WingetVersionSource> logger) : IReleaseVersionSource
{
    private const string PackageId = "cadwal.BookDB";

    public async Task<UpdateVersion?> GetLatestStableAsync(CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
        try
        {
            var psi = new ProcessStartInfo("winget")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add("--id");
            psi.ArgumentList.Add(PackageId);
            psi.ArgumentList.Add("--exact");
            psi.ArgumentList.Add("--disable-interactivity");

            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return ParseWingetVersion(output);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "winget update check failed (ignored)");
            return null;
        }
    }

    // winget show prints a localized "Version: X.Y.Z" line. Match the first key:value line whose key
    // mentions "version" and whose value parses as a release — locale-robust enough for the common cases.
    internal static UpdateVersion? ParseWingetVersion(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (line.AsSpan(0, colon).IndexOf("version", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (UpdateVersion.TryParse(line[(colon + 1)..].Trim(), out var v)) return v;
        }
        return null;
    }
}
