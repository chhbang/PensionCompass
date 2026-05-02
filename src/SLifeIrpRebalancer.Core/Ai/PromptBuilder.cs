using System.Globalization;
using System.Text;
using SLifeIrpRebalancer.Core.Models;

namespace SLifeIrpRebalancer.Core.Ai;

public sealed record PromptInput(
    ProductCatalog? Catalog,
    AccountStatusModel Account,
    string UserAdditionalQuery);

public sealed record PromptOutput(string SystemPrompt, string UserPrompt);

/// <summary>
/// Builds the system+user prompt sent to the AI provider. Pure (no I/O), so it's testable
/// in isolation. The user prompt is a single markdown document with sections for timing,
/// account state, holdings (with per-row sellable flag), the matched product universe,
/// any manager-restriction note, and the user's free-text addendum.
/// </summary>
public static class PromptBuilder
{
    private const string SystemPromptText =
        "당신은 한국 퇴직연금(IRP) 포트폴리오 전문가입니다. 사용자가 제공한 보유 상품, 매수 가능한 상품 유니버스, " +
        "그리고 매도 정책과 시장 환경을 종합하여 구체적이고 실행 가능한 리밸런싱 제안을 작성하세요. " +
        "답변은 한국어 마크다운으로 거시 환경 진단 → 매도 후보 선정 → 매수 배분 → 제안 근거 요약 순으로 구조화하고, " +
        "구체적인 금액과 비중을 명시해주세요.";

    public static PromptOutput Build(PromptInput input)
    {
        var sb = new StringBuilder();
        AppendTiming(sb, input.Account);
        AppendSubscriberInfo(sb, input.Account);
        AppendManagerConstraintIfNeeded(sb, input.Account.WantsLifelongAnnuity);
        AppendAccountSummary(sb, input.Account);
        AppendHoldings(sb, input.Account.OwnedItems);
        AppendCatalog(sb, input.Catalog);
        AppendUserAddendum(sb, input.UserAdditionalQuery);
        AppendInstructions(sb);
        return new PromptOutput(SystemPromptText, sb.ToString().TrimEnd());
    }

    private static void AppendSubscriberInfo(StringBuilder sb, AccountStatusModel account)
    {
        var hasAge = account.CurrentAge.HasValue;
        var hasStartAge = account.DesiredAnnuityStartAge.HasValue;
        var hasContribution = account.MonthlyContribution is > 0m;
        var hasOtherAssets = !string.IsNullOrWhiteSpace(account.OtherRetirementAssets);
        if (!hasAge && !hasStartAge && !account.WantsLifelongAnnuity && !hasContribution && !hasOtherAssets) return;

        sb.AppendLine("## 가입자 정보 / 희망사항");
        if (hasAge)
            sb.AppendLine($"- 현재 나이: 만 {account.CurrentAge}세");
        if (hasStartAge)
        {
            if (hasAge)
            {
                var yearsToStart = account.DesiredAnnuityStartAge!.Value - account.CurrentAge!.Value;
                sb.AppendLine($"- 연금 개시 희망 연령: 만 {account.DesiredAnnuityStartAge}세 (현 시점부터 약 {yearsToStart}년 후)");
            }
            else
            {
                sb.AppendLine($"- 연금 개시 희망 연령: 만 {account.DesiredAnnuityStartAge}세");
            }
        }
        sb.AppendLine($"- 종신 지급형 연금 수령 의향: {(account.WantsLifelongAnnuity ? "예 — 아래 운용사 제약 섹션의 hard constraint를 반드시 따라야 합니다" : "아니오 (또는 미정)")}");
        sb.AppendLine($"- 추가 납입 계획: {(hasContribution ? $"매월 {Won(account.MonthlyContribution!.Value)} 적립식 자동이체 (DCA 효과 — 향후 신규 자금이 꾸준히 유입되므로 현 잔고 배분을 약간 더 성장 자산 쪽으로 기울일 여력이 있습니다)" : "없음 — 현 잔고만으로 운용합니다")}");
        if (hasOtherAssets)
        {
            sb.AppendLine("- 다른 노후 자산 / 기타 참고사항:");
            foreach (var line in account.OtherRetirementAssets.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.TrimEnd('\r').Trim();
                if (trimmed.Length == 0) continue;
                sb.AppendLine($"  - {trimmed}");
            }
        }
        sb.AppendLine("- 위 정보는 시간 지평(time horizon)과 자산 집중도를 판단하는 데 사용해주세요. 단, 사용자의 현재 보유 자산 구성이나 (있다면) 과거 위험 성향이 \"공격형\"이라는 것이 곧 공격적 추천의 근거가 되어서는 안 됩니다 — 현재 거시·시장 환경에서 공격적 진입이 위험해 보이면 사용자가 듣고 싶은 답이 아니더라도 보수적 진입 또는 분할 매수 / 관망을 명확히 권고해주세요.");
        sb.AppendLine();
    }

