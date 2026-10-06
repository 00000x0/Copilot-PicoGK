namespace CopilotPicoGK;

/// <summary>
/// Fichiers du projet « Runner » : le petit programme qui référence PicoGK,
/// reçoit le code généré par l'IA (GeneratedTask.cs) et le lance.
/// </summary>
public static class RunnerTemplates
{
    public static string Csproj(string targetFramework, string picoGkVersion) => """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>__TFM__</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>disable</Nullable>
            <AssemblyName>Runner</AssemblyName>
            <RootNamespace>CopilotRunner</RootNamespace>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="PicoGK" Version="__VERSION__" />
          </ItemGroup>

        </Project>
        """
        .Replace("__TFM__", targetFramework)
        .Replace("__VERSION__", picoGkVersion);

    public const string ProgramCs = """
        using PicoGK;
        using CopilotRunner;

        // Lance la tâche générée dans le viewer PicoGK.
        // Le journal PicoGK.log est écrit dans le dossier de sortie du run.
        Library.Go(GeneratedTask.VoxelSize, GeneratedTask.Run, RunnerContext.OutputFolder);
        """;

    public const string RunnerContextCs = """
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
        """;
}
