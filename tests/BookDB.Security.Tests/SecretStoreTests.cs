using System;
using System.Collections.Generic;
using BookDB.Data.Interfaces;
using BookDB.Models;
using BookDB.Security;
using GitCredentialManager;
using Xunit;

namespace BookDB.Security.Tests;

/// <summary>
/// Covers the credential-store wrapper, the startup availability probe and its no-store fallback, and the
/// account-key format used to scope a server's password.
/// </summary>
public sealed class SecretStoreTests
{
    [Fact]
    public void CredentialManagerSecretStore_RoundTripsUnderTheBookdbService()
    {
        var fake = new FakeCredentialStore();
        var store = new CredentialManagerSecretStore(fake);

        store.Set("user@host:5432/db", "hunter2");

        Assert.Equal("hunter2", store.Get("user@host:5432/db"));
        // GCM's Windows backend runs new Uri(service); a non-absolute-URL service throws UriFormatException.
        Assert.True(Uri.TryCreate(fake.LastService, UriKind.Absolute, out _),
            $"service must be an absolute URL for Git Credential Manager, was '{fake.LastService}'");

        store.Delete("user@host:5432/db");
        Assert.Null(store.Get("user@host:5432/db"));
    }

    [Fact]
    public void CredentialManagerSecretStore_StoresAccountWithoutASlash()
    {
        // The OS backend folds the account into the service URL, so a '/' in the account (our keys are
        // "user@host:port/database") turns into a URL path segment and the Set/Get targets diverge — the
        // secret is written but never read back. The token handed to the library must be flat.
        var fake = new FakeCredentialStore();
        var store = new CredentialManagerSecretStore(fake);

        store.Set("user@host:3306/db", "hunter2");

        var storedAccount = Assert.Single(fake.GetAccounts(fake.LastService!));
        Assert.DoesNotContain('/', storedAccount);
        Assert.Equal("hunter2", store.Get("user@host:3306/db"));
    }

    [Fact]
    public void CredentialManagerSecretStore_Get_FallsBackToRawAccount_ForPreEncodingCredentials()
    {
        // A password saved by a build from before the encoding fix sits under the raw account; reading must
        // still find it so existing users are not forced to re-enter the password.
        var fake = new FakeCredentialStore();
        fake.AddOrUpdate("https://bookdb.local", "user@host:5432/db", "legacy");
        var store = new CredentialManagerSecretStore(fake);

        Assert.Equal("legacy", store.Get("user@host:5432/db"));
    }

    [Fact]
    public void CredentialManagerSecretStore_Get_ReturnsNull_ForUnknownAccount()
    {
        var store = new CredentialManagerSecretStore(new FakeCredentialStore());

        Assert.Null(store.Get("nobody@nowhere:5432/db"));
    }

    [Fact]
    public void NullSecretStore_GetReturnsNull_AndWritesAreSafeNoOps()
    {
        var store = new NullSecretStore();

        store.Set("a", "b");   // must not throw
        store.Delete("a");     // must not throw
        Assert.Null(store.Get("a"));
    }

    [Fact]
    public void Create_WithWorkingStore_IsAvailable_AndProbesWithARead()
    {
        var fake = new FakeCredentialStore();

        var (store, availability) = SecretStoreFactory.Create(() => fake);

        Assert.IsType<CredentialManagerSecretStore>(store);
        Assert.True(availability.IsAvailable);
        Assert.Null(availability.UnavailableReason);
        Assert.True(fake.GetCalled, "The probe should perform a read to confirm the store responds.");
    }

    [Fact]
    public void Create_WhenStoreUnavailable_FallsBackToNullStore_AndReportsReason()
    {
        var (store, availability) = SecretStoreFactory.Create(
            () => throw new InvalidOperationException("no Secret Service"));

        Assert.IsType<NullSecretStore>(store);
        Assert.False(availability.IsAvailable);
        Assert.Contains("no Secret Service", availability.UnavailableReason);
    }

    private static Func<(ISecretStore, SecretStoreAvailability)> AttemptsReturning(
        Action onCall, params (ISecretStore, SecretStoreAvailability)[] results)
    {
        var call = 0;
        return () =>
        {
            onCall();
            return results[Math.Min(call++, results.Length - 1)];
        };
    }

    private static (ISecretStore, SecretStoreAvailability) AvailableResult(ISecretStore store) =>
        (store, SecretStoreAvailability.Available);

    private static (ISecretStore, SecretStoreAvailability) UnavailableResult(string reason) =>
        (new NullSecretStore(), SecretStoreAvailability.Unavailable(reason));

    [Fact]
    public void CreateWithPlatformDefault_OnLinuxWithNoSelection_DefaultsToSecretServiceAndRetries()
    {
        var env = new Dictionary<string, string>();
        var calls = 0;
        var working = new CredentialManagerSecretStore(new FakeCredentialStore());
        var attempt = AttemptsReturning(() => calls++,
            UnavailableResult("No credential store has been selected."),
            AvailableResult(working));

        var (store, availability) = SecretStoreFactory.CreateWithPlatformDefault(
            attempt,
            isLinux: true,
            key => env.TryGetValue(key, out var v) ? v : null,
            (key, value) => { if (value is null) env.Remove(key); else env[key] = value; });

        Assert.Same(working, store);
        Assert.True(availability.IsAvailable);
        Assert.Equal(2, calls);
        Assert.Equal("secretservice", env["GCM_CREDENTIAL_STORE"]);
    }

