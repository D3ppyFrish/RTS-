using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Meshing;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// Draws ground and buildings that are falling (see <see cref="PhysicsSystem"/>): each moving piece where the
/// core says, a whole piece tipping a little as it drops and fragments of a piece that broke tumbling as they
/// fly, a cloud of dust where a piece comes down (a bigger one where it breaks apart), and a splash where it
/// comes down in water. Building blocks (and rubble) in a piece keep the fine detail they are drawn with
/// standing (see <see cref="StructureDetail"/>), so a wall comes down with its battlements and a house with
/// its windows, and where such a piece lands it throws out chips of what it was made of. A felled tree
/// swings down about its stump, faster as it goes, and throws up dust and leaves where it lands. The tipping,
/// dust, chips and splashes are only for show: where every block ends up is the core's call, and the
/// rubble it leaves is drawn with the terrain.
/// </summary>
public partial class FallingRenderer : Node3D
{
    private const float Bs = ChunkMesher.BlockSize;
    /// <summary>Most a piece tips over as it falls, in radians.</summary>
    private const float MaxTip = 0.35f;
    private const float DustSeconds = 1.6f;

    private static readonly Color Dust = new(0.5f, 0.44f, 0.37f);
    private static readonly Color Spray = new(0.75f, 0.87f, 1f);

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private StandardMaterial3D _material = null!;
    private Material? _treeMaterial;
    private StandardMaterial3D _particleMaterial = null!;
    private StandardMaterial3D _chipMaterial = null!;
    private readonly Dictionary<int, Piece> _pieces = new();
    private readonly Dictionary<int, MeshInstance3D> _trees = new();

    /// <summary>A falling piece as drawn: its node, its centre (blocks), the way it tips, and the colours of its chips.</summary>
    private sealed record Piece(MeshInstance3D Node, Vector3 Centre, Vector3 Axis, Color[] Chips);
    private readonly HashSet<int> _live = new();
    private readonly List<int> _gone = new();
    private readonly List<(Node3D Node, float Left)> _effects = new();

    /// <summary>Dust clouds and splashes started so far (for the UI tests).</summary>
    public int Effects { get; private set; }

