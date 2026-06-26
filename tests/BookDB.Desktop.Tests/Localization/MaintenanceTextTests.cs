using System;
using BookDB.Desktop.Localization;
using BookDB.Logic.Services;
using BookDB.Models;
using Xunit;

namespace BookDB.Desktop.Tests.Localization;

/// <summary>
/// Guards the enum↔resource contract: every <see cref="MaintenanceStep"/> and
/// <see cref="MaintenanceCheckStatus"/> value must map to a non-empty localized string. A new enum value with no
/// mapping throws (caught here), and an empty/missing resource fails the non-empty assertion — so the enums and
/// the resource strings can never silently drift out of sync.
/// </summary>
public sealed class MaintenanceTextTests
{
    [Fact]
    public void EveryMaintenanceStep_MapsToNonEmptyString()
    {
        foreach (var step in Enum.GetValues<MaintenanceStep>())
        {
            var text = MaintenanceText.Describe(step);
            Assert.False(string.IsNullOrWhiteSpace(text), $"MaintenanceStep.{step} has no localized text");
        }
    }

    [Fact]
    public void EveryMaintenanceCheckStatus_MapsToNonEmptyString()
    {
        foreach (var status in Enum.GetValues<MaintenanceCheckStatus>())
        {
            var text = MaintenanceText.Describe(status);
            Assert.False(string.IsNullOrWhiteSpace(text), $"MaintenanceCheckStatus.{status} has no localized text");
        }
    }
}
