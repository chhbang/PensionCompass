using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Util.Store;
using Windows.Security.Credentials;

namespace PensionCompass.Services;

/// <summary>
/// Adapts Google's <see cref="IDataStore"/> contract to <see cref="PasswordVault"/> so OAuth
/// access/refresh tokens persist encrypted at rest by Windows under the user's logon credentials.
/// All entries collapse into a single vault item carrying a JSON dictionary; this avoids
/// proliferating PasswordVault userNames as Google's SDK adds key suffixes per token type.
/// </summary>
public sealed class GoogleOAuthDataStore : IDataStore
{
    private const string VaultResource = "PensionCompass.GoogleDriveTokens";
    private const string VaultUserName = "default";

    public Task ClearAsync()
    {
        TryRemoveVault();
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var dict = LoadAll();
        if (dict.Remove(NamespacedKey<T>(key)))
            SaveAll(dict);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var dict = LoadAll();
        if (!dict.TryGetValue(NamespacedKey<T>(key), out var json))
            return Task.FromResult<T>(default!);
        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<T>(json) ?? default!);
        }
        catch
        {
            return Task.FromResult<T>(default!);
        }
    }

    public Task StoreAsync<T>(string key, T value)
    {
        var dict = LoadAll();
        dict[NamespacedKey<T>(key)] = JsonSerializer.Serialize(value);
        SaveAll(dict);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Google's SDK hands us the same key for different value types in some flows, so we
    /// disambiguate by prefixing with the type name. Mirrors what their built-in FileDataStore does.
    /// </summary>
    private static string NamespacedKey<T>(string key) => $"{typeof(T).FullName}-{key}";

    private static Dictionary<string, string> LoadAll()
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, VaultUserName);
            cred.RetrievePassword();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(cred.Password) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static void SaveAll(Dictionary<string, string> dict)
    {
        TryRemoveVault();
        try
        {
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential(VaultResource, VaultUserName, JsonSerializer.Serialize(dict)));
        }
        catch
        {
            // Best-effort — if the vault is unavailable the user simply has to re-auth next launch.
        }
    }

    private static void TryRemoveVault()
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, VaultUserName);
            vault.Remove(cred);
        }
        catch
        {
            // No existing entry — fine.
        }
    }
}
