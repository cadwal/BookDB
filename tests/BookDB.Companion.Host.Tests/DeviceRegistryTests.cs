using System;
using System.IO;
using System.Text.Json;
using BookDB.Companion.Host;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The persisted half of pairing: devices survive a restart, the cap and revocation behave, and neither a
/// mangled devices.json nor a mangled server.pfx can stop the host from coming up.
/// </summary>
public sealed class DeviceRegistryTests : IDisposable
{
    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"bookdb_companion_{Guid.NewGuid():N}");

    private readonly FixedClock _clock = new() { Now = new DateTimeOffset(2026, 7, 23, 9, 0, 0, TimeSpan.Zero) };

    private DeviceRegistry NewRegistry() => new(_directory, _clock);

    private PairedDevice Device(string name, string thumbprint) => new()
    {
        Name = name,
        Thumbprint = thumbprint,
        PairedAtUtc = _clock.Now,
        LastUsedUtc = _clock.Now,
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void CompanionFolderSitsBesideTheConfigFile()
    {
        string expected = Path.Combine(Path.Combine("C:", "state"), "companion");

        Assert.Equal(expected, CompanionPaths.CompanionDirectory(Path.Combine("C:", "state", "config.json")));
    }

    [Fact]
    public void AnAbsentRegistryStartsEmpty()
    {
        var registry = NewRegistry();

        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.List());
        Assert.False(registry.IsAtCapacity);
    }

    [Fact]
    public void RegisteredDevicesSurviveAReload()
    {
        Assert.True(NewRegistry().TryRegister(Device("Phone", "AA11")));

        var reloaded = NewRegistry();

        var device = Assert.Single(reloaded.List());
        Assert.Equal("Phone", device.Name);
        Assert.Equal("AA11", device.Thumbprint);
        Assert.Equal(_clock.Now, device.PairedAtUtc);
        Assert.True(reloaded.IsRegistered("AA11"));
    }

    [Fact]
    public void ThumbprintLookupIgnoresHexCasing()
    {
        var registry = NewRegistry();
        registry.TryRegister(Device("Phone", "abcdef"));

        Assert.True(registry.IsRegistered("ABCDEF"));
        Assert.NotNull(registry.Find("ABCDEF"));
    }

    [Fact]
    public void DevicesAreListedOldestPairingFirst()
    {
        var registry = NewRegistry();
        registry.TryRegister(Device("First", "A1"));
        _clock.Now = _clock.Now.AddMinutes(5);
        registry.TryRegister(Device("Second", "B2"));

        Assert.Equal(["First", "Second"], Array.ConvertAll([.. registry.List()], d => d.Name));
    }

    [Fact]
    public void TheSixthDeviceIsRefused()
    {
        var registry = NewRegistry();
        for (int i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            Assert.True(registry.TryRegister(Device($"Phone {i}", $"T{i}")));
        }

        Assert.True(registry.IsAtCapacity);
        Assert.False(registry.TryRegister(Device("One too many", "T99")));
        Assert.Equal(DeviceRegistry.MaxDevices, registry.Count);
    }

    [Fact]
    public void ReRegisteringAKnownThumbprintDoesNotConsumeASecondSlot()
    {
        var registry = NewRegistry();
        for (int i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            registry.TryRegister(Device($"Phone {i}", $"T{i}"));
        }

        Assert.True(registry.TryRegister(Device("Phone 0 renamed", "T0")));
        Assert.Equal(DeviceRegistry.MaxDevices, registry.Count);
        Assert.Equal("Phone 0 renamed", registry.Find("T0")!.Name);
    }

    [Fact]
    public void RevokingFreesTheSlotAndPersists()
    {
        var registry = NewRegistry();
        for (int i = 0; i < DeviceRegistry.MaxDevices; i++)
        {
            registry.TryRegister(Device($"Phone {i}", $"T{i}"));
        }

        Assert.True(registry.Revoke("T2"));
        Assert.False(registry.IsRegistered("T2"));
        Assert.True(registry.TryRegister(Device("Replacement", "T99")));

        Assert.False(NewRegistry().IsRegistered("T2"));
    }

    [Fact]
    public void RevokingAnUnknownDeviceReportsNothingWasRemoved()
    {
        Assert.False(NewRegistry().Revoke("NOPE"));
    }

    [Fact]
    public void LastUsedIsStampedAndEventuallyWrittenBack()
    {
        var registry = NewRegistry();
        registry.TryRegister(Device("Phone", "AA11"));

        _clock.Now = _clock.Now.AddHours(2);
        registry.TouchLastUsed("AA11");

        Assert.Equal(_clock.Now, registry.Find("AA11")!.LastUsedUtc);
        Assert.Equal(_clock.Now, NewRegistry().Find("AA11")!.LastUsedUtc);
    }

    [Fact]
    public void RapidCallsUpdateLastUsedInMemoryWithoutRewritingTheFileEveryTime()
    {
        var registry = NewRegistry();
        registry.TryRegister(Device("Phone", "AA11"));
        var pairedAt = _clock.Now;

        _clock.Now = _clock.Now.AddSeconds(2);
        registry.TouchLastUsed("AA11");

        Assert.Equal(_clock.Now, registry.Find("AA11")!.LastUsedUtc);
        Assert.Equal(pairedAt, NewRegistry().Find("AA11")!.LastUsedUtc);
    }

    [Fact]
    public void TouchingAnUnknownDeviceIsIgnored()
    {
        var registry = NewRegistry();

        registry.TouchLastUsed("MISSING");

        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void AMangledRegistryStartsEmptyAndIsKeptAside()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "devices.json");
        File.WriteAllText(path, "{ this is not json");

        var registry = NewRegistry();

        Assert.Equal(0, registry.Count);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void DeviceEntriesWithoutAThumbprintAreDropped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "devices.json"),
            """{"version":1,"devices":[{"name":"Ghost","thumbprint":""},{"name":"Real","thumbprint":"AA11"}]}""");

        var registry = NewRegistry();

        Assert.Equal(1, registry.Count);
        Assert.True(registry.IsRegistered("AA11"));
    }

    [Fact]
    public void TheServerCertificateIsMintedOnceAndReusedAcrossRestarts()
    {
        using var first = NewRegistry().GetOrCreateServerCertificate();
        using var second = NewRegistry().GetOrCreateServerCertificate();

        Assert.True(File.Exists(Path.Combine(_directory, "server.pfx")));
        Assert.True(first.HasPrivateKey);
        Assert.Equal(
            CertificateFactory.Sha256Thumbprint(first),
            CertificateFactory.Sha256Thumbprint(second));
    }

    [Fact]
    public void AMangledServerCertificateIsReplacedRatherThanFatal()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "server.pfx");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        using var certificate = NewRegistry().GetOrCreateServerCertificate();

        Assert.True(certificate.HasPrivateKey);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void AnExpiredServerCertificateIsReplaced()
    {
        using var original = NewRegistry().GetOrCreateServerCertificate();

        _clock.Now = _clock.Now.AddYears(6);
        using var replacement = NewRegistry().GetOrCreateServerCertificate();

        Assert.NotEqual(
            CertificateFactory.Sha256Thumbprint(original),
            CertificateFactory.Sha256Thumbprint(replacement));
    }

    [Fact]
    public void TheRegistryFileIsVersionedSoALaterFormatCanBeRecognised()
    {
        NewRegistry().TryRegister(Device("Phone", "AA11"));

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "devices.json")));

        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
    }
}
