namespace CopilotRunner;

public static class RunnerContext
{
    /// <summary>Dossier de sortie du run (STL, journal). Fourni par le moteur.</summary>
    public static string OutputFolder { get; } = Resolve();

    private static string Resolve()
    {
        var folder = Environment.GetEnvironmentVariable("COPILOT_OUTPUT")
                     ?? Path.Combine(Path.GetTempPath(), "CopilotPicoGK");
        Directory.CreateDirectory(folder);
        return folder;
    }
}