using System.Text;

namespace CopilotPicoGK;

/// <summary>Ce que l'IA a consommé pendant ce run, tel que rapporté par le fournisseur.</summary>
public sealed class LlmStats
{
    /// <summary>Requêtes HTTP envoyées au fournisseur (y compris celles refusées).</summary>
    public int Requests { get; set; }

    public long PromptTokens { get; private set; }
    public long CompletionTokens { get; private set; }

    /// <summary>Coût en crédits (dollars) si le fournisseur le renseigne ; 0 pour les modèles gratuits.</summary>
    public double Cost { get; private set; }

    public string? LastModel { get; set; }

    /// <summary>En-têtes « x-ratelimit-* » les plus récents, si le fournisseur en envoie (ex. Groq).</summary>
    public Dictionary<string, string> RateLimit { get; } = new();

    public void Add(long promptTokens, long completionTokens, double cost = 0)
    {
        PromptTokens += promptTokens;
        CompletionTokens += completionTokens;
        Cost += cost;
    }

    public string Summary()
    {
        var sb = new StringBuilder(
            $"Utilisation : {Requests} requête(s) | {PromptTokens:N0} tokens envoyés, {CompletionTokens:N0} reçus");
        if (Cost > 0) sb.Append($" | coût {Cost:0.000000} $");
        if (RateLimit.Count > 0)
            sb.Append(" | limites annoncées : ").Append(string.Join(", ", RateLimit.Select(kv => $"{kv.Key}={kv.Value}")));
        return sb.ToString();
    }
}
