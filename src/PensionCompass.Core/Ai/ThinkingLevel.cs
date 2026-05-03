namespace PensionCompass.Core.Ai;

/// <summary>
/// User-facing reasoning effort level. Each provider maps this onto its native knob:
/// Anthropic → <c>thinking.budget_tokens</c>, OpenAI → <c>reasoning_effort</c>,
/// Gemini → <c>generationConfig.thinkingConfig.thinkingBudget</c>. Default for the
/// rebalancing use case is <see cref="High"/> — this is a once-in-a-while, high-stakes call.
/// </summary>
public enum ThinkingLevel
{
    Off,
    Low,
    Medium,
    High,
}

public static class ThinkingLevelExtensions
{
    public static string ToKoreanLabel(this ThinkingLevel level) => level switch
    {
        ThinkingLevel.Off => "끔",
        ThinkingLevel.Low => "낮음",
        ThinkingLevel.Medium => "보통",
        ThinkingLevel.High => "최상",
        _ => level.ToString(),
    };
}
