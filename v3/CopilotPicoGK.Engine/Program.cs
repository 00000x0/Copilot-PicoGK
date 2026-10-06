using System.Text.RegularExpressions;
using CopilotPicoGK;

var options = Options.Parse(args);
if (options is null) return 1;

try
{
    var workspace = new Workspace(options.WorkspaceDir);
    workspace.EnsureRunner(options.ExamplesDir);

    Console.WriteLine("Restauration du paquet PicoGK (cache local en priorité)…");
    var restore = await workspace.RestoreAsync();
    if (restore.ExitCode != 0)
        throw new InvalidOperationException(
            "Restauration du paquet PicoGK impossible. Lance une première fois avec Internet, " +
            "ou vérifie que PicoGK_Examples se compile.\n" + Workspace.ExtractBuildErrors(restore.Output));

    var system = PromptBuilder.Build(options.ExamplesDir, options.ContextTokens);
    ILlmClient llm = options.Backend == "ollama"
        ? new OllamaClient(options.BaseUrl, options.Model, options.ContextTokens)
        : new OpenAiCompatClient(options.BaseUrl, options.ApiKey!, options.Model);
    var runDir = workspace.NewRunDir();
    Console.WriteLine($"Modèle : {options.Model} (backend {options.Backend})");
    Console.WriteLine($"Dossier du run : {runDir}");

    // L'historique de correction reste borné à [cahier des charges, dernière réponse,
    // dernier retour d'erreur] : prompts courts, donc moins de tokens et moins d'erreurs.
    var specMessage = new ChatMessage("user", $"Cahier des charges :\n\n{options.Spec}");
    var history = new List<ChatMessage> { specMessage };

    void Retry(string reply, string feedback) =>
        history = [specMessage, new("assistant", reply), new("user", feedback)];

    for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
    {
        Console.WriteLine($"\n[{attempt}/{options.MaxAttempts}] Génération du code…");
        var reply = await llm.CompleteAsync(system, history);

        var code = ExtractCode(reply);
        if (code is null)
        {
            Console.WriteLine("Pas de bloc de code dans la réponse, nouvelle tentative.");
            Retry(reply, "Ta réponse ne contient pas de bloc ```csharp. Renvoie le fichier complet dans un seul bloc ```csharp.");
            continue;
        }

        workspace.WriteGenerated(code, runDir, attempt);

        Console.WriteLine("Compilation…");
        var build = await workspace.BuildAsync();
        if (build.ExitCode != 0)
        {
            var errors = Workspace.ExtractBuildErrors(build.Output);
            Console.WriteLine($"Échec de compilation :\n{errors}");
            Retry(reply, $"Erreurs de compilation :\n{errors}\n\nCorrige et renvoie le fichier complet.");
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
        Retry(reply,
            $"Le code compile mais l'exécution n'a pas produit result.stl.\n\n{diagnostic}\n" +
            "Corrige et renvoie le fichier complet.");
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
    string Spec, string ExamplesDir, string WorkspaceDir, int MaxAttempts,
    string Backend, string Model, string BaseUrl, int ContextTokens, string? ApiKey)
{
    private const string Usage = """
        Copilot PicoGK — moteur (étape 1)

        Usage :
          dotnet run -- --spec "texte du cahier des charges"
          dotnet run -- --spec-file cahier.txt

        Options :
          --backend <openrouter|ollama>  Fournisseur d'IA (défaut : openrouter)
          --model <nom>          Modèle (défaut : variable COPILOT_MODEL, sinon
                                 qwen/qwen3-coder:free pour openrouter, qwen2.5-coder:7b pour ollama)
          --base-url <url>       Adresse de l'API (défaut : https://openrouter.ai/api/v1, ou http://localhost:11434 pour ollama)
          --context <tokens>     Taille de contexte visée : règle le nombre d'exemples PicoGK injectés
                                 (défaut : 32768 pour openrouter, 16384 pour ollama)
          --examples <dossier>   Projet PicoGK_Examples (défaut : Documents\PicoGK_Examples-main)
          --workspace <dossier>  Dossier de travail (défaut : Documents\CopilotPicoGK)
          --attempts <n>         Tentatives max de génération/correction (défaut : 4)

        Clé OpenRouter : variable d'environnement OPENROUTER_API_KEY (jamais dans un fichier).
        """;

    public static Options? Parse(string[] args)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string? spec = null, model = Environment.GetEnvironmentVariable("COPILOT_MODEL"), baseUrl = null;
        var backend = "openrouter";
        var examples = Path.Combine(documents, "PicoGK_Examples-main");
        var workspace = Path.Combine(documents, "CopilotPicoGK");
        var attempts = 4;
        int? context = null;

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
                    case "--context": context = int.Parse(Next()); break;
                    case "--backend": backend = Next().ToLowerInvariant(); break;
                    case "--base-url": baseUrl = Next(); break;
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

        if (backend is not ("openrouter" or "ollama"))
        {
            Console.Error.WriteLine($"Backend inconnu : {backend}\n\n{Usage}");
            return null;
        }
        if (string.IsNullOrWhiteSpace(spec))
        {
            Console.Error.WriteLine(Usage);
            return null;
        }

        var ollama = backend == "ollama";
        model ??= ollama ? "qwen2.5-coder:7b" : "qwen/qwen3-coder:free";
        baseUrl ??= ollama ? "http://localhost:11434" : "https://openrouter.ai/api/v1";
        var contextTokens = context ?? (ollama ? 16384 : 32768);

        string? apiKey = null;
        if (!ollama)
        {
            apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine("Variable d'environnement OPENROUTER_API_KEY absente.\n\n" + Usage);
                return null;
            }
        }

        return new Options(spec, examples, workspace, attempts, backend, model, baseUrl, contextTokens, apiKey);
    }
}
