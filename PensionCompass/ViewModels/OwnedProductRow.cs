using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Models;

namespace PensionCompass.ViewModels;

/// <summary>
/// Observable wrapper around <see cref="OwnedProductModel"/> for the My Account / Sell Targets grids.
/// Bridges decimal? domain values to the double / NaN convention that NumberBox expects
/// (NumberBox.Value is double; NaN renders as empty for optional fields).
/// All setters write through to the underlying model so AppState.Account stays current.
/// </summary>
public sealed class OwnedProductRow : ObservableObject
{
    public OwnedProductModel Source { get; }

    public OwnedProductRow(OwnedProductModel source)
    {
        Source = source;
    }

    public string ProductName
    {
        get => Source.ProductName;
        set
        {
            if (Source.ProductName == value) return;
            Source.ProductName = value;
            OnPropertyChanged();
        }
    }

    public double CurrentValue
    {
        get => (double)Source.CurrentValue;
        set
        {
            var clean = double.IsNaN(value) ? 0m : (decimal)value;
            if (Source.CurrentValue == clean) return;
            Source.CurrentValue = clean;
            OnPropertyChanged();
        }
    }

    public double ReturnRate
    {
        get => Source.ReturnRate.HasValue ? (double)Source.ReturnRate.Value : double.NaN;
        set
        {
            decimal? next = double.IsNaN(value) ? null : (decimal?)value;
            if (Source.ReturnRate == next) return;
            Source.ReturnRate = next;
            OnPropertyChanged();
        }
    }

    public double AnnualizedReturn
    {
        get => Source.AnnualizedReturn.HasValue ? (double)Source.AnnualizedReturn.Value : double.NaN;
        set
        {
            decimal? next = double.IsNaN(value) ? null : (decimal?)value;
            if (Source.AnnualizedReturn == next) return;
            Source.AnnualizedReturn = next;
            OnPropertyChanged();
        }
    }

    public double InvestedDays
    {
        get => Source.InvestedDays.HasValue ? Source.InvestedDays.Value : double.NaN;
        set
        {
            int? next = double.IsNaN(value) ? null : (int?)value;
            if (Source.InvestedDays == next) return;
            Source.InvestedDays = next;
            OnPropertyChanged();
        }
    }

    public double TotalShares
    {
        get => Source.TotalShares.HasValue ? (double)Source.TotalShares.Value : double.NaN;
        set
        {
            decimal? next = double.IsNaN(value) ? null : (decimal?)value;
            if (Source.TotalShares == next) return;
            Source.TotalShares = next;
            OnPropertyChanged();
        }
    }
}