    private static void AppendTiming(StringBuilder sb, AccountStatusModel account)
    {
        sb.AppendLine("## 리밸런싱 시점");
        if (account.RebalanceTiming == RebalanceTiming.Immediate)
        {
            sb.AppendLine("- 최대한 즉시 일반 리밸런싱을 실행합니다. 현재 시점의 시장 상황을 반영해주세요.");
        }
        else
        {
            sb.AppendLine("- 만기 예약용 리밸런싱입니다. 즉시 실행이 아니라 아래 실행 예정일을 기준으로 제안해주세요.");
            if (account.ExecutionDate is { } date)
                sb.AppendLine($"- 실행 예정일: {date:yyyy년 M월 d일}");
            else
                sb.AppendLine("- 실행 예정일: (미지정 — 사용자가 입력하지 않았습니다)");
        }
        sb.AppendLine();
    }

    private static void AppendManagerConstraintIfNeeded(StringBuilder sb, bool restrict)
    {
        if (!restrict) return;
        sb.AppendLine("## 운용사 제약 (필수, 매우 엄격)");
        sb.AppendLine("- 사용자는 연금 개시 시 종신형 수령을 계획 중이며, 삼성생명 고객센터 안내에 따르면 종신형 지급을 받으려면 연금 개시 직전에 운용사가 **\"삼성생명보험주식회사\"** 자체인 상품들로만 운용되어야 합니다.");
        sb.AppendLine("- 따라서 신규 매수 추천은 반드시 아래 카탈로그에서 **운용사 컬럼이 정확히 \"삼성생명\" 또는 \"삼성생명보험\"** 인 상품들로만 제한해주세요. 두 표기 모두 동일 법인(삼성생명보험주식회사)을 가리키며, 카탈로그 표기가 한 쪽은 약칭, 다른 쪽은 풀네임으로 들어 있을 뿐입니다.");
        sb.AppendLine("- 위 조건을 만족하는 상품에는 **원리금보장형뿐 아니라 일부 펀드도 포함됩니다** (예: \"S selection 주식형/혼합형\", \"삼성그룹주식형\", \"인덱스주식형\", \"일반주식형\", \"채권형\" 등 — 삼성생명보험이 직접 운용하는 펀드들). 위험성향 허용 범위 내에서 이런 펀드를 매수 후보로 적극 활용해주세요. \"펀드는 모두 제외\"가 아니라 \"운용사가 삼성생명보험인 펀드만 허용\"이라는 의미입니다.");
        sb.AppendLine("- **다음은 명시적으로 제외 대상입니다:**");
        sb.AppendLine("  - 삼성자산운용㈜ 등 다른 삼성 계열사 운용 상품 (이들은 삼성생명보험과 다른 법인이라 종신형 자격이 없음)");
        sb.AppendLine("  - 메리츠, 푸본현대생명, 고려저축은행 등 타 금융사 원리금보장형 상품");
        sb.AppendLine("- 신규 매수 후보가 충분치 않으면 매도 가능 자산을 그대로 두거나 현금성 보유를 권장하는 것이 잘못된 추천보다 낫습니다.");
        sb.AppendLine();
    }

    private static void AppendAccountSummary(StringBuilder sb, AccountStatusModel account)
    {
        sb.AppendLine("## 사용자 IRP 계좌 현황");
        sb.AppendLine($"- 총 적립금: {Won(account.TotalAmount)}");
        if (account.DepositAmount.HasValue)
            sb.AppendLine($"- 입금액 (원금): {Won(account.DepositAmount.Value)}");
        if (account.ProfitAmount.HasValue)
            sb.AppendLine($"- 운용수익: {Won(account.ProfitAmount.Value)}");
        sb.AppendLine();
    }

