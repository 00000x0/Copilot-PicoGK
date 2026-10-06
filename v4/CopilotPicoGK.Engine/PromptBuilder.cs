using System.Text;

namespace CopilotPicoGK;

public static class PromptBuilder
{
    // Marge réservée dans la fenêtre de contexte : réponse (~4000 tokens),
    // règles (~1000) et historique de correction (~2500).
    private const int ReservedTokens = 8000;
    private const int CharsPerToken = 3; // estimation prudente pour du code
    private const int MaxBudgetChars = 60_000;
    private const int MinBudgetChars = 6_000;

    /// <summary>Budget de caractères d'exemples que la fenêtre de contexte permet d'injecter.</summary>
    public static int ExamplesBudget(int contextTokens)
    {
        var budget = Math.Clamp((contextTokens - ReservedTokens) * CharsPerToken, 0, MaxBudgetChars);
        if (budget < MinBudgetChars)
            throw new ArgumentException(
                $"Fenêtre de contexte trop petite ({contextTokens} tokens) pour injecter des exemples PicoGK. " +
                "Utilise --context 12288 ou plus.");
        return budget;
    }

    public static string Build(string examplesDir, int contextTokens)
    {
        if (!Directory.Exists(examplesDir))
            throw new DirectoryNotFoundException(
                $"Dossier d'exemples introuvable : {examplesDir}. Indique-le avec --examples.");

        var budget = ExamplesBudget(contextTokens);

        var rulesPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "rules.md");
        var sb = new StringBuilder(File.ReadAllText(rulesPath));

        sb.AppendLine().AppendLine("# Exemples officiels PicoGK (référence de l'API réelle)").AppendLine();
        sb.AppendLine("Ces fichiers compilent avec la version de PicoGK installée : c'est la seule source de vérité pour l'API.").AppendLine();

        var files = Directory.EnumerateFiles(examplesDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildArtifact(p))
            .OrderBy(p => new FileInfo(p).Length); // les petits d'abord : plus d'exemples dans le budget

        var included = 0;
        var names = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            if (text.Length > budget) continue;
            budget -= text.Length;
            included++;
            names.Add($"{Path.GetFileName(file)} ({text.Length / 1000.0:0.#} ko)");
            sb.AppendLine($"## {Path.GetFileName(file)}").AppendLine("```csharp").AppendLine(text).AppendLine("```").AppendLine();
        }

        if (included == 0)
            throw new InvalidOperationException($"Aucun fichier .cs utilisable dans {examplesDir}.");

        Console.WriteLine($"Prompt : {included} fichiers d'exemples injectés (contexte {contextTokens} tokens) : {string.Join(", ", names)}");
        return sb.ToString();
    }

    private static bool IsBuildArtifact(string path)
    {
        var p = path.Replace('/', '\\');
        return p.Contains("\\obj\\") || p.Contains("\\bin\\");
    }
}
