namespace PensionCompass.Core.Reference;

/// <summary>
/// The kind of a reference PDF the user attaches for the AI to consult. Different document types
/// serve different purposes, so we surface the category to the LLM (along with usage framing) rather
/// than relying on it to infer intent from the content alone. Added 2026-06-09 when the user attached
/// both an asset-manager fund guidebook and a securities-firm monthly market report.
/// </summary>
public enum ReferenceCategory
{
    /// <summary>자산운용사 펀드/ETF 가이드 — 상품 성격·분류·장기 활용법 이해용.</summary>
    FundGuide,

    /// <summary>증권사 등 시장 분석/전망 리포트 — 거시·시장 환경 판단 보조용 (시점 의존).</summary>
    MarketReport,

    /// <summary>그 외 일반 참고 자료.</summary>
    Other,
}

public static class ReferenceCategoryExtensions
{
    /// <summary>Short Korean label for the UI (combo box, list).</summary>
    public static string ToKoreanLabel(this ReferenceCategory category) => category switch
    {
        ReferenceCategory.FundGuide => "펀드/ETF 가이드",
        ReferenceCategory.MarketReport => "시장 분석 리포트",
        ReferenceCategory.Other => "기타",
        _ => "기타",
    };

    /// <summary>
    /// One-line usage instruction handed to the LLM for documents of this category, so it knows HOW to
    /// use each attachment (and where the guardrails are — e.g. don't let a fund guide bias the AI
    /// toward that manager's products; treat a market report as time-sensitive context, not stock tips).
    /// </summary>
    public static string ToLlmFraming(this ReferenceCategory category) => category switch
    {
        ReferenceCategory.FundGuide =>
            "자산운용사의 펀드/ETF 가이드입니다. 특정 펀드·ETF의 성격·자산분류·장기 활용법을 이해하는 데만 활용하고, 발행 운용사의 상품을 편애하거나 다른 운용사를 배제하지 마세요. 실제 상품 선택은 위 카탈로그의 수익률·보수율·자산구분 수치로만 판단하세요.",
        ReferenceCategory.MarketReport =>
            "증권사 등의 시장 분석/전망 리포트입니다. 현재 거시·시장 환경과 자산군 전망을 판단하는 보조 자료로만 활용하세요. 시점에 의존하는 정보이므로 발행일을 감안하고, 개별 종목 추천이 아니라 환경 진단의 근거로만 사용하세요.",
        ReferenceCategory.Other =>
            "일반 참고 자료입니다. 본 IRP 리밸런싱 맥락에서 적절히 취사선택하여 활용하세요.",
        _ => "일반 참고 자료입니다.",
    };
}
