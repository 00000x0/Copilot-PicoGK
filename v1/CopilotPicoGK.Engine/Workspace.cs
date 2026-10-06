using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CopilotPicoGK;

public sealed record ProcessResult(int ExitCode, string Output);

public sealed partial class Workspace(string root)
{
    public string Root { get; } = root;
    public string RunnerDir => Path.Combine(Root, "Runner");

    /// <summary>Crée (ou met à jour) le projet Runner à partir des gabarits.</summary>
    public void EnsureRunner(string examplesDir)
    {
        Directory.CreateDirectory(RunnerDir);
        var (tfm, version, detected) = DetectPicoGk(examplesDir);

        File.WriteAllText(Path.Combine(RunnerDir, "Runner.csproj"), RunnerTemplates.Csproj(tfm, version));
        File.WriteAllText(Path.Combine(RunnerDir, "Program.cs"), RunnerTemplates.ProgramCs);
        File.WriteAllText(Path.Combine(RunnerDir, "RunnerContext.cs"), RunnerTemplates.RunnerContextCs);

        Console.WriteLine(detected
            ? $"Runner : {tfm}, PicoGK {version} (repris du projet d'exemples)."
            : $"Runner : {tfm}, PicoGK {version} (valeurs par défaut : référence PicoGK non trouvée dans les exemples).");
    }

    public string NewRunDir()
    {
        var dir = Path.Combine(Root, "runs", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void WriteGenerated(string code, string runDir, int attempt)
    {
        File.WriteAllText(Path.Combine(RunnerDir, "GeneratedTask.cs"), code);
        File.WriteAllText(Path.Combine(runDir, $"attempt-{attempt}.cs"), code);
    }

    public Task<ProcessResult> BuildAsync() =>
        ProcessRunner.RunAsync("dotnet", "build Runner.csproj -c Release --nologo -v:q", RunnerDir);

    public Task<ProcessResult> RunAsync(string runDir) =>
        ProcessRunner.RunAsync("dotnet", "run --project Runner.csproj -c Release --no-build", RunnerDir,
            new Dictionary<string, string> { ["COPILOT_OUTPUT"] = runDir });

    /// <summary>Extrait les lignes d'erreur du compilateur (sans doublons).</summary>
    public static string ExtractBuildErrors(string output)
    {
        var lines = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => ErrorLine().IsMatch(l))
            .Select(l => l.Replace(@"C:\", "").Replace(@"\Runner\", ""))
            .Distinct()
            .Take(25);
        var text = string.Join("\n", lines);
        return text.Length > 0 ? text : Tail(output, 25);
    }

    /// <summary>Fin du journal PicoGK + de la sortie du processus, pour diagnostiquer un run raté.</summary>
    public static string DescribeFailedRun(string runDir, string processOutput)
    {
        var sb = new StringBuilder();
        foreach (var log in Directory.EnumerateFiles(runDir, "*.log"))
            sb.AppendLine($"--- {Path.GetFileName(log)} (fin) ---").AppendLine(Tail(File.ReadAllText(log), 40));
        sb.AppendLine("--- sortie du processus (fin) ---").AppendLine(Tail(processOutput, 40));
        return sb.ToString();
    }

    private static string Tail(string text, int lines) =>
        string.Join("\n", text.Split('\n').TakeLast(lines).Select(l => l.TrimEnd()));

    private static (string Tfm, string Version, bool Detected) DetectPicoGk(string examplesDir)
    {
        const string defaultTfm = "net9.0", defaultVersion = "2.2.0";
        if (!Directory.Exists(examplesDir)) return (defaultTfm, defaultVersion, false);

        foreach (var csproj in Directory.EnumerateFiles(examplesDir, "*.csproj", SearchOption.AllDirectories))
        {
            var xml = File.ReadAllText(csproj);
            var pkg = PicoGkPackage().Match(xml);
            if (!pkg.Success) continue;
            var tfm = TargetFramework().Match(xml);
            return (tfm.Success ? tfm.Groups[1].Value.Trim() : defaultTfm, pkg.Groups[1].Value, true);
        }
        return (defaultTfm, defaultVersion, false);
    }

    [GeneratedRegex(@"error\s+[A-Z]{2,4}\d+", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();

    [GeneratedRegex("""<PackageReference\s+Include="PicoGK"\s+Version="([^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PicoGkPackage();

    [GeneratedRegex("<TargetFramework>([^<]+)</TargetFramework>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFramework();
}

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string file, string args, string workDir, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (env is not null)
            foreach (var (key, value) in env) psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Collect(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (output) output.AppendLine(e.Data);
        }
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, output.ToString());
    }
}
