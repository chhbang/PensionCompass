using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PensionCompass.Core.Models;
using PensionCompass.Core.Sync;
using Windows.Storage;

namespace PensionCompass.Services;

/// <summary>
/// Persists the user's account state and product catalog as JSON snapshots.
/// LocalState is always the canonical local copy. A pluggable <see cref="ISyncProvider"/> handles
/// the optional remote mirror (a user-chosen filesystem folder today; Google Drive in v1.1.0+).
/// Loads pick whichever side has the newer modified-time so two devices pointed at the same
/// remote stay in step without explicit "open file" actions.
/// (Settings — API key, provider, thinking level — persist via SettingsService and live in
/// PasswordVault / LocalSettings, never via this store.)
/// Catalog is serialized through a DTO because the domain record uses IReadOnlyList&lt;T&gt; and
/// Dictionary&lt;ReturnPeriod, string&gt;, neither of which round-trip cleanly through System.Text.Json defaults.
/// </summary>
public sealed class StateStore
{
    private const string AccountFileName = "account.json";
    private const string CatalogFileName = "catalog.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string _localFolderPath = ApplicationData.Current.LocalFolder.Path;
    private readonly Func<ISyncProvider> SyncSupplier;

    /// <param name="syncProviderSupplier">
    /// Resolves the current mirror target on every operation, so the caller (AppState) can swap the
    /// active provider at runtime when the user changes <see cref="SyncMode"/>. Returning
    /// <see cref="NoopSyncProvider.Instance"/> disables mirroring (LocalState only).
    /// </param>
    public StateStore(Func<ISyncProvider>? syncProviderSupplier = null)
    {
        SyncSupplier = syncProviderSupplier ?? (() => NoopSyncProvider.Instance);
    }

    private ISyncProvider Sync => SyncSupplier();

    public AccountStatusModel? LoadAccount()
        => Load<AccountStatusModel>(AccountFileName);

    public void SaveAccount(AccountStatusModel account)
        => Save(AccountFileName, account);

    public void DeleteAccount()
        => Delete(AccountFileName);

    public ProductCatalog? LoadCatalog()
    {
        var dto = Load<CatalogDto>(CatalogFileName);
        if (dto == null) return null;

        var funds = dto.Funds.Select(f => new FundProduct(
            ProductCode: f.ProductCode,
            ProductName: f.ProductName,
            AssetManager: f.AssetManager,
            RiskGrade: f.RiskGrade,
            Returns: f.Returns
                .Where(kv => Enum.TryParse<ReturnPeriod>(kv.Key, out _))
                .ToDictionary(kv => Enum.Parse<ReturnPeriod>(kv.Key), kv => kv.Value),
            AssetClass: f.AssetClass))
            .ToList();

        return new ProductCatalog(
            PrincipalGuaranteed: dto.PrincipalGuaranteed,
            Funds: funds,
            FundReturnPeriods: dto.FundReturnPeriods);
    }

    public void SaveCatalog(ProductCatalog catalog)
    {
        var dto = new CatalogDto(
            PrincipalGuaranteed: catalog.PrincipalGuaranteed.ToList(),
            Funds: catalog.Funds.Select(f => new FundProductDto(
                ProductCode: f.ProductCode,
                ProductName: f.ProductName,
                AssetManager: f.AssetManager,
                RiskGrade: f.RiskGrade,
                Returns: f.Returns.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                AssetClass: f.AssetClass)).ToList(),
            FundReturnPeriods: catalog.FundReturnPeriods.ToList());
        Save(CatalogFileName, dto);
    }

    public void DeleteCatalog()
        => Delete(CatalogFileName);

    private T? Load<T>(string fileName) where T : class
    {
        // Pick whichever of LocalState / sync provider has the newer mtime. This is what makes
        // cross-device pickup work: PC1 saves → cloud client uploads → PC2's remote copy is
        // newer than its LocalState copy → PC2's next launch loads the remote version.
        var localPath = Path.Combine(_localFolderPath, fileName);
        var localMtime = File.Exists(localPath) ? File.GetLastWriteTimeUtc(localPath) : (DateTime?)null;
        var remoteMtime = Sync.GetModifiedTime(fileName);

        byte[]? content = null;
        var preferRemote = remoteMtime.HasValue && (localMtime is null || remoteMtime > localMtime);
        if (preferRemote)
            content = Sync.Read(fileName);
        if (content is null && localMtime.HasValue)
        {
            try { content = File.ReadAllBytes(localPath); }
            catch { /* corrupted/locked — fall through to null */ }
        }

        if (content is null) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch
        {
            // Corrupted snapshot — better to start fresh than crash on launch.
            return null;
        }
    }

    private void Save<T>(string fileName, T value)
    {
        // Serialize once; write LocalState first as canonical, then mirror to remote provider.
        // Both writes are best-effort — never fail the caller's user-facing op because of disk
        // / network flake (sync folder offline, OneDrive paused, etc.).
        byte[] bytes;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        }
        catch
        {
            return;
        }

        TryWriteLocal(Path.Combine(_localFolderPath, fileName), bytes);
        if (Sync.IsConfigured)
            Sync.Write(fileName, bytes);
    }

    private void Delete(string fileName)
    {
        TryDeleteLocal(Path.Combine(_localFolderPath, fileName));
        if (Sync.IsConfigured)
            Sync.Delete(fileName);
    }

    private static void TryWriteLocal(string path, byte[] bytes)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
        }
        catch
        {
            // best-effort
        }
    }

    private static void TryDeleteLocal(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }

    private sealed record CatalogDto(
        List<PrincipalGuaranteedProduct> PrincipalGuaranteed,
        List<FundProductDto> Funds,
        List<ReturnPeriod> FundReturnPeriods);

    private sealed record FundProductDto(
        string ProductCode,
        string ProductName,
        string AssetManager,
        string RiskGrade,
        Dictionary<string, string> Returns,
        string AssetClass = "");
}
