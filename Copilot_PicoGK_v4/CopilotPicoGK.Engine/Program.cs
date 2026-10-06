using System.Text;
using System.Text.RegularExpressions;
using CopilotPicoGK;

try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { /* pas de console : sans importance */ }

var options = Options.Parse(args);
if (options is null) return 1;

var workspace = new Workspace(options.WorkspaceDir);
var runDir = workspace.NewRunDir(options.RunDir);
var report = new RunReport
{
    Spec = options.Spec,
    Mode = options.BaseCode is null ? "new" : "modify",
    BaseCode = options.BaseCode,
    Backend = options.Backend,
    MaxAttempts = options.MaxAttempts
};
LlmStats? stats = null;
var exitCode = 1;

// Écrit run.json : c'est ce que lit l'application de bureau pour afficher l'avancement.
void Save(string? stage = null)
{
    if (stage is not null) report.Stage = stage;
    if (stats is not null) report.Sync(stats);
    report.Save(runDir);
}

try
{
    Console.WriteLine($"Dossier du run : {runDir}");
    Console.WriteLine(options.BaseCode is null
        ? "Mode : nouvelle pièce."
        : $"Mode : modification de la pièce existante ({options.BaseCode}).");
    Save("restauration");

    workspace.EnsureRunner(options.ExamplesDir);

    Console.WriteLine("Restauration du paquet PicoGK (cache local en priorité)…");
    var restore = await workspace.RestoreAsync();
    if (restore.ExitCode != 0)
        throw new InvalidOperationException(
            "Restauration du paquet PicoGK impossible. Lance une première fois avec Internet, " +
            "ou vérifie que PicoGK_Examples se compile.\n" + Workspace.ExtractBuildErrors(restore.Output));

    var system = PromptBuilder.Build(options.ExamplesDir, options.ContextTokens);

    ILlmClient llm;
    if (options.Backend == "ollama")
    {
        llm = new OllamaClient(options.BaseUrl, options.Model, options.ContextTokens);
        report.Models = [options.Model];
        Console.WriteLine($"Modèle : {options.Model} (local, via Ollama)");
    }
    else
    {
        // "auto" (OpenRouter seulement) : le moteur choisit dans le catalogue en direct les meilleurs
        // modèles gratuits du moment. Sinon : liste fournie, séparée par des virgules.
        IReadOnlyList<string> models = options.Model == "auto"
            ? await OpenRouterCatalog.PickFreeModelsAsync(options.BaseUrl, options.ApiKey!)
            : options.Model.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        report.Models = [.. models];
        Console.WriteLine($"Modèles ({options.BaseUrl}), par ordre d'essai : {string.Join(", ", models)}");
        llm = new OpenAiCompatClient(options.BaseUrl, options.ApiKey!, models);
    }
    stats = llm.Stats;
    Save();

    // L'historique de correction reste borné à [demande, dernière réponse, dernier retour d'erreur] :
    // prompts courts, donc moins de tokens et moins d'erreurs.
    var specMessage = new ChatMessage("user", BuildFirstMessage(options));
    var history = new List<ChatMessage> { specMessage };

    void Retry(string reply, string feedback) =>
        history = [specMessage, new("assistant", reply), new("user", feedback)];

    for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
    {
        report.Attempt = attempt;
        Save("génération");
        Console.WriteLine($"\n[{attempt}/{options.MaxAttempts}] Génération du code…");
        var reply = await llm.CompleteAsync(system, history);
        Console.WriteLine(stats.Summary());
        Save();

        var code = ExtractCode(reply);
        if (code is null)
        {
            Console.WriteLine("Pas de bloc de code dans la réponse, nouvelle tentative.");
            Retry(reply, "Ta réponse ne contient pas de bloc ```csharp. Renvoie le fichier complet dans un seul bloc ```csharp.");
            continue;
        }

        workspace.WriteGenerated(code, runDir, attempt);
        report.FinalCode = Path.Combine(runDir, $"attempt-{attempt}.cs");

        Save("compilation");
        Console.WriteLine("Compilation…");
        var build = await workspace.BuildAsync(runDir);
        if (build.ExitCode != 0)
        {
            var errors = Workspace.ExtractBuildErrors(build.Output);
            Console.WriteLine($"Échec de compilation :\n{errors}");
            Retry(reply, $"Erreurs de compilation :\n{errors}\n\nCorrige et renvoie le fichier complet.");
            continue;
        }

        var stl = Path.Combine(runDir, "result.stl");
        if (File.Exists(stl)) File.Delete(stl);
        report.Stl = stl;

        Save("exécution");
        Console.WriteLine("Exécution : le viewer PicoGK s'ouvre. Ferme sa fenêtre pour continuer.");
        var run = await workspace.RunAsync(runDir);

        if (File.Exists(stl))
        {
            Console.WriteLine($"\nTerminé : {stl}");
            Console.WriteLine($"Code retenu : {report.FinalCode}");
            report.Status = "success";
            report.Stage = "terminé";
            exitCode = 0;
            break;
        }

        var diagnostic = Workspace.DescribeFailedRun(runDir, run.Output);
        Console.WriteLine($"L'exécution n'a pas produit result.stl :\n{diagnostic}");
        report.Stl = null;
        Retry(reply,
            $"Le code compile mais l'exécution n'a pas produit result.stl.\n\n{diagnostic}\n" +
            "Corrige et renvoie le fichier complet.");
    }

    if (report.Status == "running")
    {
        report.Status = "failed";
        report.Stage = "terminé";
        exitCode = 2;
        Console.WriteLine("\nÉchec : nombre maximal de tentatives atteint. Les codes générés sont dans le dossier du run.");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"\nErreur : {ex.Message}");
    report.Status = "error";
    report.Stage = "terminé";
    report.Error = ex.Message;
}
finally
{
    report.EndedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    Save();
    if (stats is not null) Console.WriteLine($"\n{stats.Summary()}");
    if (options.Pause)
    {
        Console.WriteLine("\nAppuie sur Entrée pour fermer cette fenêtre.");
        Console.ReadLine();
    }
}

