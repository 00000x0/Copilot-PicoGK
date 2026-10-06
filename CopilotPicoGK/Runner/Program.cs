using PicoGK;
using CopilotRunner;

// Lance la tâche générée dans le viewer PicoGK.
// Le journal PicoGK.log est écrit dans le dossier de sortie du run.
Library.Go(GeneratedTask.VoxelSize, GeneratedTask.Run, RunnerContext.OutputFolder);