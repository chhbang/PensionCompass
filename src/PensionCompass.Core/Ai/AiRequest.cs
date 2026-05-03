namespace PensionCompass.Core.Ai;

public sealed record AiRequest(
    string SystemPrompt,
    string UserPrompt,
    ThinkingLevel ThinkingLevel = ThinkingLevel.High);
