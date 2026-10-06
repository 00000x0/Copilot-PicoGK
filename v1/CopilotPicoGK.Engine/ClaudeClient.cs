using System.Text;
using System.Text.Json;

namespace CopilotPicoGK;

public sealed record ChatMessage(string Role, string Content);

/// <summary>Client minimal pour l'API Messages d'Anthropic.</summary>
public sealed class ClaudeClient(string apiKey, string model)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<string> CompleteAsync(
        string system,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken ct = default)
    {
        var body = new
        {
            model,
            max_tokens = 8000,
            // Le prompt système (règles + exemples) est long et identique d'une tentative
            // à l'autre : on le met en cache pour réduire coût et latence.
            system = new[]
            {
                new { type = "text", text = system, cache_control = new { type = "ephemeral" } }
            },
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Erreur API Claude {(int)resp.StatusCode} : {json}");

        using var doc = JsonDocument.Parse(json);
        var text = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
                text.Append(block.GetProperty("text").GetString());
        }
        return text.ToString();
    }
}
