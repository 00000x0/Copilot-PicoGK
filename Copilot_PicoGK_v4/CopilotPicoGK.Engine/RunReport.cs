using System.Text.Encodings.Web;
using System.Text.Json;

namespace CopilotPicoGK;

/// <summary>
/// État du run, écrit dans run.json à chaque étape. L'application de bureau le lit
/// pour afficher l'avancement, les tokens consommés et retrouver le code et le STL.
/// </summary>
public sealed class RunReport
{
    /// <summary>running | success | failed | error</summary>
    public string Status { get; set; } = "running";

    /// <summary>init | restauration | génération | compilation | exécution | terminé</summary>
    public string Stage { get; set; } = "init";

    public string Spec { get; set; } = "";

    /// <summary>new | modify</summary>
    public string Mode { get; set; } = "new";

    public string? BaseCode { get; set; }
    public string Backend { get; set; } = "";
    public List<string> Models { get; set; } = new();
    public string? ModelUsed { get; set; }
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; }
    public string? Stl { get; set; }
    public string? FinalCode { get; set; }
    public int Requests { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public double Cost { get; set; }
    public Dictionary<string, string> RateLimit { get; set; } = new();
    public string StartedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    public string? EndedAt { get; set; }
    public string? Error { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Sync(LlmStats stats)
    {
        Requests = stats.Requests;
        PromptTokens = stats.PromptTokens;
        CompletionTokens = stats.CompletionTokens;
        Cost = stats.Cost;
        RateLimit = new Dictionary<string, string>(stats.RateLimit);
        if (stats.LastModel is not null) ModelUsed = stats.LastModel;
    }

    /// <summary>Écriture atomique : l'application ne lit jamais un fichier à moitié écrit.</summary>
    public void Save(string runDir)
    {
        try
        {
            Directory.CreateDirectory(runDir);
            var final = Path.Combine(runDir, "run.json");
            var tmp = final + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, final, overwrite: true);
        }
        catch (IOException) { /* fichier momentanément verrouillé : le prochain Save réessaiera */ }
    }
}
