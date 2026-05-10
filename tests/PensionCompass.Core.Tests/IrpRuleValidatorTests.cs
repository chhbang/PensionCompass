using PensionCompass.Core.Validation;

namespace PensionCompass.Core.Tests;

public class IrpRuleValidatorTests
{
    [Fact]
    public void Validate_EmptyResponse_ReturnsUnableToVerify()
    {
        var result = IrpRuleValidator.Validate("");

        Assert.Equal(IrpValidationStatus.UnableToVerify, result.Status);
        Assert.Null(result.RiskAssetPercent);
        Assert.Null(result.SafeAssetPercent);
    }

    [Fact]
    public void Validate_WhenBothRatiosWithinLimits_ReturnsCompliant()
    {
        var markdown = """
            ## 추천 포트폴리오

            매수 후보 배분이 끝나면 합계는 다음과 같습니다:

            - 위험자산 합계: ₩67,000,000 (총 적립금 대비 65.0%)
            - 안정자산 합계: ₩36,000,000 (총 적립금 대비 35.0%)
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(IrpValidationStatus.Compliant, result.Status);
        Assert.Equal(65.0m, result.RiskAssetPercent);
        Assert.Equal(35.0m, result.SafeAssetPercent);
    }

    [Fact]
    public void Validate_WhenRiskAssetExceeds70_FlagsViolation()
    {
        var markdown = """
            - 위험자산 합계: 75.0%
            - 안정자산 합계: 25.0%
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(IrpValidationStatus.Violation, result.Status);
        Assert.Equal(75.0m, result.RiskAssetPercent);
        Assert.Equal(25.0m, result.SafeAssetPercent);
        Assert.Contains("위험자산", result.Message);
        Assert.Contains("안정자산", result.Message);
    }

    [Fact]
    public void Validate_WhenSafeAssetUnder30_FlagsViolation()
    {
        var markdown = """
            제안 포트폴리오 합계:
            - 위험자산 비중: 72.0% (IRP 한도 초과)
            - 안정자산 비중: 28.0%
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(IrpValidationStatus.Violation, result.Status);
    }

    [Fact]
    public void Validate_TakesLastMatchingLine_BiasingTowardRecommendationSummary()
    {
        // The AI describes current state first (60/40), then the proposed portfolio (65/35).
        // The validator should report the proposed numbers, not the current state.
        var markdown = """
            ## 현재 상태
            현재 위험자산 비중은 60% 입니다.
            현재 안정자산 비중은 40% 입니다.

            ## 제안
            추천 포트폴리오 합계:
            - 위험자산 합계: 65.0%
            - 안정자산 합계: 35.0%
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(65.0m, result.RiskAssetPercent);
        Assert.Equal(35.0m, result.SafeAssetPercent);
    }

    [Fact]
    public void Validate_LinesContainingBothKeywords_AreSkippedAsAmbiguous()
    {
        // A prose line that mentions both 위험자산 and 안정자산 should NOT be parsed.
        // Only the explicit summary lines below should drive the result.
        var markdown = """
            위험자산을 줄이고 안정자산을 늘리되, 비중은 약 50% 50% 정도로 맞추는 것이 좋겠습니다.

            ## 결과
            - 위험자산: 55.0%
            - 안정자산: 45.0%
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(55.0m, result.RiskAssetPercent);
        Assert.Equal(45.0m, result.SafeAssetPercent);
    }

    [Fact]
    public void Validate_WhenNoRatiosFound_ReturnsUnableToVerify()
    {
        var markdown = """
            추천 포트폴리오는 다음과 같습니다:
            1. 펀드 A 매수
            2. 펀드 B 매수
            (비중 정보 없음 — AI가 형식을 어김)
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(IrpValidationStatus.UnableToVerify, result.Status);
    }

    [Fact]
    public void Validate_WhenOnlyOneRatioFound_ReturnsUnableToVerifyButReportsIt()
    {
        var markdown = "- 위험자산 합계: 60.0%";

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(IrpValidationStatus.UnableToVerify, result.Status);
        Assert.Equal(60.0m, result.RiskAssetPercent);
        Assert.Null(result.SafeAssetPercent);
        Assert.Contains("위험자산 비중 60.0%만 확인", result.Message);
    }

    [Fact]
    public void Validate_AtBoundary_70PercentRiskIsCompliant()
    {
        // 70% is the maximum allowed (≤), not strictly less than.
        var markdown = """
            - 위험자산 합계: 70.0%
            - 안정자산 합계: 30.0%
            """;

        var result = IrpRuleValidator.Validate(markdown);

        Assert.Equal(IrpValidationStatus.Compliant, result.Status);
    }
}
