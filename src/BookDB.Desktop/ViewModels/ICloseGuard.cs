using System.Threading.Tasks;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Implemented by ViewModels that need to intercept window close with an
/// async confirmation (e.g. unsaved-changes or running-batch prompts).
/// The <see cref="Behaviors.WindowCloseGuardBehavior"/> subscribes to the
/// Window.Closing event and delegates to this interface.
/// </summary>
public interface ICloseGuard
{
    /// <summary>
    /// Returns <c>true</c> if closing should be blocked and the user prompted.
    /// </summary>
    bool ShouldGuardClose { get; }

    /// <summary>
    /// Shows a confirmation dialog and returns <c>true</c> if closing is allowed.
    /// </summary>
    Task<bool> ConfirmCloseAsync();
}
