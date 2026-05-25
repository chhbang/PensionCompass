namespace PensionCompass.Core.Parsing;

/// <summary>
/// Classifies a fund by its product name as covered-call or not. Korean retirement-pension funds
/// reliably advertise the strategy in the name (e.g. "[온라인전용]미래에셋퇴직연금배당커버드콜액티브증권자1",
/// "신한퇴직연금커버드콜인덱스증권자투자신탁"), so a simple substring match on "커버드콜" is sufficient.
/// We also accept the English form "Covered Call" / "CoveredCall" defensively in case a manager
/// localizes the prospectus name later — cost is negligible and the false-positive surface is
/// near-zero given covered-call is a specific named strategy.
///
/// Used by <see cref="Ai.PromptBuilder"/> when the user has opted to exclude covered-call funds
/// from the recommendation universe (<see cref="Models.AccountStatusModel.ExcludeCoveredCallFunds"/>).
/// </summary>
public static class CoveredCallDetector
{
    public static bool IsCoveredCallFund(string productName)
    {
        if (string.IsNullOrEmpty(productName)) return false;
        if (productName.Contains("커버드콜")) return true;
        // English form — case-insensitive, allow with or without space.
        if (productName.Contains("CoveredCall", StringComparison.OrdinalIgnoreCase)) return true;
        if (productName.Contains("Covered Call", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
