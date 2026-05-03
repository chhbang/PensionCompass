using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PensionCompass.Core.Models;
using PensionCompass.Services;

namespace PensionCompass.ViewModels;

public sealed partial class SellTargetsViewModel : ObservableObject
{
    private AccountStatusModel Account => AppState.Instance.Account;

    public ObservableCollection<SellTargetsRow> OwnedItems { get; }

    public SellTargetsViewModel()
    {
        OwnedItems = new ObservableCollection<SellTargetsRow>(
            Account.OwnedItems.Select(m => new SellTargetsRow(m)));
        foreach (var row in OwnedItems)
            row.PropertyChanged += Row_PropertyChanged;
    }

    /// <summary>
    /// 0 = 즉시 일반 리밸런싱, 1 = 만기 예약용 리밸런싱.
    /// </summary>
    public int TimingIndex
    {
        get => Account.RebalanceTiming == RebalanceTiming.Immediate ? 0 : 1;
        set
        {
            var next = value == 1 ? RebalanceTiming.MaturityReservation : RebalanceTiming.Immediate;
            if (Account.RebalanceTiming == next) return;
            Account.RebalanceTiming = next;
            // Switching to immediate clears the planned date so the UI / prompt don't carry stale data.
            if (next == RebalanceTiming.Immediate && Account.ExecutionDate.HasValue)
            {
                Account.ExecutionDate = null;
                OnPropertyChanged(nameof(ExecutionDate));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsExecutionDateRequired));
            AppState.Instance.SaveAccount();
        }
    }

    public bool IsExecutionDateRequired => Account.RebalanceTiming == RebalanceTiming.MaturityReservation;

    /// <summary>
    /// 0 = 3개월, 1 = 6개월, 2 = 1년. 기본값 6개월(중간)이 모델 기본과 일치합니다.
    /// </summary>
    public int CycleIndex
    {
        get => Account.RebalanceCycle switch
        {
            RebalanceCycle.ThreeMonths => 0,
            RebalanceCycle.SixMonths => 1,
            RebalanceCycle.OneYear => 2,
            _ => 1,
        };
        set
        {
            var next = value switch
            {
                0 => RebalanceCycle.ThreeMonths,
                2 => RebalanceCycle.OneYear,
                _ => RebalanceCycle.SixMonths,
            };
            if (Account.RebalanceCycle == next) return;
            Account.RebalanceCycle = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    /// <summary>
    /// Bound to <see cref="Microsoft.UI.Xaml.Controls.CalendarDatePicker.Date"/> which uses DateTimeOffset?.
    /// We store the underlying value as a calendar-only <see cref="DateOnly"/>.
    /// </summary>
    public DateTimeOffset? ExecutionDate
    {
        get => Account.ExecutionDate.HasValue
            ? new DateTimeOffset(Account.ExecutionDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : (DateTimeOffset?)null;
        set
        {
            DateOnly? next = value.HasValue ? DateOnly.FromDateTime(value.Value.DateTime) : null;
            if (Account.ExecutionDate == next) return;
            Account.ExecutionDate = next;
            OnPropertyChanged();
            AppState.Instance.SaveAccount();
        }
    }

    public bool HasOwnedItems => OwnedItems.Count > 0;

    public string PlannedSellSummary
    {
        get
        {
            if (OwnedItems.Count == 0)
                return "내 계좌 정보 화면에서 보유 상품을 먼저 추가해주세요.";

            var sellable = OwnedItems.Count(r => r.IsSellable);
            var locked = OwnedItems.Count - sellable;
            return $"매도 가능 {sellable}건 · 매도 금지 {locked}건";
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SellTargetsRow.IsSellable))
        {
            OnPropertyChanged(nameof(PlannedSellSummary));
            AppState.Instance.SaveAccount();
        }
    }
}
