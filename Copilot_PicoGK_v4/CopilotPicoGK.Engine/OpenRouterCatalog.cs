using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CopilotPicoGK;

/// <summary>
/// Les modèles gratuits d'OpenRouter changent d'une semaine à l'autre. Plutôt que d'en figer un
/// dans le code, on interroge le catalogue en direct et on classe les modèles gratuits.
/// </summary>
public static partial class OpenRouterCatalog
{
    // Le routeur gratuit d'OpenRouter choisit lui-même un modèle gratuit : dernier recours.
    private const string FreeRouter = "openrouter/free";
    private const int MinContext = 32_768; // il faut de la place pour règles + exemples + réponse
    private const double MinBillions = 7;

    public static async Task<IReadOnlyList<string>> PickFreeModelsAsync(
        string baseUrl, string apiKey, int count = 4, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/models");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");

        try
        {
            using var resp = await http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Catalogue OpenRouter inaccessible ({(int)resp.StatusCode}).");
            return Rank(json, count);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Impossible de joindre {baseUrl} (Internet ?) : {ex.Message}");
        }
    }

    /// <summary>Classe les modèles gratuits (texte, grand contexte) : « coder » et grande taille d'abord.</summary>
    public static IReadOnlyList<string> Rank(string catalogJson, int count)
    {
        using var doc = JsonDocument.Parse(catalogJson);
        var scored = new List<(string Id, double Score)>();

        foreach (var model in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!model.TryGetProperty("id", out var idProp) || idProp.GetString() is not { } id) continue;
            if (id.StartsWith("openrouter/", StringComparison.Ordinal)) continue; // routeurs : gérés à part
            if (!IsFree(model) || !OutputsOnlyText(model) || Excluded().IsMatch(id)) continue;

            var context = model.TryGetProperty("context_length", out var c) && c.TryGetInt64(out var n) ? n : 0;
            if (context < MinContext) continue;
            if (ParamBillions(id) is < MinBillions) continue; // taille connue et trop petite pour du code PicoGK

            scored.Add((id, Score(id, context)));
        }

        var ranked = scored.OrderByDescending(s => s.Score).Take(count).Select(s => s.Id).ToList();
        ranked.Add(FreeRouter);
        return ranked;
    }

    /// <summary>Plus grand « 123b » trouvé dans l'identifiant, ou nul si la taille n'y figure pas.</summary>
    private static double? ParamBillions(string id)
    {
        var sizes = ParamCount().Matches(id)
            .Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
        return sizes.Count > 0 ? sizes.Max() : null;
    }

    private static double Score(string id, long context)
    {
        var billions = ParamBillions(id) ?? 30; // taille inconnue : valeur moyenne
        var score = Math.Min(billions, 500);
        if (id.Contains("coder", StringComparison.OrdinalIgnoreCase)) score += 200;
        if (context >= 100_000) score += 10;
        return score;
    }

    private static bool IsFree(JsonElement model)
    {
        if (!model.TryGetProperty("pricing", out var pricing)) return false;
        return Zero(pricing, "prompt") && Zero(pricing, "completion");

        static bool Zero(JsonElement p, string name) =>
            p.TryGetProperty(name, out var v)
            && double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && d == 0;
    }

    private static bool OutputsOnlyText(JsonElement model)
    {
        if (!model.TryGetProperty("architecture", out var arch)
            || !arch.TryGetProperty("output_modalities", out var outputs)
            || outputs.ValueKind != JsonValueKind.Array) return true; // information absente : on garde
        return outputs.EnumerateArray().All(o => o.GetString() == "text");
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)b(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex ParamCount();

    [GeneratedRegex(@"vision|image|audio|embed|guard|whisper|tts|ocr|moderation|(^|[-/:])vl([-:]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Excluded();
}
