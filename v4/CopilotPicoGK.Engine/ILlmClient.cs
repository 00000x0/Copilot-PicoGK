namespace CopilotPicoGK;

public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// L'IA est un composant à part : le moteur ne connaît que cette interface.
/// Aujourd'hui : un modèle local via Ollama. Demain : tout autre moteur local
/// (llama.cpp, LM Studio…) en ajoutant une classe qui l'implémente.
/// </summary>
public interface ILlmClient
{
    Task<string> CompleteAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default);
}