    private static void AppendHoldings(StringBuilder sb, IReadOnlyList<OwnedProductModel> holdings)
    {
        sb.AppendLine($"## 보유 상품 ({holdings.Count}개)");
        if (holdings.Count == 0)
        {
            sb.AppendLine("(보유 상품이 입력되지 않았습니다)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| 상품명 | 적립금 | 수익률 | 연환산수익률 | 운용일수 | 좌수 | 매도 정책 |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (var h in holdings)
        {
            sb.Append("| ").Append(EscapeCell(h.ProductName));
            sb.Append(" | ").Append(Won(h.CurrentValue));
            sb.Append(" | ").Append(Percent(h.ReturnRate));
            sb.Append(" | ").Append(Percent(h.AnnualizedReturn));
            sb.Append(" | ").Append(h.InvestedDays?.ToString(CultureInfo.InvariantCulture) ?? "-");
            sb.Append(" | ").Append(h.TotalShares?.ToString("N4", CultureInfo.InvariantCulture) ?? "-");
            sb.Append(" | ").Append(h.IsSellable ? "매도 가능" : "매도 금지");
            sb.AppendLine(" |");
        }
        sb.AppendLine();
        sb.AppendLine("매도 정책 안내:");
        sb.AppendLine("- \"매도 가능\"으로 표시된 상품만 매도 후보입니다. 매도 비중과 전량/부분 여부는 당신이 시장 상황과 포트폴리오 균형을 고려해 결정해주세요.");
        sb.AppendLine("- \"매도 금지\"로 표시된 상품은 절대 매도하지 말고 그대로 보유해야 합니다.");
        sb.AppendLine();
    }

    private static void AppendCatalog(StringBuilder sb, ProductCatalog? catalog)
    {
        sb.AppendLine("## 매수 가능한 상품 유니버스");
        if (catalog is null || (catalog.PrincipalGuaranteed.Count == 0 && catalog.Funds.Count == 0))
        {
            sb.AppendLine("(상품 카탈로그가 입력되지 않았습니다 — \"상품 데이터 준비\" 화면에서 HTML 또는 CSV를 불러오세요)");
            sb.AppendLine();
            return;
        }

        AppendPrincipalGuaranteedTable(sb, catalog.PrincipalGuaranteed);
        AppendFundTable(sb, catalog.Funds);
    }

    private static void AppendPrincipalGuaranteedTable(StringBuilder sb, IReadOnlyList<PrincipalGuaranteedProduct> items)
    {
        sb.AppendLine($"### 원리금보장형 ({items.Count}개)");
        if (items.Count == 0)
        {
            sb.AppendLine("(없음)");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("| 운용사 | 상품코드 | 상품명 | 금리 | 만기 |");
        sb.AppendLine("|---|---|---|---:|---|");
        foreach (var p in items)
        {
            sb.Append("| ").Append(EscapeCell(p.AssetManager));
            sb.Append(" | ").Append(EscapeCell(p.ProductCode));
            sb.Append(" | ").Append(EscapeCell(p.ProductName));
            sb.Append(" | ").Append(EscapeCell(p.AppliedRate));
            sb.Append(" | ").Append(EscapeCell(p.MaturityTerm));
            sb.AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void AppendFundTable(StringBuilder sb, IReadOnlyList<FundProduct> funds)
    {
        sb.AppendLine($"### 펀드 ({funds.Count}개)");
        if (funds.Count == 0)
        {
            sb.AppendLine("(없음)");
            sb.AppendLine();
            return;
        }
        sb.AppendLine("| 운용사 | 상품코드 | 상품명 | 위험등급 | 1개월 | 3개월 | 6개월 | 1년 | 3년 |");
        sb.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|");
        foreach (var f in funds)
        {
            sb.Append("| ").Append(EscapeCell(f.AssetManager));
            sb.Append(" | ").Append(EscapeCell(f.ProductCode));
            sb.Append(" | ").Append(EscapeCell(f.ProductName));
            sb.Append(" | ").Append(EscapeCell(f.RiskGrade));
            sb.Append(" | ").Append(GetReturnCell(f, ReturnPeriod.Month1));
            sb.Append(" | ").Append(GetReturnCell(f, ReturnPeriod.Month3));
            sb.Append(" | ").Append(GetReturnCell(f, ReturnPeriod.Month6));
            sb.Append(" | ").Append(GetReturnCell(f, ReturnPeriod.Year1));
            sb.Append(" | ").Append(GetReturnCell(f, ReturnPeriod.Year3));
            sb.AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void AppendUserAddendum(StringBuilder sb, string userQuery)
    {
        sb.AppendLine("## 사용자 추가 요구사항");
        sb.AppendLine(string.IsNullOrWhiteSpace(userQuery) ? "(없음)" : userQuery.Trim());
        sb.AppendLine();
    }

    private static void AppendInstructions(StringBuilder sb)
    {
        sb.AppendLine("## 답변 요청");
        sb.AppendLine("위 정보를 종합하여 다음을 포함한 리밸런싱 제안을 작성해주세요:");
        sb.AppendLine("1. 현재 거시경제·시장 환경 진단 (간략히)");
        sb.AppendLine("2. 매도 가능 보유 상품 중 매도 후보와 매도 금액·비중 (구체적인 ₩ 금액)");
        sb.AppendLine("3. 매도로 확보한 현금을 어떤 매수 가능 상품에 어떻게 배분할지 (구체적인 ₩ 금액과 %)");
        sb.AppendLine("4. 위 결정의 핵심 근거 요약");
    }

    private static string Won(decimal amount)
        => $"₩{amount.ToString("N0", CultureInfo.InvariantCulture)}";

    private static string Percent(decimal? value)
        => value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%" : "-";

    private static string GetReturnCell(FundProduct fund, ReturnPeriod period)
        => fund.Returns.TryGetValue(period, out var v) ? EscapeCell(v) : "-";

    private static string EscapeCell(string s)
        => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
