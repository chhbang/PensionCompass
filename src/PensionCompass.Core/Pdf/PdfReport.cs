using PensionCompass.Core.Models;

namespace PensionCompass.Core.Pdf;

public sealed record PdfReport(
    DateTime GeneratedAt,
    string ProviderName,
    string ModelId,
    AccountStatusModel Account,
    string AiResponseMarkdown);
