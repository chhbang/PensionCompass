using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SLifeIrpRebalancer.Core.Models;
using SLifeIrpRebalancer.Services;

namespace SLifeIrpRebalancer.ViewModels;

public sealed partial class MyAccountViewModel : ObservableObject
{
    private AccountStatusModel Account => AppState.Instance.Account;

    public ObservableCollection<OwnedProductRow> OwnedItems { get; }

    public MyAccountViewModel()
    {
        OwnedItems = new ObservableCollection<OwnedProductRow>(
            Account.OwnedItems.Select(m => new OwnedProductRow(m)));
        foreach (var row in OwnedItems)
            row.PropertyChanged += Row_PropertyChanged;
        OwnedItems.CollectionChanged += OwnedItems_CollectionChanged;
    }

    public double TotalAmount
    {
        get => (double)Account.TotalAmount;
        set
        {
            var clean = double.IsNaN(value) ? 0m : (decimal)value;
            if (Account.TotalAmount == clean) return;
            Account.TotalAmount = clean;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public double DepositAmount
    {
        get => Account.DepositAmount.HasValue ? (double)Account.DepositAmount.Value : double.NaN;
        set
        {
            decimal? next = double.IsNaN(value) ? null : (decimal?)value;
            if (Account.DepositAmount == next) return;
            Account.DepositAmount = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public double ProfitAmount
    {
        get => Account.ProfitAmount.HasValue ? (double)Account.ProfitAmount.Value : double.NaN;
        set
        {
            decimal? next = double.IsNaN(value) ? null : (decimal?)value;
            if (Account.ProfitAmount == next) return;
            Account.ProfitAmount = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public double CurrentAge
    {
        get => Account.CurrentAge.HasValue ? Account.CurrentAge.Value : double.NaN;
        set
        {
            int? next = double.IsNaN(value) ? null : (int?)value;
            if (Account.CurrentAge == next) return;
            Account.CurrentAge = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public double DesiredAnnuityStartAge
    {
        get => Account.DesiredAnnuityStartAge.HasValue ? Account.DesiredAnnuityStartAge.Value : double.NaN;
        set
        {
            int? next = double.IsNaN(value) ? null : (int?)value;
            if (Account.DesiredAnnuityStartAge == next) return;
            Account.DesiredAnnuityStartAge = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public bool WantsLifelongAnnuity
    {
        get => Account.WantsLifelongAnnuity;
        set
        {
            if (Account.WantsLifelongAnnuity == value) return;
            Account.WantsLifelongAnnuity = value;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public double MonthlyContribution
    {
        get => Account.MonthlyContribution.HasValue ? (double)Account.MonthlyContribution.Value : 0d;
        set
        {
            // Treat NaN as 0 (cleared field) so the "no contribution" case is the natural default.
            decimal? next = double.IsNaN(value) || value <= 0 ? null : (decimal?)value;
            if (Account.MonthlyContribution == next) return;
            Account.MonthlyContribution = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public string OtherRetirementAssets
    {
        get => Account.OtherRetirementAssets;
        set
        {
            var clean = value ?? string.Empty;
            if (Account.OtherRetirementAssets == clean) return;
            Account.OtherRetirementAssets = clean;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    /// <summary>
    /// Product names from the imported catalog (both principal-guaranteed and funds),
    /// used as the suggestion pool for the AutoSuggestBox.
    /// </summary>
    public IReadOnlyList<string> AllProductNames
    {
        get
        {
            if (AppState.Instance.Catalog is not { } catalog)
                return [];
            return catalog.PrincipalGuaranteed
                .Select(p => p.ProductName)
                .Concat(catalog.Funds.Select(f => f.ProductName))
                .ToList();
        }
    }

    public IEnumerable<string> FilterProductNames(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return AllProductNames.Take(20);
        return AllProductNames
            .Where(name => name.Contains(query, System.StringComparison.OrdinalIgnoreCase))
            .Take(20);
    }

    public void AddOwnedProduct(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return;
        var model = new OwnedProductModel { ProductName = productName.Trim() };
        OwnedItems.Add(new OwnedProductRow(model));
    }

    public void RemoveOwnedProduct(OwnedProductRow row)
    {
        OwnedItems.Remove(row);
    }

    /// <summary>Called from the View after a confirmation dialog clears all account state.</summary>
    public void ResetAccount()
    {
        foreach (var row in OwnedItems)
            row.PropertyChanged -= Row_PropertyChanged;
        OwnedItems.CollectionChanged -= OwnedItems_CollectionChanged;

        AppState.Instance.ResetAccount();
        OwnedItems.Clear();

        OwnedItems.CollectionChanged += OwnedItems_CollectionChanged;
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(DepositAmount));
        OnPropertyChanged(nameof(ProfitAmount));
        OnPropertyChanged(nameof(CurrentAge));
        OnPropertyChanged(nameof(DesiredAnnuityStartAge));
        OnPropertyChanged(nameof(WantsLifelongAnnuity));
        OnPropertyChanged(nameof(MonthlyContribution));
        OnPropertyChanged(nameof(OtherRetirementAssets));
    }

    private void OwnedItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep the underlying domain model in lockstep with the row collection.
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null)
                    foreach (OwnedProductRow row in e.NewItems)
                    {
                        Account.OwnedItems.Add(row.Source);
                        row.PropertyChanged += Row_PropertyChanged;
                    }
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems != null)
                    foreach (OwnedProductRow row in e.OldItems)
                    {
                        Account.OwnedItems.Remove(row.Source);
                        row.PropertyChanged -= Row_PropertyChanged;
                    }
                break;
            case NotifyCollectionChangedAction.Reset:
                Account.OwnedItems.Clear();
                foreach (var row in OwnedItems)
                    Account.OwnedItems.Add(row.Source);
                break;
        }
        AppState.Instance.SaveAccount();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        AppState.Instance.SaveAccount();
    }
}
