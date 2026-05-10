using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PensionCompass.Core.Models;
using Windows.Storage;

namespace PensionCompass.Services;

/// <summary>
/// Persists the user's account state and product catalog as JSON snapshots.
/// Primary location is the app's per-user LocalFolder; if a sync folder is configured
/// (see <see cref="SettingsService.SyncFolder"/>), saves are mirrored there too and loads
/// pick whichever copy has the newer mtime — so two PCs pointed at the same cloud-backed
/// sync folder stay in step without explicit "open file" actions.
/// (Settings — API key, provider, thinking level — already persist via SettingsService;
/// API keys live in PasswordVault and are never written to disk regardless of sync state.)
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
    private readonly Func<string?> _syncFolderProvider;

    /// <param name="syncFolderProvider">
    /// Called on every save/load to look up the optional sync-folder path. Returning null
    /// or whitespace means "LocalState only". Provider is invoked late so changes to the
    /// SyncFolder setting take effect without restarting the app.
    /// </param>
    public StateStore(Func<string?>? syncFolderProvider = null)
    {
        _syncFolderProvider = syncFolderProvider ?? (() => null);
    }

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
        // Pick whichever of LocalState / sync folder has the newer mtime. This is what makes
        // cross-device pickup work: PC1 saves → cloud client uploads → PC2's sync folder copy
        // becomes newer than its LocalState copy → PC2's next launch loads the cloud version.
        var localPath = Path.Combine(_localFolderPath, fileName);
        var syncPath = GetSyncFilePath(fileName);

        var pathToLoad = ChooseNewer(localPath, syncPath);
        if (pathToLoad is null) return null;

        try
        {
            using var stream = File.OpenRead(pathToLoad);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (Exception)
        {
            // Corrupted snapshot — better to start fresh than crash on launch.
            return null;
        }
    }

    private void Save<T>(string fileName, T value)
    {
        // Always write LocalState as the canonical copy. The sync folder mirror is best-effort —
        // if the cloud client's folder is offline / locked, LocalState still progresses.
        var localPath = Path.Combine(_localFolderPath, fileName);
        TryWrite(localPath, value);

        var syncPath = GetSyncFilePath(fileName);
        if (syncPath is not null) TryWrite(syncPath, value);
    }

    private void Delete(string fileName)
    {
        TryDelete(Path.Combine(_localFolderPath, fileName));
        var syncPath = GetSyncFilePath(fileName);
        if (syncPath is not null) TryDelete(syncPath);
    }

    /// <summary>
    /// Returns the absolute path under the sync folder for this file, or null when the sync
    /// folder is not configured or doesn't exist on disk. The directory is created on demand
    /// only when actually saving — listing/loading never creates it.
    /// </summary>
    private string? GetSyncFilePath(string fileName)
    {
        var folder = _syncFolderProvider();
        if (string.IsNullOrWhiteSpace(folder)) return null;
        return Path.Combine(folder, fileName);
    }

    private static string? ChooseNewer(string pathA, string? pathB)
    {
        var aExists = File.Exists(pathA);
        var bExists = pathB != null && File.Exists(pathB);
        if (!aExists && !bExists) return null;
        if (!aExists) return pathB;
        if (!bExists) return pathA;
        return File.GetLastWriteTimeUtc(pathA) >= File.GetLastWriteTimeUtc(pathB!) ? pathA : pathB;
    }

    private static void TryWrite<T>(string path, T value)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, value, JsonOptions);
        }
        catch (Exception)
        {
            // Persistence is best-effort; never fail user-facing operations because of disk issues
            // (network drive offline, OneDrive paused, permission flake, etc.).
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
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
