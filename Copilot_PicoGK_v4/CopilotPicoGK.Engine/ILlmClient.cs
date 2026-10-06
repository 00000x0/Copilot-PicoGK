namespace CopilotPicoGK;

public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// L'IA est un composant à part : le moteur ne connaît que cette interface.
/// Implémentations : OpenAiCompatClient (OpenRouter, Groq, LM Studio…) et OllamaClient (local).
/// </summary>
public interface ILlmClient
{
    /// <summary>Compteurs d'utilisation (requêtes, tokens, coût, limites annoncées par le fournisseur).</summary>
    LlmStats Stats { get; }

    Task<string> CompleteAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);
}
