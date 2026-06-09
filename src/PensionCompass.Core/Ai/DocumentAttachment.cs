using PensionCompass.Core.Reference;

namespace PensionCompass.Core.Ai;

/// <summary>
/// A binary document (currently always a PDF) attached to an <see cref="AiRequest"/> so the model can
/// read it alongside the prompt. The <see cref="Category"/> mirrors the prompt's "## 참고 자료" section
/// so the byte payload and its described purpose stay in sync.
/// </summary>
public sealed record DocumentAttachment(
    string FileName,
    byte[] Content,
    ReferenceCategory Category,
    string MediaType = "application/pdf");
