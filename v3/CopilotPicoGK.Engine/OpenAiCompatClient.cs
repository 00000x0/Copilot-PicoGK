using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CopilotPicoGK;

/// <summary>
/// Client pour toute API compatible OpenAI (/chat/completions) : OpenRouter par défaut,
/// mais aussi Groq, LM Studio, llama.cpp server… en changeant seulement l'URL et le modèle.
/// La clé API est passée par le constructeur ; elle n'est jamais écrite dans un fichier.
/// </summary>
public sealed partial class OpenAiCompatClient(string baseUrl, string apiKey, string model) : ILlmClient
{
    private const int MaxRetries = 3;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<string> CompleteAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var all = new List<object> { new { role = "system", content = system } };
        all.AddRange(messages.Select(m => (object)new { role = m.Role, content = m.Content }));
        var body = JsonSerializer.Serialize(new { model, messages = all, temperature = 0.2, max_tokens = 6000 });

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
            try
            {
                using var resp = await Http.SendAsync(req, ct);
                status = resp.StatusCode;
                json = await resp.Content.ReadAsStringAsync(ct);
                retryAfter = resp.Headers.RetryAfter?.Delta;
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Impossible de joindre {baseUrl} (Internet ?) : {ex.Message}");
            }

            var retriable = status == HttpStatusCode.TooManyRequests || (int)status >= 500;
            if (retriable && attempt <= MaxRetries && !DailyLimit().IsMatch(json))
            {
                var wait = retryAfter ?? TimeSpan.FromSeconds(15 * attempt);
                Console.WriteLine($"Fournisseur saturé ({(int)status}), nouvel essai dans {wait.TotalSeconds:0} s…");
                await Task.Delay(wait, ct);
                continue;
            }

            return ParseOrThrow(status, json);
        }
    }

    private string ParseOrThrow(HttpStatusCode status, string json)
    {
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { /* corps non JSON : géré plus bas */ }

        using (doc)
        {
            var errorMessage = doc is not null && doc.RootElement.TryGetProperty("error", out var err)
                ? (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var m)
                    ? m.GetString() : err.ToString())
                : null;

            if (status == HttpStatusCode.Unauthorized)
                throw new InvalidOperationException("Clé API refusée (401). Vérifie la variable OPENROUTER_API_KEY.");
            if (status == HttpStatusCode.PaymentRequired)
                throw new InvalidOperationException($"Crédits insuffisants (402) : {errorMessage}");
            if (errorMessage is not null && DailyLimit().IsMatch(json))
                throw new InvalidOperationException(
                    $"Quota quotidien des modèles gratuits atteint : {errorMessage}\n" +
                    "Reviens demain, ou ajoute 10 $ de crédit une fois sur OpenRouter pour relever le plafond.");
            if (errorMessage is not null && errorMessage.Contains("data policy", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"OpenRouter refuse ce modèle gratuit à cause de tes réglages de confidentialité : {errorMessage}\n" +
                    "Dans les paramètres de confidentialité de ton compte OpenRouter, autorise l'usage des modèles gratuits.");
            if (!IsSuccess(status) || errorMessage is not null)
                throw new InvalidOperationException($"Erreur API {(int)status} : {errorMessage ?? json}");

            if (doc is null)
                throw new InvalidOperationException($"Réponse inattendue ({(int)status}) : {json}");

            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message")
                .TryGetProperty("content", out var c) ? c.GetString() : null;
            content = ThinkBlock().Replace(content ?? "", "").Trim();

            if (content.Length == 0)
                throw new InvalidOperationException(
                    "Réponse vide du modèle (souvent : il a épuisé ses tokens à réfléchir). Relance, ou change de modèle avec --model.");
            return content;
        }
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    [GeneratedRegex("per-day|per_day|daily", RegexOptions.IgnoreCase)]
    private static partial Regex DailyLimit();

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();
}
