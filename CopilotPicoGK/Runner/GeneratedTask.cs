using PicoGK;
using System.Numerics;
using System.IO;

namespace CopilotRunner;

public static class GeneratedTask
{
    // Voxel size: 0.5 mm gives good resolution for the 10 mm hole
    // (at least 20 voxels across the hole diameter) while keeping the
    // total voxel count reasonable.
    public const float VoxelSize = 0.5f;

    public static void Run()
    {
        // Assumptions / approximations:
        // - Sphere diameter = 40 mm → radius = 20 mm, centred at origin.
        // - Through‑hole diameter = 10 mm → radius = 5 mm.
        // - PicoGK examples only show voxSphere as a primitive.
        //   To approximate the cylindrical hole we use a column of
        //   overlapping spheres (radius 5 mm) stacked along the Y‑axis
        //   from Y = -25 mm to Y = +25 mm. The union of these spheres
        //   closely resembles a 10 mm‑diameter hole.
        // - Boolean operations (+, -, &) are used as shown in the
        //   BooleanShowCase example.

        // 1. Build the sphere
        Voxels sphere = Voxels.voxSphere(new Vector3(0f, 0f, 0f), 20f);   // radius 20 mm

        // 2. Approximate the hole as a column of overlapping spheres
        Voxels holeColumn = Voxels.voxSphere(new Vector3(0f, -20f, 0f), 5f);
        holeColumn += Voxels.voxSphere(new Vector3(0f, -10f, 0f), 5f);
        holeColumn += Voxels.voxSphere(new Vector3(0f,   0f, 0f), 5f);
        holeColumn += Voxels.voxSphere(new Vector3(0f,  10f, 0f), 5f);
        holeColumn += Voxels.voxSphere(new Vector3(0f,  20f, 0f), 5f);

        // 3. Subtract the hole from the sphere
        Voxels part = sphere - holeColumn;

        // 4. Show the result in the viewer (optional material)
        Library.oViewer().SetGroupMaterial(0, "777777", 0.8f, 0.2f); // light grey
        Library.oViewer().Add(part, 0);

        // 5. Export to STL
        Mesh msh = new Mesh(part);
        string outPath = Path.Combine(RunnerContext.OutputFolder, "result.stl");
        msh.SaveToStlFile(outPath);
    }
}