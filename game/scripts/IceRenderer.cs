using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// Draws the cracks on an ice bridge in its last seconds (see <see cref="SpellSystem.Cracking"/>): dark lines
/// over the top of every ice block of a cracking bridge you can see, more of them as the thaw nears, so players
/// know to get off. The ice itself is a terrain block, drawn by the chunk meshes.
/// </summary>
public partial class IceRenderer : Node3D
{
    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_disabled;
uniform float slice_y = 100000.0;
varying float v_world_y;
void vertex() {
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    ALBEDO = vec3(0.16, 0.26, 0.36);
}
";

    // Each crack runs between two points on the block's top, in sixteenths of a block.
    private static readonly (int X1, int Z1, int X2, int Z2)[] Cracks =
    {
        (1, 3, 9, 7), (9, 7, 15, 5), (9, 7, 7, 15), (4, 0, 6, 9), (6, 9, 0, 13), (12, 0, 10, 8), (10, 8, 16, 12),
    };

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private ImmediateMesh _mesh = null!;
    private ShaderMaterial _material = null!;

    public ShaderMaterial Material => _material;

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _mesh = new ImmediateMesh();
        AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    public void UpdateFrame()
    {
        _mesh.ClearSurfaces();
        var spells = _sim.Spells;
        bool begun = false;
        float s = ChunkMesher.BlockSize, half = 0.035f * s;
        foreach (var ice in spells.IceBridges)
        {
            if (!spells.Cracking(ice)) continue;
            // From 3 cracks a block when it starts to crack to all of them at the thaw.
            long left = Math.Max(0, ice.Thaws - _sim.Tick);
            int count = 3 + (int)((Cracks.Length - 3) * (SpellSystem.CrackTicks - left) / SpellSystem.CrackTicks);
            foreach (var (x, y, z) in ice.Blocks)
            {
                if (_sim.World.Get(x, y, z) != BlockType.Ice || !_fog.Sees(x, z)) continue;
                if (!begun)
                {
                    _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
                    begun = true;
                }
                // Turn the pattern by the block's place, so neighbours do not match.
                int turn = (x * 7 + z * 13) & 3;
                float top = (y + 1) * s + 0.01f * s;
                for (int c = 0; c < count; c++)
                {
                    var (ax, az) = Turn(Cracks[c].X1, Cracks[c].Z1, turn);
                    var (bx, bz) = Turn(Cracks[c].X2, Cracks[c].Z2, turn);
                    var a = new Vector3((x + ax / 16f) * s, top, (z + az / 16f) * s);
                    var b = new Vector3((x + bx / 16f) * s, top, (z + bz / 16f) * s);
                    var d = (b - a).Normalized();
                    var side = new Vector3(-d.Z, 0, d.X) * half;
                    _mesh.SurfaceAddVertex(a - side);
                    _mesh.SurfaceAddVertex(a + side);
                    _mesh.SurfaceAddVertex(b + side);
                    _mesh.SurfaceAddVertex(a - side);
                    _mesh.SurfaceAddVertex(b + side);
                    _mesh.SurfaceAddVertex(b - side);
                }
            }
        }
        if (begun) _mesh.SurfaceEnd();
    }

    private static (int X, int Z) Turn(int x, int z, int turn) => turn switch
    {
        1 => (16 - z, x),
        2 => (16 - x, 16 - z),
        3 => (z, 16 - x),
        _ => (x, z),
    };
}