    [Fact]
    public void CreateWithPlatformDefault_WhenTheDefaultAlsoFails_RestoresTheEnvironmentAndOriginalReason()
    {
        var env = new Dictionary<string, string>();
        var attempt = AttemptsReturning(() => { },
            UnavailableResult("No credential store has been selected."),
            UnavailableResult("no Secret Service on this session bus"));

        var (store, availability) = SecretStoreFactory.CreateWithPlatformDefault(
            attempt,
            isLinux: true,
            key => env.TryGetValue(key, out var v) ? v : null,
            (key, value) => { if (value is null) env.Remove(key); else env[key] = value; });

        Assert.IsType<NullSecretStore>(store);
        Assert.Contains("No credential store has been selected", availability.UnavailableReason);
        Assert.Empty(env);
    }

    [Fact]
    public void CreateWithPlatformDefault_OnLinuxWithExplicitSelection_DoesNotOverrideIt()
    {
        // A set-but-broken GCM_CREDENTIAL_STORE is the user's choice — surface its failure, don't paper over it.
        var env = new Dictionary<string, string> { ["GCM_CREDENTIAL_STORE"] = "gpg" };
        var calls = 0;
        var attempt = AttemptsReturning(() => calls++, UnavailableResult("gpg is broken"));

        var (_, availability) = SecretStoreFactory.CreateWithPlatformDefault(
            attempt,
            isLinux: true,
            key => env.TryGetValue(key, out var v) ? v : null,
            (key, value) => { if (value is null) env.Remove(key); else env[key] = value; });

        Assert.Contains("gpg is broken", availability.UnavailableReason);
        Assert.Equal(1, calls);
        Assert.Equal("gpg", env["GCM_CREDENTIAL_STORE"]);
    }

    [Fact]
    public void CreateWithPlatformDefault_OffLinux_NeverRetries()
    {
        var env = new Dictionary<string, string>();
        var calls = 0;
        var attempt = AttemptsReturning(() => calls++, UnavailableResult("boom"));

        var (_, availability) = SecretStoreFactory.CreateWithPlatformDefault(
            attempt,
            isLinux: false,
            key => env.TryGetValue(key, out var v) ? v : null,
            (key, value) => { if (value is null) env.Remove(key); else env[key] = value; });

        Assert.False(availability.IsAvailable);
        Assert.Equal(1, calls);
        Assert.Empty(env);
    }

    [Fact]
    public void CreateWithPlatformDefault_WhenTheStoreResolves_NeitherRetriesNorTouchesTheEnvironment()
    {
        var env = new Dictionary<string, string>();
        var calls = 0;
        var working = new CredentialManagerSecretStore(new FakeCredentialStore());
        var attempt = AttemptsReturning(() => calls++, AvailableResult(working));

        var (store, _) = SecretStoreFactory.CreateWithPlatformDefault(
            attempt,
            isLinux: true,
            key => env.TryGetValue(key, out var v) ? v : null,
            (key, value) => { if (value is null) env.Remove(key); else env[key] = value; });

        Assert.Same(working, store);
        Assert.Equal(1, calls);
        Assert.Empty(env);
    }

    [Theory]
    [InlineData("bookdb_user", "db.example.com", 6543, "library", "bookdb_user@db.example.com:6543/library")]
    [InlineData("", "localhost", 5432, "bookdb", "@localhost:5432/bookdb")]
    public void PostgresOptions_AccountKey_MatchesUserAtHostPortDatabase(
        string username, string host, int port, string database, string expected)
    {
        var options = new PostgresOptions { Username = username, Host = host, Port = port, Database = database };

        Assert.Equal(expected, options.AccountKey);
    }

    // In-memory ICredentialStore so the wrapper and probe can be tested without a real OS keychain.
    private sealed class FakeCredentialStore : ICredentialStore
    {
        private readonly Dictionary<(string service, string account), string> _secrets = new();

        public string? LastService { get; private set; }
        public bool GetCalled { get; private set; }

        public ICredential? Get(string service, string account)
        {
            GetCalled = true;
            LastService = service;
            return _secrets.TryGetValue((service, account), out var secret)
                ? new FakeCredential(account, secret)
                : null;
        }

        public void AddOrUpdate(string service, string account, string secret)
        {
            LastService = service;
            _secrets[(service, account)] = secret;
        }

        public bool Remove(string service, string account)
        {
            LastService = service;
            return _secrets.Remove((service, account));
        }

        public IList<string> GetAccounts(string service)
        {
            var accounts = new List<string>();
            foreach (var key in _secrets.Keys)
            {
                if (key.service == service)
                    accounts.Add(key.account);
            }
            return accounts;
        }
    }

    private sealed class FakeCredential : ICredential
    {
        public FakeCredential(string account, string password)
        {
            Account = account;
            Password = password;
        }

        public string Account { get; }
        public string Password { get; }
    }
}
