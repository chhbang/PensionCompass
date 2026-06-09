namespace PensionCompass.Core.Ai;

public sealed record AiRequest(
    string SystemPrompt,
    string UserPrompt,
    ThinkingLevel ThinkingLevel = ThinkingLevel.High,
    IReadOnlyList<DocumentAttachment>? Attachments = null)
{
    /// <summary>Attached reference documents (PDFs), never null — empty when none.</summary>
    public IReadOnlyList<DocumentAttachment> Attachments { get; init; } = Attachments ?? [];
}
