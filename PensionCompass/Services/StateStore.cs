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
/// Persists the user's account state and product catalog as JSON snapshots in the
/// app's per-user LocalFolder so each launch resumes where the previous session left off.
/// (Settings — API key, provider, lifelong-annuity flag — already persist via SettingsService/LocalSettings.)
/// Catalog is serialized through a DTO because the domain record uses IReadOnlyList&lt;T&gt; and
/// Dictionary&lt;ReturnPeriod, string&gt;, neither of which round-trip cleanly through System.Text.Json defaults.
/// </summary>
public sealed class StateStore
{
    private const string AccountFileName = "account.json";
    private const string CatalogFileName = "catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    private readonly string _localFolderPath = ApplicationData.Current.LocalFolder.Path;

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
                .ToDictionary(kv => Enum.Parse<ReturnPeriod>(kv.Key), kv => kv.Value)))
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
                Returns: f.Returns.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value))).ToList(),
            FundReturnPeriods: catalog.FundReturnPeriods.ToList());
        Save(CatalogFileName, dto);
    }

    public void DeleteCatalog()
        => Delete(CatalogFileName);

    private T? Load<T>(string fileName) where T : class
    {
        var path = Path.Combine(_localFolderPath, fileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
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
        var path = Path.Combine(_localFolderPath, fileName);
        try
        {
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, value, JsonOptions);
        }
        catch (Exception)
        {
            // Persistence is best-effort; never fail user-facing operations because of disk issues.
        }
    }

    private void Delete(string fileName)
    {
        var path = Path.Combine(_localFolderPath, fileName);
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
        Dictionary<string, string> Returns);
}
