using CommunityToolkit.Mvvm.ComponentModel;
using SLifeIrpRebalancer.Core.Models;

namespace SLifeIrpRebalancer.Services;

/// <summary>
/// Process-wide state shared across the five screens — a poor-man's DI container.
/// On first access, restores the last-saved Account + Catalog from disk via <see cref="StateStore"/>;
/// catalog assignments auto-persist via the source-generated <c>OnCatalogChanged</c> hook,
/// account mutations require an explicit <see cref="SaveAccount"/> call from the VM that touched them.
/// </summary>
public sealed partial class AppState : ObservableObject
{
    public static AppState Instance { get; } = new();

    private readonly StateStore _store = new();

    [ObservableProperty]
    private ProductCatalog? _catalog;

    public AccountStatusModel Account { get; private set; } = new();

    public SettingsService Settings { get; } = new();

    private AppState()
    {
        if (_store.LoadAccount() is { } persistedAccount)
            Account = persistedAccount;

        if (_store.LoadCatalog() is { } persistedCatalog)
            _catalog = persistedCatalog; // backing field directly to avoid re-saving during load

        MigrateLifelongAnnuityFromSettings();
    }

    /// <summary>
    /// Earlier builds stored the lifelong-annuity preference in LocalSettings; it's now part of
    /// <see cref="AccountStatusModel.WantsLifelongAnnuity"/> (subscriber info, not app config).
    /// Migrate the legacy value once on the first launch after upgrade, then drop the LocalSettings entry.
    /// </summary>
    private void MigrateLifelongAnnuityFromSettings()
    {
        if (Settings.ReadLegacyLifelongAnnuityFlag() is not { } legacy) return;
        Account.WantsLifelongAnnuity = legacy;
        _store.SaveAccount(Account);
        Settings.ClearLegacyLifelongAnnuityFlag();
    }

    /// <summary>Persists current Account snapshot. Call after any field/list mutation in MyAccount or SellTargets VMs.</summary>
    public void SaveAccount() => _store.SaveAccount(Account);

    /// <summary>Wipes the persisted account snapshot and replaces the in-memory model with a fresh one.</summary>
    public void ResetAccount()
    {
        Account = new AccountStatusModel();
        _store.DeleteAccount();
    }

    /// <summary>Wipes the persisted catalog snapshot and clears the in-memory catalog.</summary>
    public void ResetCatalog()
    {
        Catalog = null; // triggers OnCatalogChanged → DeleteCatalog
    }

    partial void OnCatalogChanged(ProductCatalog? value)
    {
        if (value == null)
            _store.DeleteCatalog();
        else
            _store.SaveCatalog(value);
    }
}
