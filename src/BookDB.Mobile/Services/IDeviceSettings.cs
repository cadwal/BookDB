namespace BookDB.Mobile.Services;

/// <summary>The device's own settings page for this app — where a permission that was refused can be granted
/// again, since the app itself is no longer allowed to ask.</summary>
public interface IDeviceSettings
{
    void OpenAppSettings();
}
