namespace SLifeIrpRebalancer.Core.Models;

public sealed class AccountStatusModel
{
    public decimal TotalAmount { get; set; }
    public decimal? DepositAmount { get; set; }
    public decimal? ProfitAmount { get; set; }
    public RebalanceTiming RebalanceTiming { get; set; } = RebalanceTiming.Immediate;

    /// <summary>
    /// The planned execution date when <see cref="RebalanceTiming"/> is
    /// <see cref="RebalanceTiming.MaturityReservation"/>. Typically the maturity date of a product
    /// that triggers the rebalance. Required only when timing is MaturityReservation; null for immediate.
    /// </summary>
    public DateOnly? ExecutionDate { get; set; }

    /// <summary>Subscriber's current age in years. Drives the AI's time-horizon and risk-budget reasoning.</summary>
    public int? CurrentAge { get; set; }

    /// <summary>Desired age at which to begin pension payouts. Korean IRP allows annuity start from age 55.</summary>
    public int? DesiredAnnuityStartAge { get; set; }

    /// <summary>
    /// Whether the subscriber plans to take the payout as a lifelong annuity (종신형). When true, the prompt
    /// adds a hard constraint that new buys must be products run by 삼성생명보험주식회사 itself
    /// (per Samsung Life call-center guidance) — see <see cref="Parsing.AssetManagerResolver.IsSamsungLifeInsurance"/>.
    /// </summary>
    public bool WantsLifelongAnnuity { get; set; }

    public List<OwnedProductModel> OwnedItems { get; set; } = [];
}