return exitCode;

static string BuildFirstMessage(Options o)
{
    if (o.BaseCode is null) return $"Cahier des charges :\n\n{o.Spec}";

    var code = File.ReadAllText(o.BaseCode);
    return "Voici le code C# actuel de la pièce (il compile et s'exécute correctement) :\n\n" +
           $"```csharp\n{code}\n```\n\n" +
           $"Modification demandée :\n\n{o.Spec}\n\n" +
           "Renvoie le fichier complet modifié. Garde tout ce qui n'est pas concerné par la modification, " +
           "et respecte le même contrat (namespace CopilotRunner, classe GeneratedTask, VoxelSize, export result.stl).";
}

static string? ExtractCode(string reply)
{
    var match = Regex.Match(reply, @"```(?:csharp|cs|c#)\s*\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    return match.Success ? match.Groups[1].Value.Trim() : null;
}

internal sealed record Options(
    string Spec, string ExamplesDir, string WorkspaceDir, int MaxAttempts,
    string Backend, string Model, string BaseUrl, int ContextTokens, string? ApiKey,
    string? BaseCode, string? RunDir, bool Pause)
{
    private const string Usage = """
        Copilot PicoGK — moteur

        Usage :
          dotnet run -- --spec "texte du cahier des charges"
          dotnet run -- --spec-file cahier.txt

        Options :
          --backend <openrouter|ollama|openai>
                                 Fournisseur d'IA (défaut : openrouter).
                                 openai = toute autre API compatible OpenAI (Groq, Mistral, LM Studio…) : --base-url et --model obligatoires.
          --model <nom>          Modèle (défaut : variable COPILOT_MODEL, sinon « auto » pour openrouter
                                 = meilleurs modèles gratuits du moment ; qwen2.5-coder:7b pour ollama).
                                 Avec openrouter/openai : une liste séparée par des virgules est essayée dans l'ordre.
          --base-url <url>       Adresse de l'API (défaut : https://openrouter.ai/api/v1, ou http://localhost:11434 pour ollama)
          --api-key-env <NOM>    Variable d'environnement qui contient la clé (défaut : OPENROUTER_API_KEY, ou COPILOT_API_KEY pour openai)
          --context <tokens>     Taille de contexte visée : règle le nombre d'exemples PicoGK injectés
                                 (défaut : 32768 pour openrouter/openai, 16384 pour ollama)
          --base-code <fichier>  Modifier une pièce existante : code C# d'un run précédent (attempt-N.cs)
          --run-dir <dossier>    Dossier de ce run (défaut : <workspace>\runs\<date-heure>)
          --pause                Attendre Entrée avant de fermer (pour garder le terminal ouvert)
          --examples <dossier>   Projet PicoGK_Examples (défaut : Documents\PicoGK_Examples-main)
          --workspace <dossier>  Dossier de travail (défaut : Documents\CopilotPicoGK)
          --attempts <n>         Tentatives max de génération/correction (défaut : 4)

        La clé API n'est jamais passée en argument : seulement par variable d'environnement.
        """;

    public static Options? Parse(string[] args)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string? spec = null, model = Environment.GetEnvironmentVariable("COPILOT_MODEL"), baseUrl = null;
        string? apiKeyEnv = null, baseCode = null, runDir = null;
        var backend = "openrouter";
        var examples = Path.Combine(documents, "PicoGK_Examples-main");
        var workspace = Path.Combine(documents, "CopilotPicoGK");
        var attempts = 4;
        var pause = false;
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
                    case "--api-key-env": apiKeyEnv = Next(); break;
                    case "--base-code": baseCode = Next(); break;
                    case "--run-dir": runDir = Next(); break;
                    case "--pause": pause = true; break;
                    default: Console.Error.WriteLine($"Option inconnue : {args[i]}\n\n{Usage}"); return null;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{ex.Message}\n\n{Usage}");
                return Fail(pause);
            }
        }

        if (backend is not ("openrouter" or "ollama" or "openai"))
            return Fail(pause, $"Backend inconnu : {backend}\n\n{Usage}");
        if (string.IsNullOrWhiteSpace(spec))
            return Fail(pause, Usage);
        if (baseCode is not null && !File.Exists(baseCode))
            return Fail(pause, $"Fichier --base-code introuvable : {baseCode}");

        var ollama = backend == "ollama";
        model ??= ollama ? "qwen2.5-coder:7b" : "auto";
        baseUrl ??= backend switch
        {
            "ollama" => "http://localhost:11434",
            "openrouter" => "https://openrouter.ai/api/v1",
            _ => null
        };
        var contextTokens = context ?? (ollama ? 16384 : 32768);

        string? apiKey = null;
        if (backend == "openai")
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return Fail(pause, "--base-url est obligatoire avec --backend openai.");
            if (model == "auto") return Fail(pause, "--model est obligatoire avec --backend openai (« auto » n'existe que pour openrouter).");
            // Les serveurs locaux (LM Studio, llama.cpp) n'ont pas besoin de clé : une valeur bidon suffit.
            apiKey = Environment.GetEnvironmentVariable(apiKeyEnv ?? "COPILOT_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey)) apiKey = "none";
        }
        else if (backend == "openrouter")
        {
            var variable = apiKeyEnv ?? "OPENROUTER_API_KEY";
            apiKey = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(apiKey))
                return Fail(pause, $"Variable d'environnement {variable} absente.\n\n{Usage}");
        }

        return new Options(spec, examples, workspace, attempts, backend, model, baseUrl!, contextTokens, apiKey,
            baseCode, runDir, pause);
    }

    private static Options? Fail(bool pause, string? message = null)
    {
        if (message is not null) Console.Error.WriteLine(message);
        if (pause)
        {
            Console.WriteLine("\nAppuie sur Entrée pour fermer cette fenêtre.");
            Console.ReadLine();
        }
        return null;
    }
}
