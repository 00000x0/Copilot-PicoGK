using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CopilotPicoGK;

/// <summary>
/// Client pour toute API compatible OpenAI (/chat/completions) : OpenRouter par défaut,
/// mais aussi Groq, LM Studio, llama.cpp server… en changeant l'URL et les modèles.
/// Reçoit une liste de modèles par ordre de préférence : si l'un est indisponible
/// (retiré, sans crédit, saturé, réponse vide), le suivant est essayé automatiquement.
/// La clé API est passée par le constructeur ; elle n'est jamais écrite dans un fichier.
/// </summary>
public sealed partial class OpenAiCompatClient(string baseUrl, string apiKey, IReadOnlyList<string> models) : ILlmClient
{
    private const int MaxRetries = 3;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private int _current;
    private string? _announced;

    public LlmStats Stats { get; } = new();

    public async Task<string> CompleteAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var all = new List<object> { new { role = "system", content = system } };
        all.AddRange(messages.Select(m => (object)new { role = m.Role, content = m.Content }));

        while (_current < models.Count)
        {
            var model = models[_current];
            var (text, reason) = await TryModelAsync(model, all, ct);
            if (text is not null)
            {
                if (_announced != model)
                {
                    Console.WriteLine($"Modèle utilisé : {model}");
                    _announced = model;
                }
                return text;
            }

            Console.WriteLine($"Modèle {model} inutilisable : {reason}");
            _current++;
        }

        throw new InvalidOperationException(
            $"Aucun modèle utilisable parmi : {string.Join(", ", models)}.\n" +
            "Les modèles gratuits changent souvent. Vérifie la liste sur openrouter.ai/models, " +
            "ou utilise un modèle payant, par exemple --model qwen/qwen3-coder (il faut alors des crédits sur le compte).");
    }

    /// <summary>Essaie un modèle (avec nouvelles tentatives si saturé). Texte nul = passer au suivant.</summary>
    private async Task<(string? Text, string Reason)> TryModelAsync(string model, List<object> messages, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { model, messages, temperature = 0.2, max_tokens = 6000 });

        for (var attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Headers.Add("X-Title", "Copilot PicoGK");

            HttpStatusCode status;
            string json;
            TimeSpan? retryAfter;
            Stats.Requests++;
            try
            {
                using var resp = await Http.SendAsync(req, ct);
                status = resp.StatusCode;
                json = await resp.Content.ReadAsStringAsync(ct);
                retryAfter = resp.Headers.RetryAfter?.Delta;
                foreach (var header in resp.Headers.Where(h => h.Key.StartsWith("x-ratelimit", StringComparison.OrdinalIgnoreCase)))
                    Stats.RateLimit[header.Key.ToLowerInvariant()] = string.Join(",", header.Value);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Impossible de joindre {baseUrl} (Internet ?) : {ex.Message}");
            }

            if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
            {
                // Plafond quotidien : inutile de réessayer ou de changer de modèle gratuit.
                if (status == HttpStatusCode.TooManyRequests && DailyLimit().IsMatch(json))
                    throw new InvalidOperationException(
                        $"Quota quotidien des modèles gratuits atteint : {ErrorMessage(json)}\n" +
                        "Reviens demain, ou ajoute 10 $ de crédit une fois sur OpenRouter pour relever le plafond.");

                if (attempt <= MaxRetries)
                {
                    var wait = retryAfter ?? TimeSpan.FromSeconds(15 * attempt);
                    Console.WriteLine($"Fournisseur saturé ({(int)status}), nouvel essai dans {wait.TotalSeconds:0} s…");
                    await Task.Delay(wait, ct);
                    continue;
                }
                return (null, $"saturé ({(int)status}) après {MaxRetries} essais");
            }

            var parsed = Parse(status, json);
            if (parsed.Text is not null)
            {
                RecordUsage(json);
                Stats.LastModel = model;
            }
            return parsed;
        }
    }

    private void RecordUsage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;
            long Count(string name) => usage.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
            var cost = usage.TryGetProperty("cost", out var c) && c.TryGetDouble(out var d) ? d : 0;
            Stats.Add(Count("prompt_tokens"), Count("completion_tokens"), cost);
        }
        catch (JsonException) { /* usage absent ou illisible : sans importance */ }
    }

    private static (string? Text, string Reason) Parse(HttpStatusCode status, string json)
    {
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { /* corps non JSON : géré plus bas */ }

        using (doc)
        {
            string? errorMessage = null;
            if (doc is not null && doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var err))
            {
                errorMessage = err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m)
                    ? m.GetString() : err.ToString();
            }

            // Erreurs fatales : changer de modèle ne les résoudrait pas.
            if (status == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Clé API refusée (401). Vérifie la variable OPENROUTER_API_KEY.");
            if (errorMessage is not null && errorMessage.Contains("data policy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"OpenRouter refuse les modèles gratuits à cause de tes réglages de confidentialité : {errorMessage}\n" +
                    "Dans les paramètres de confidentialité de ton compte OpenRouter, autorise l'usage des modèles gratuits.");

            // Erreurs propres au modèle (retiré, crédits requis, paramètre refusé…) : on passe au suivant.
            if (!IsSuccess(status) || errorMessage is not null)
                return (null, $"{(int)status} {Shorten(errorMessage ?? json)}");
            if (doc is null)
                return (null, $"réponse inattendue ({(int)status})");

            string? content = null;
            if (doc.RootElement.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                && choices[0].TryGetProperty("message", out var msg)
                && msg.TryGetProperty("content", out var c))
            {
                content = c.GetString();
            }

            // Certains modèles « raisonneurs » renvoient leur réflexion entre <think>…</think>.
            content = ThinkBlock().Replace(content ?? "", "").Trim();
            return content.Length == 0
                ? (null, "réponse vide (le modèle a peut-être épuisé ses tokens à réfléchir)")
                : (content, "");
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static string ErrorMessage(string json)
    {
        var m = Regex.Match(json, "\"message\"\\s*:\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : Shorten(json);
    }

    private static string Shorten(string text)
    {
        var oneLine = Regex.Replace(text, @"\s+", " ").Trim();
        return oneLine.Length <= 200 ? oneLine : oneLine[..200] + "…";
    }

    [GeneratedRegex("per-day|per_day|daily", RegexOptions.IgnoreCase)]
    private static partial Regex DailyLimit();

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();
}
