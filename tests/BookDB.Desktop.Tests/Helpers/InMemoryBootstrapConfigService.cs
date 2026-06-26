using System;
using BookDB.Desktop.Services;
using BookDB.Models;

namespace BookDB.Desktop.Tests.Helpers;

/// <summary>In-memory <see cref="IBootstrapConfigService"/> backed by a single mutable config instance.</summary>
public sealed class InMemoryBootstrapConfigService : IBootstrapConfigService
{
    public BootstrapConfig Config { get; } = new();

    public BootstrapConfig Load() => Config;

    public void Update(Action<BootstrapConfig> mutate) => mutate(Config);
}
