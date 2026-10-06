using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CopilotPicoGK;

/// <summary>
/// Client pour un modèle local servi par Ollama (API native /api/chat).
/// Tout reste sur la machine : aucune clé API, aucune connexion Internet requise.
/// </summary>
public sealed partial class OllamaClient(string baseUrl, string model, int contextTokens) : ILlmClient
{
    public LlmStats Stats { get; } = new();

    // Un modèle local peut être lent : on laisse jusqu'à 30 minutes par réponse.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public async Task<string> CompleteAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var all = new List<object> { new { role = "system", content = system } };
        all.AddRange(messages.Select(m => (object)new { role = m.Role, content = m.Content }));

        var body = new
        {
            model,
            stream = false,
            messages = all,
            // num_ctx : sans lui, Ollama utilise une fenêtre très courte et tronque le prompt.
            options = new { num_ctx = contextTokens, temperature = 0.2, num_predict = 4096 }
        };

        string json;
        System.Net.HttpStatusCode status;
        Stats.Requests++;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/chat")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            using var resp = await Http.SendAsync(req, ct);
            status = resp.StatusCode;
            json = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Impossible de joindre Ollama sur {baseUrl}. Lance l'application Ollama (ou « ollama serve »). Détail : {ex.Message}");
        }

        if (status == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                $"Modèle « {model} » introuvable dans Ollama. Installe-le avec : ollama pull {model}");

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"Erreur Ollama : {error.GetString()}");

        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";

        long Count(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
        Stats.Add(Count("prompt_eval_count"), Count("eval_count"));
        Stats.LastModel = model;

        // Certains modèles « raisonneurs » renvoient leur réflexion entre <think>…</think>.
        return ThinkBlock().Replace(content, "").Trim();
    }

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();
}