    public void Init(Simulation sim, FogOfWar fog, Material? treeMaterial = null)
    {
        _sim = sim;
        _fog = fog;
        _treeMaterial = treeMaterial;
        _material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true, // block colours are written as the terrain shows them
            Roughness = 0.95f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _chipMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.95f,
        };
        _particleMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }

    /// <summary>After each tick: dust and splashes where sections came down.</summary>
    public void AfterTick()
    {
        foreach (var l in _sim.Physics.Landings)
        {
            if (!_fog.Sees(l.X, l.Z)) continue;
            var at = new Vector3(l.X + 0.5f, l.Y + 0.5f, l.Z + 0.5f) * Bs;
            float spread = Mathf.Clamp(Mathf.Sqrt(l.Blocks) * 0.5f, 0.5f, 4f) * Bs;
            Burst(at, spread, Dust, Mathf.Clamp(l.Blocks * 6, 32, 200), 1.6f);
            if (l.Broke) Burst(at, spread * 1.5f, Dust, Mathf.Clamp(l.Blocks * 4, 32, 160), 2.4f);
            if (l.Splash) Burst(at, spread, Spray, Mathf.Clamp(l.Blocks * 4, 20, 100), 3f);
            if (l.Section == 0) // a felled tree: leaves and twigs fly
                Chips(at, spread, TreeChips, Mathf.Clamp(l.Blocks * 10, 24, 80), 2.5f);
            if (_pieces.TryGetValue(l.Section, out var piece) && piece.Chips.Length > 0)
                Chips(at, spread, piece.Chips, Mathf.Clamp(l.Blocks * (l.Broke ? 12 : 4), 16, 240), l.Broke ? 4.5f : 2.5f);
        }
    }

    private static readonly Color[] TreeChips =
    {
        ChunkMesher.PaletteColor(StructurePalette.LeafA), ChunkMesher.PaletteColor(StructurePalette.LeafC),
        ChunkMesher.PaletteColor(StructurePalette.BarkA),
    };

    /// <summary>Each frame: falling pieces where they are, between the last two ticks.</summary>
    public void UpdateFrame(float alpha, float delta)
    {
        UpdateTrees(alpha);
        _live.Clear();
        foreach (var s in _sim.Physics.Falling)
        {
            _live.Add(s.Id);
            if (!_pieces.TryGetValue(s.Id, out var piece))
            {
                piece = Build(s);
                _pieces[s.Id] = piece;
            }
            const float sub = PhysicsSystem.SubSteps;
            var moved = new Vector3(
                Mathf.Lerp(s.PrevX, s.PosX, alpha) / sub,
                Mathf.Lerp(s.PrevY, s.PosY, alpha) / sub,
                Mathf.Lerp(s.PrevZ, s.PosZ, alpha) / sub);
            // A whole piece tips a little as it drops; fragments flying apart tumble, faster the smaller they are.
            float age = _sim.Tick - s.Started + alpha;
            float tip = s.Generation == 0
                ? Mathf.Min(age * 0.04f, MaxTip)
                : age * 0.25f * s.Generation / Mathf.Sqrt(s.Blocks.Count);
            var axis = s.VelX != 0 || s.VelZ != 0 ? new Vector3(s.VelZ, 0, -s.VelX).Normalized() : piece.Axis;
            if (s.Generation == 0) axis = piece.Axis;
            var pivot = piece.Centre * Bs;
            var basis = new Basis(axis, tip);
            // Turn about the piece's centre, then move it.
            var origin = pivot - basis * pivot + moved * Bs;
            piece.Node.Transform = new Transform3D(basis, origin);
            piece.Node.Visible = _fog.Sees((int)(piece.Centre.X + moved.X), (int)(piece.Centre.Z + moved.Z));
        }
        _gone.Clear();
        foreach (var id in _pieces.Keys)
            if (!_live.Contains(id)) _gone.Add(id);
        foreach (int id in _gone)
        {
            _pieces[id].Node.QueueFree();
            _pieces.Remove(id);
        }
        for (int k = _effects.Count - 1; k >= 0; k--)
        {
            var (node, left) = _effects[k];
            left -= delta;
            if (left > 0)
            {
                _effects[k] = (node, left);
                continue;
            }
            node.QueueFree();
            _effects.RemoveAt(k);
        }
    }

    /// <summary>
    /// Felled trees swing down about the foot of their stump, away from whoever cut them, over
    /// <see cref="PhysicsSystem.TreeFallTicks"/> ticks: slowly at first, then faster, as a tree does.
    /// </summary>
    private void UpdateTrees(float alpha)
    {
        _live.Clear();
        foreach (var t in _sim.Physics.FallingTrees)
        {
            _live.Add(t.Id);
            if (!_trees.TryGetValue(t.Id, out var node))
            {
                var st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                AddDetail(st, t.Blocks, terrain: true);
                // Drawn with the terrain's own material, so it looks as it did standing.
                node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _treeMaterial ?? _material };
                AddChild(node);
                _trees[t.Id] = node;
            }
            float p = Mathf.Clamp((_sim.Tick - t.Started + alpha) / PhysicsSystem.TreeFallTicks, 0f, 1f);
            var basis = new Basis(new Vector3(t.DirZ, 0, -t.DirX), Mathf.Pi / 2 * p * p);
            // Turns about the edge of the stump's foot on the side it falls to.
            var pivot = new Vector3(t.X + 0.5f + t.DirX * 0.4f, t.BaseY, t.Z + 0.5f + t.DirZ * 0.4f) * Bs;
            node.Transform = new Transform3D(basis, pivot - basis * pivot);
            node.Visible = _fog.Sees(t.X, t.Z);
        }
        _gone.Clear();
        foreach (var id in _trees.Keys)
            if (!_live.Contains(id)) _gone.Add(id);
        foreach (int id in _gone)
        {
            _trees[id].QueueFree();
            _trees.Remove(id);
        }
    }

    /// <summary>
    /// A mesh of the section's blocks where they broke off, showing only faces not against another block of it:
    /// plain blocks for ground, fine detail for building blocks and rubble, worked out from the piece alone, so
    /// where it broke away its insides show.
    /// </summary>
    private Piece Build(FallingSection s)
    {
        var inPiece = new Dictionary<(int, int, int), BlockType>();
        var centre = Vector3.Zero;
        foreach (var b in s.Blocks)
        {
            inPiece[(b.X, b.Y, b.Z)] = b.Block;
            centre += new Vector3(b.X + 0.5f, b.Y + 0.5f, b.Z + 0.5f);
        }
        centre /= s.Blocks.Count;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var chips = AddDetail(st, s.Blocks);
        foreach (var b in s.Blocks)
        {
            if (StructureDetail.IsDetailed(b.Block)) continue;
            var colour = ChunkMesher.ColorOf(b.Block);
            for (int f = 0; f < 6; f++)
            {
                var n = Normals[f];
                if (inPiece.ContainsKey((b.X + (int)n.X, b.Y + (int)n.Y, b.Z + (int)n.Z))) continue;
                AddFace(st, new Vector3(b.X, b.Y, b.Z), f, colour);
            }
        }
        var node = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = _material };
        AddChild(node);
        // Tips over away from the side it broke from, or a way of its own for a piece with no lean.
        float a = (s.Id * 2.3999632f) % Mathf.Tau;
        var axis = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        return new Piece(node, centre, axis, chips);
    }

    // Quad face numbering (0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z) as normals.
    private static readonly Vector3[] QuadNormals =
    {
        Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward,
    };

    /// <summary>
    /// Adds the fine voxels of the piece's building blocks and rubble to <paramref name="st"/>, and returns the
    /// colours that cover most of it (for the chips it throws out when it lands), or none if it has no such blocks.
    /// </summary>
    private static Color[] AddDetail(SurfaceTool st, IReadOnlyList<SectionBlock> blocks, bool terrain = false)
    {
        const int f = StructureDetail.Size;
        var inPiece = new Dictionary<(int, int, int), BlockType>();
        foreach (var b in blocks) inPiece[(b.X, b.Y, b.Z)] = b.Block;
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var b in blocks)
        {
            if (!StructureDetail.IsDetailed(b.Block)) continue;
            x0 = Math.Min(x0, b.X); y0 = Math.Min(y0, b.Y); z0 = Math.Min(z0, b.Z);
            x1 = Math.Max(x1, b.X); y1 = Math.Max(y1, b.Y); z1 = Math.Max(z1, b.Z);
        }
        if (x1 < x0) return Array.Empty<Color>();
        int nx = (x1 - x0 + 1) * f, ny = (y1 - y0 + 1) * f, nz = (z1 - z0 + 1) * f;
        var grid = new byte[nx * ny * nz];
        var near = new BlockType[27];
        var shape = new byte[StructureDetail.Volume];
        for (int by = y0; by <= y1; by++)
        for (int bz = z0; bz <= z1; bz++)
        for (int bx = x0; bx <= x1; bx++)
        {
            if (!inPiece.TryGetValue((bx, by, bz), out var t)) continue;
            int ox = (bx - x0) * f, oy = (by - y0) * f, oz = (bz - z0) * f;
            if (StructureDetail.IsDetailed(t))
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    near[StructureDetail.NeighbourIndex(dx, dy, dz)] =
                        inPiece.TryGetValue((bx + dx, by + dy, bz + dz), out var n) ? n : BlockType.Air;
                StructureDetail.Fill(near, bx, by, bz, shape);
            }
            else
            {
                Array.Fill(shape, StructurePalette.Occluder); // ground in the piece hides the faces against it
            }
            for (int y = 0; y < f; y++)
            for (int z = 0; z < f; z++)
                Array.Copy(shape, StructureDetail.VoxelIndex(0, y, z), grid, ((oy + y) * nz + oz + z) * nx + ox, f);
        }

        var quads = new List<FineQuad>();
        StructureMesher.Mesh(grid, nx, ny, nz, x0 * f, y0 * f, z0 * f, quads);
        var area = new long[256];
        foreach (var q in quads)
        {
            var colour = ChunkMesher.PaletteColor(q.Colour);
            area[q.Colour] += (long)q.W * q.H;
            st.SetNormal(QuadNormals[q.Face]);
            if (terrain)
            {
                // For the terrain's material: shaded by side, and marked as the chunks mark them.
                colour *= ChunkMesher.Shade[q.Face];
                colour.A = ChunkMesher.FineMarkOf(q.Colour);
            }
            st.SetColor(colour);
            foreach (int k in FineOrder)
            {
                var (cx, cy, cz) = q.Corner(k);
                st.AddVertex(new Vector3(cx, cy, cz) * ChunkMesher.FineSize);
            }
        }
        var chips = new List<Color>();
        for (int n = 0; n < 3; n++)
        {
            int best = 0;
            for (int c = 1; c < area.Length; c++) if (area[c] > area[best]) best = c;
            if (area[best] == 0) break;
            chips.Add(ChunkMesher.PaletteColor((byte)best));
            area[best] = 0;
        }
        return chips.ToArray();
    }

    // Quad corners are counter-clockwise from outside; Godot treats clockwise as front-facing.
    private static readonly int[] FineOrder = { 0, 2, 1, 0, 3, 2 };

    private static readonly Vector3[] Normals =
    {
        Vector3.Up, Vector3.Down, Vector3.Right, Vector3.Left, Vector3.Back, Vector3.Forward,
    };

    // Corners of each face (unit cube), counter-clockwise seen from outside.
    private static readonly Vector3[][] Corners =
    {
        new[] { new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1) },
        new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, 0) },
        new[] { new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) },
        new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(0, 0, 1) },
        new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 0, 1) },
        new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) },
    };

    private static readonly int[] FaceOrder = { 0, 1, 2, 0, 2, 3 };

    private static void AddFace(SurfaceTool st, Vector3 at, int f, Color colour)
    {
        var c = Corners[f];
        st.SetNormal(Normals[f]);
        st.SetColor(colour);
        foreach (int k in FaceOrder)
            st.AddVertex((at + c[k]) * Bs);
    }

    /// <summary>
    /// Chips of a piece that came down: fine-voxel bits in its colours, thrown up and out, falling back and
    /// fading as they reach the ground.
    /// </summary>
    private void Chips(Vector3 at, float spread, Color[] colours, int amount, float speed)
    {
        var ramp = new Gradient();
        ramp.SetColor(0, colours[0]);
        ramp.SetColor(1, colours[^1]);
        for (int k = 1; k < colours.Length - 1; k++) ramp.AddPoint((float)k / (colours.Length - 1), colours[k]);
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = 1.1f,
            OneShot = true,
            Explosiveness = 0.95f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(spread * 0.7f, Bs * 0.4f, spread * 0.7f),
            Direction = Vector3.Up,
            Spread = 60,
            InitialVelocityMin = speed * 0.35f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -9.8f, 0),
            AngularVelocityMin = -360,
            AngularVelocityMax = 360,
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.6f,
            ColorInitialRamp = ramp,
            Mesh = new BoxMesh { Size = Vector3.One * ChunkMesher.FineSize, Material = _chipMaterial },
            Position = at,
        };
        var fade = new Gradient();
        fade.SetColor(0, new Color(1, 1, 1, 1));
        fade.SetColor(1, new Color(1, 1, 1, 0));
        fade.AddPoint(0.75f, new Color(1, 1, 1, 1));
        p.ColorRamp = fade;
        AddChild(p);
        p.Emitting = true;
        _effects.Add((p, 1.3f));
        Effects++;
    }

    /// <summary>A one-off burst of particles: dust (low, drifting) or spray (thrown up, falling back).</summary>
    private void Burst(Vector3 at, float spread, Color colour, int amount, float speed)
    {
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = DustSeconds,
            OneShot = true,
            Explosiveness = 0.9f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(spread, Bs * 0.5f, spread),
            Direction = Vector3.Up,
            Spread = 75,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, speed > 2 ? -6f : -0.6f, 0),
            DampingMin = 0.5f,
            DampingMax = 1.5f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.6f,
            Color = colour,
            Mesh = new BoxMesh { Size = Vector3.One * (Bs * 0.7f), Material = _particleMaterial },
            Position = at,
        };
        var fade = new Gradient();
        fade.SetColor(0, new Color(1, 1, 1, 1));
        fade.SetColor(1, new Color(1, 1, 1, 0));
        fade.AddPoint(0.6f, new Color(1, 1, 1, 0.8f));
        p.ColorRamp = fade;
        AddChild(p);
        p.Emitting = true;
        _effects.Add((p, DustSeconds + 0.2f));
        Effects++;
    }
}
