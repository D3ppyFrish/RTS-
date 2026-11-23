using System;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// Draws the ore your prospectors have found by sounding the ground (see <see cref="Rts.Sim.Economy.EconomySystem.ProspectedOre"/>):
/// each block of iron ore, coal, gold or crystal still there as a small glowing cube in its own colour, seen through
/// the ground (drawn over it), so you can plan mines and dig areas without digging blind. Blocks above the height
/// slice are hidden, as the ground there is. Brought up to date twice a second.
/// </summary>
public partial class OreRenderer : Node3D
{
    private Simulation _sim = null!;
    private MultiMeshInstance3D _instance = null!;
    private MultiMesh _mesh = null!;
    private float _sliceY = 100000f, _wait;
    private int _version = -1;

    /// <summary>Found blocks shown now (for the screenshot checks).</summary>
    public int Shown { get; private set; }

    public void Init(Simulation sim)
    {
        _sim = sim;
        float s = ChunkMesher.BlockSize;
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            RenderPriority = 10,
        };
        var box = new BoxMesh { Size = new Vector3(0.55f, 0.55f, 0.55f) * s, Material = material };
        _mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = box };
        _instance = new MultiMeshInstance3D { Multimesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_instance);
    }

    /// <summary>Ore above the height slice is hidden.</summary>
    public void SetSlice(float y)
    {
        _sliceY = y;
        _version = -1;
    }

    /// <summary>The colour a found block shows in.</summary>
    public static Color ColourOf(BlockType b) => b switch
    {
        BlockType.IronOre => new Color(0.95f, 0.55f, 0.35f, 0.85f),
        BlockType.Coal => new Color(0.35f, 0.35f, 0.4f, 0.9f),
        BlockType.GoldOre => new Color(1f, 0.85f, 0.2f, 0.9f),
        _ => new Color(0.6f, 0.85f, 1f, 0.85f), // crystal
    };

    public void UpdateFrame(float delta)
    {
        _wait -= delta;
        if (_wait > 0 && _version >= 0) return;
        _wait = 0.5f;
        var ore = _sim.Economy.ProspectedOre(PlayerControl.LocalPlayer);
        int version = ore.Count * 31 + _sim.Economy.Soundings(PlayerControl.LocalPlayer);
        if (version == _version) return;
        _version = version;
        float s = ChunkMesher.BlockSize;
        int n = 0;
        foreach (var (x, y, z, _) in ore)
            if (y * s <= _sliceY) n++;
        _mesh.InstanceCount = n;
        int k = 0;
        foreach (var (x, y, z, b) in ore)
        {
            if (y * s > _sliceY) continue;
            _mesh.SetInstanceTransform(k, new Transform3D(Basis.Identity, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * s));
            _mesh.SetInstanceColor(k, ColourOf(b));
            k++;
        }
        Shown = n;
    }
}
