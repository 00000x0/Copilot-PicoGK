using System.Text;

namespace CopilotPicoGK;

public static class PromptBuilder
{
    // Budget de caractères pour les exemples injectés dans le prompt.
    private const int ExamplesBudget = 60_000;

    public static string Build(string examplesDir)
    {
        if (!Directory.Exists(examplesDir))
            throw new DirectoryNotFoundException(
                $"Dossier d'exemples introuvable : {examplesDir}. Indique-le avec --examples.");

        var rulesPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "rules.md");
        var sb = new StringBuilder(File.ReadAllText(rulesPath));

        sb.AppendLine().AppendLine("# Exemples officiels PicoGK (référence de l'API réelle)").AppendLine();
        sb.AppendLine("Ces fichiers compilent avec la version de PicoGK installée : c'est la seule source de vérité pour l'API.").AppendLine();

        var budget = ExamplesBudget;
        var files = Directory.EnumerateFiles(examplesDir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsBuildArtifact(p))
            .OrderBy(p => new FileInfo(p).Length); // les petits d'abord : plus d'exemples dans le budget

        var included = 0;
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            if (text.Length > budget) continue;
            budget -= text.Length;
            included++;
            sb.AppendLine($"## {Path.GetFileName(file)}").AppendLine("```csharp").AppendLine(text).AppendLine("```").AppendLine();
        }

        if (included == 0)
            throw new InvalidOperationException($"Aucun fichier .cs trouvé dans {examplesDir}.");

        Console.WriteLine($"Prompt : {included} fichiers d'exemples injectés.");
        return sb.ToString();
    }

    private static bool IsBuildArtifact(string path)
    {
        var p = path.Replace('/', '\\');
        return p.Contains("\\obj\\") || p.Contains("\\bin\\");
    }
}
