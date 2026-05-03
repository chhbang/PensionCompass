namespace PensionCompass.Core.Models;

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

    /// <summary>
    /// 사용자가 계획하는 다음 리밸런싱까지의 간격. AI가 펀드 변동성 허용치와 어느 수익률 기간을
    /// 더 비중 있게 볼지 판단하는 데 사용됩니다. 기본값은 중간값인 6개월.
    /// </summary>
    public RebalanceCycle RebalanceCycle { get; set; } = RebalanceCycle.SixMonths;

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

    /// <summary>
    /// Planned monthly DCA contribution into this IRP (₩). Null or 0 means "no ongoing contributions —
    /// rebalance the existing balance only". A positive value tells the AI it can plan with future
    /// dollar-cost-averaging in mind, e.g. tilt the current allocation slightly more toward growth assets
    /// since incoming contributions provide natural averaging.
    /// </summary>
    public decimal? MonthlyContribution { get; set; }

    /// <summary>
    /// Free-form description of the user's other retirement assets (NPS, housing, separate 연금저축 etc.).
    /// Lets the AI judge how concentrated this IRP is in the user's overall retirement picture.
    /// Empty string when the user has nothing to add.
    /// </summary>
    public string OtherRetirementAssets { get; set; } = string.Empty;

    public List<OwnedProductModel> OwnedItems { get; set; } = [];
}
