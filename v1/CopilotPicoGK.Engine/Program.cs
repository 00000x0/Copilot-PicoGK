using System.Text.RegularExpressions;
using CopilotPicoGK;

var options = Options.Parse(args);
if (options is null) return 1;

try
{
    var workspace = new Workspace(options.WorkspaceDir);
    workspace.EnsureRunner(options.ExamplesDir);

    var system = PromptBuilder.Build(options.ExamplesDir);
    var client = new ClaudeClient(options.ApiKey, options.Model);
    var runDir = workspace.NewRunDir();
    Console.WriteLine($"Dossier du run : {runDir}");

    var history = new List<ChatMessage> { new("user", $"Cahier des charges :\n\n{options.Spec}") };

    for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
    {
        Console.WriteLine($"\n[{attempt}/{options.MaxAttempts}] Génération du code…");
        var reply = await client.CompleteAsync(system, history);
        history.Add(new("assistant", reply));

        var code = ExtractCode(reply);
        if (code is null)
        {
            Console.WriteLine("Pas de bloc de code dans la réponse, nouvelle tentative.");
            history.Add(new("user", "Ta réponse ne contient pas de bloc ```csharp. Renvoie le fichier complet dans un seul bloc ```csharp."));
            continue;
        }

        workspace.WriteGenerated(code, runDir, attempt);

        Console.WriteLine("Compilation…");
        var build = await workspace.BuildAsync();
        if (build.ExitCode != 0)
        {
            var errors = Workspace.ExtractBuildErrors(build.Output);
            Console.WriteLine($"Échec de compilation :\n{errors}");
            history.Add(new("user", $"Erreurs de compilation :\n{errors}\n\nCorrige et renvoie le fichier complet."));
            continue;
        }

        var stl = Path.Combine(runDir, "result.stl");
        if (File.Exists(stl)) File.Delete(stl);

        Console.WriteLine("Exécution : le viewer PicoGK s'ouvre. Ferme sa fenêtre pour continuer.");
        var run = await workspace.RunAsync(runDir);

        if (File.Exists(stl))
        {
            Console.WriteLine($"\nTerminé : {stl}");
            Console.WriteLine($"Code retenu : {Path.Combine(runDir, $"attempt-{attempt}.cs")}");
            return 0;
        }

        var diagnostic = Workspace.DescribeFailedRun(runDir, run.Output);
        Console.WriteLine($"L'exécution n'a pas produit result.stl :\n{diagnostic}");
        history.Add(new("user",
            $"Le code compile mais l'exécution n'a pas produit result.stl.\n\n{diagnostic}\n" +
            "Corrige et renvoie le fichier complet."));
    }

    Console.WriteLine("\nÉchec : nombre maximal de tentatives atteint. Les codes générés sont dans le dossier du run.");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erreur : {ex.Message}");
    return 1;
}

static string? ExtractCode(string reply)
{
    var match = Regex.Match(reply, @"```(?:csharp|cs|c#)\s*\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    return match.Success ? match.Groups[1].Value.Trim() : null;
}

internal sealed record Options(
    string Spec, string ExamplesDir, string WorkspaceDir, int MaxAttempts, string Model, string ApiKey)
{
    private const string Usage = """
        Copilot PicoGK — moteur (étape 1)

        Usage :
          dotnet run -- --spec "texte du cahier des charges"
          dotnet run -- --spec-file cahier.txt

        Options :
          --examples <dossier>   Projet PicoGK_Examples (défaut : Documents\PicoGK_Examples-main)
          --workspace <dossier>  Dossier de travail (défaut : Documents\CopilotPicoGK)
          --attempts <n>         Tentatives max de génération/correction (défaut : 4)
          --model <nom>          Modèle Claude (défaut : variable COPILOT_MODEL, sinon claude-sonnet-5-5)

        Clé API : variable d'environnement ANTHROPIC_API_KEY.
        """;

    public static Options? Parse(string[] args)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string? spec = null;
        var examples = Path.Combine(documents, "PicoGK_Examples-main");
        var workspace = Path.Combine(documents, "CopilotPicoGK");
        var attempts = 4;
        var model = Environment.GetEnvironmentVariable("COPILOT_MODEL") ?? "claude-sonnet-5-5";

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Valeur manquante après {args[i]}");
            try
            {
                switch (args[i])
                {
                    case "--spec": spec = Next(); break;
                    case "--spec-file": spec = File.ReadAllText(Next()); break;
                    case "--examples": examples = Next(); break;
                    case "--workspace": workspace = Next(); break;
                    case "--attempts": attempts = int.Parse(Next()); break;
                    case "--model": model = Next(); break;
                    default: Console.Error.WriteLine($"Option inconnue : {args[i]}\n\n{Usage}"); return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.Message}\n\n{Usage}");
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(spec))
        {
            Console.Error.WriteLine(Usage);
            return null;
        }

        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("Variable d'environnement ANTHROPIC_API_KEY absente.\n\n" + Usage);
            return null;
        }

        return new Options(spec, examples, workspace, attempts, model, key);
    }
}
