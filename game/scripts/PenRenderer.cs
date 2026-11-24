using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;

namespace Rts.Game;

/// <summary>
/// Draws the low stake fence round each beast pen's yard (see <see cref="BuildingKind.Pen"/>; a look only: the yard is
/// walkable ground in the simulation): a pointed stake at every column of its edge and two rails along each side, in a
/// palisade's wood. Only pens on ground the player has seen, and only finished ones (a site is its trodden yard alone).
/// </summary>
public partial class PenRenderer : Node3D
{
    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private MultiMeshInstance3D _stakes = null!, _rails = null!;
    private float _sliceY = 100000f;
    private string _key = "";
    private readonly List<Transform3D> _stakeAt = new(), _railAt = new();

    private static readonly Color Wood = new(0.45f, 0.31f, 0.18f);

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        var mat = new StandardMaterial3D { AlbedoColor = Wood, Roughness = 0.95f };
        float s = ChunkMesher.BlockSize;
        _stakes = Make(new BoxMesh { Size = new Vector3(0.22f, 1.15f, 0.22f) * s, Material = mat });
        _rails = Make(new BoxMesh { Size = new Vector3(1f, 0.1f, 0.08f) * s, Material = mat });
    }

    private MultiMeshInstance3D Make(Mesh mesh)
    {
        var m = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(m);
        return m;
    }

    /// <summary>Fences above the height slice are hidden, as the ground there is.</summary>
    public void SetSlice(float y)
    {
        _sliceY = y;
        _key = "";
    }

    public void UpdateFrame()
    {
        // Rebuilt only when the pens shown change.
        var shown = new List<Building>();
        var key = new System.Text.StringBuilder();
        float s = ChunkMesher.BlockSize;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.Def.Kind != BuildingKind.Pen || !b.Active || b.Y * s > _sliceY) continue;
            if (!_fog.HasSeen(b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2)) continue;
            shown.Add(b);
            key.Append(b.Id).Append(',');
        }
        if (key.ToString() == _key) return;
        _key = key.ToString();
        _stakeAt.Clear();
        _railAt.Clear();
        foreach (var b in shown) Fence(b, s);
        Fill(_stakes.Multimesh, _stakeAt);
        Fill(_rails.Multimesh, _railAt);
    }

    private void Fence(Building b, float s)
    {
        int w = b.Def.SizeX, d = b.Def.SizeZ;
        float x0 = b.X * s, z0 = b.Z * s, y = b.Y * s;
        float inset = 0.15f * s;
        // A stake at every column of the edge, a little in from it.
        for (int k = 0; k <= w; k++)
        {
            float x = x0 + Mathf.Clamp(k * s, inset, w * s - inset);
            Stake(new Vector3(x, y, z0 + inset));
            Stake(new Vector3(x, y, z0 + d * s - inset));
        }
        for (int k = 1; k < d; k++)
        {
            float z = z0 + k * s;
            Stake(new Vector3(x0 + inset, y, z));
            Stake(new Vector3(x0 + w * s - inset, y, z));
        }
        // Two rails along each side.
        foreach (float h in new[] { 0.35f * s, 0.8f * s })
        {
            float lenX = w * s - 2 * inset, lenZ = d * s - 2 * inset;
            Rail(new Vector3(x0 + w * s / 2, y + h, z0 + inset), lenX / s, 0f);
            Rail(new Vector3(x0 + w * s / 2, y + h, z0 + d * s - inset), lenX / s, 0f);
            Rail(new Vector3(x0 + inset, y + h, z0 + d * s / 2), lenZ / s, Mathf.Pi / 2);
            Rail(new Vector3(x0 + w * s - inset, y + h, z0 + d * s / 2), lenZ / s, Mathf.Pi / 2);
        }
    }

    private void Stake(Vector3 foot)
    {
        float s = ChunkMesher.BlockSize;
        // Leaning a touch, each its own way, so the fence looks put up by hand.
        float lean = ((int)(foot.X * 7 + foot.Z * 13) % 5 - 2) * 0.03f;
        var basis = new Basis(Vector3.Forward, lean);
        _stakeAt.Add(new Transform3D(basis, foot + new Vector3(0, 0.575f * s, 0)));
    }

    private void Rail(Vector3 at, float length, float turn)
    {
        var basis = new Basis(Vector3.Up, turn) * Basis.FromScale(new Vector3(length, 1, 1));
        _railAt.Add(new Transform3D(basis, at));
    }

    private static void Fill(MultiMesh mm, List<Transform3D> at)
    {
        mm.InstanceCount = at.Count;
        for (int k = 0; k < at.Count; k++) mm.SetInstanceTransform(k, at[k]);
    }
}
