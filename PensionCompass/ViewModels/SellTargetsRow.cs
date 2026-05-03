using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Models;

namespace PensionCompass.ViewModels;

/// <summary>
/// Row VM for the Sell Targets grid. Wraps the same <see cref="OwnedProductModel"/> instance
/// stored in AppState.Account.OwnedItems, so toggles are visible immediately from any other screen.
/// IsSellable is the single per-row decision: false marks the product as never-sell.
/// </summary>
public sealed class SellTargetsRow : ObservableObject
{
    public OwnedProductModel Source { get; }

    public SellTargetsRow(OwnedProductModel source)
    {
        Source = source;
    }

    public string ProductName => Source.ProductName;
    public decimal CurrentValue => Source.CurrentValue;

    public bool IsSellable
    {
        get => Source.IsSellable;
        set
        {
            if (Source.IsSellable == value) return;
            Source.IsSellable = value;
            OnPropertyChanged();
        }
    }
}
