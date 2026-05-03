namespace PensionCompass.Core.Ai;

public interface IAiClient
{
    string ProviderName { get; }
    string ModelId { get; }

    Task<string> GenerateAsync(AiRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of model IDs the user's API key has access to,
    /// already filtered to chat/generate-capable models and stripped of any provider-specific
    /// path prefix (e.g. Gemini's <c>models/</c>). Sorted alphabetically.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);
}

public sealed class AiClientException : Exception
{
    public AiClientException(string message) : base(message) { }
    public AiClientException(string message, Exception inner) : base(message, inner) { }
}
