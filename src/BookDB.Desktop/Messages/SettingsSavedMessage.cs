using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Sent after settings are saved to signal that dependent ViewModels should reload their settings.
/// </summary>
public sealed class SettingsSavedMessage : ValueChangedMessage<bool>
{
    public SettingsSavedMessage() : base(true) { }
}
