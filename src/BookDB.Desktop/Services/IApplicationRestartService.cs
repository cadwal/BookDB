using System.Threading.Tasks;

namespace BookDB.Desktop.Services;

/// <summary>
/// Drives a forced restart: there is no in-process backend swap, so a changed backend or connection only
/// takes effect in a freshly started process.
/// </summary>
public interface IApplicationRestartService
{
    /// <summary>Shows a modal restart confirmation with the given message; <c>true</c> when the user accepts.</summary>
    Task<bool> ConfirmRestartAsync(string message);

    /// <summary>Spawns a fresh instance and cleanly shuts this one down. Does not return on success.</summary>
    void Restart();
}
