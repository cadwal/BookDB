using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace BookDB.Companion.Host;

/// <summary>
/// The paired devices and the server certificate, persisted under the companion folder
/// (<c>devices.json</c> + <c>server.pfx</c>). Loaded once at construction and kept in memory; every
/// mutation writes the file back.
/// </summary>
public sealed class DeviceRegistry : IDeviceRegistry
{
    public const int MaxDevices = 5;

    /// <summary>
    /// Last-used is written back at most this often. A phone makes a burst of calls per batch and the
    /// timestamp is only ever shown as a coarse "last seen", so stamping every call would be pure churn.
    /// </summary>
    private static readonly TimeSpan LastUsedWriteInterval = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, PairedDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    public DeviceRegistry(string directory, TimeProvider clock)
    {
        _directory = directory;
        _clock = clock;
        Load();
    }

    private string DevicesPath => Path.Combine(_directory, "devices.json");
    private string ServerCertificatePath => Path.Combine(_directory, "server.pfx");

    public int Count
    {
        get { lock (_gate) { return _devices.Count; } }
    }

    public bool IsAtCapacity => Count >= MaxDevices;

    public IReadOnlyList<PairedDevice> List()
    {
        lock (_gate)
        {
            return _devices.Values.OrderBy(d => d.PairedAtUtc).ToArray();
        }
    }

    public PairedDevice? Find(string thumbprint)
    {
        lock (_gate)
        {
            return _devices.TryGetValue(thumbprint, out var device) ? device : null;
        }
    }

    public bool IsRegistered(string thumbprint)
    {
        lock (_gate)
        {
            return _devices.ContainsKey(thumbprint);
        }
    }

    public bool TryRegister(PairedDevice device)
    {
        lock (_gate)
        {
            if (!_devices.ContainsKey(device.Thumbprint) && _devices.Count >= MaxDevices)
            {
                return false;
            }

            _devices[device.Thumbprint] = device;
            Save();
            return true;
        }
    }

    public bool Revoke(string thumbprint)
    {
        lock (_gate)
        {
            if (!_devices.Remove(thumbprint))
            {
                return false;
            }

            Save();
            return true;
        }
    }

    public void TouchLastUsed(string thumbprint)
    {
        lock (_gate)
        {
            if (!_devices.TryGetValue(thumbprint, out var device))
            {
                return;
            }

            var now = _clock.GetUtcNow();
            bool worthWriting = now - device.LastUsedUtc >= LastUsedWriteInterval;
            _devices[thumbprint] = device with { LastUsedUtc = now };

            if (worthWriting)
            {
                Save();
            }
        }
    }

    public X509Certificate2 GetOrCreateServerCertificate()
    {
        lock (_gate)
        {
            var existing = TryLoadServerCertificate();
            if (existing is not null)
            {
                return existing;
            }

            var certificate = CertificateFactory.CreateServerCertificate($"BookDB-{Environment.MachineName}");
            WriteProtected(ServerCertificatePath, CertificateFactory.ExportPfx(certificate));
            return certificate;
        }
    }

    private X509Certificate2? TryLoadServerCertificate()
    {
        if (!File.Exists(ServerCertificatePath))
        {
            return null;
        }

        X509Certificate2 certificate;
        try
        {
            certificate = CertificateFactory.LoadPfx(File.ReadAllBytes(ServerCertificatePath));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // An unreadable certificate must not stop the host from coming up: mint a fresh one and keep the
            // old file aside. Paired phones will have to re-pair, which is the documented fallback.
            Quarantine(ServerCertificatePath);
            return null;
        }

        if (certificate.NotAfter.ToUniversalTime() <= _clock.GetUtcNow().UtcDateTime)
        {
            certificate.Dispose();
            return null;
        }

        return certificate;
    }

    private void Load()
    {
        if (!File.Exists(DevicesPath))
        {
            return;
        }

        DeviceFile? file;
        try
        {
            file = JsonSerializer.Deserialize<DeviceFile>(File.ReadAllText(DevicesPath), SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Same reasoning as the certificate: start empty rather than refuse to run, but keep the file so
            // the next Save() cannot silently destroy a recoverable pairing list.
            Quarantine(DevicesPath);
            return;
        }

        foreach (var device in file?.Devices ?? [])
        {
            if (!string.IsNullOrWhiteSpace(device.Thumbprint))
            {
                _devices[device.Thumbprint] = device;
            }
        }
    }

    private void Save()
    {
        var file = new DeviceFile { Devices = _devices.Values.OrderBy(d => d.PairedAtUtc).ToList() };
        WriteProtected(DevicesPath, JsonSerializer.SerializeToUtf8Bytes(file, SerializerOptions));
    }

    private static void WriteProtected(string path, byte[] contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, contents);
        RestrictToOwner(tempPath);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void RestrictToOwner(string path)
    {
        // Windows inherits the user-profile ACL; elsewhere the private key would otherwise be world-readable.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void Quarantine(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (IOException)
        {
            // Best effort only — the caller's fallback (start empty / mint fresh) still holds.
        }
    }

    private sealed class DeviceFile
    {
        public int Version { get; set; } = 1;
        public List<PairedDevice> Devices { get; set; } = [];
    }
}
