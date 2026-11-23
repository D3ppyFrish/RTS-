using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;
using Rts.Sim.Meshing;

namespace Rts.Game;

/// <summary>
/// See-through models of buildings to come (issue #92): the finished building over each of your plans and
/// building sites until it is complete, and over the spot the build tool would place one. The colour says
/// what it is: violet for a plan you cannot pay for yet, cyan for a plan you can start now (right click it),
/// amber for a site being built (only its blocks still to come), red where the build tool cannot place it,
/// pale grey over a ruin your rebuild orders will put back. Other players' plans, sites and ruins get none.
/// </summary>
public partial class GhostRenderer : Node3D
{
    // Not green: ghosts mostly stand on grass.
    public static readonly Color PlannedColor = new(0.68f, 0.45f, 1f);
    public static readonly Color ReadyColor = new(0.2f, 0.92f, 1f);
    public static readonly Color SiteColor = new(1f, 0.72f, 0.22f);
    public static readonly Color BlockedColor = new(1f, 0.3f, 0.25f);
    /// <summary>A destroyed building rebuild orders will put back (see <see cref="Rts.Sim.Automation.AutomationSystem.Ruins"/>).</summary>
    public static readonly Color RuinColor = new(0.85f, 0.85f, 0.8f);

    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_never, blend_mix;
// Drawn after DepthCode has marked the nearest surfaces, so only the outside shows, evenly see-through.
uniform float slice_y = 100000.0;
instance uniform vec4 tint : source_color = vec4(1.0);
instance uniform float pulse = 0.0;
varying float v_world_y;
void vertex() {
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    // Edges on to the camera show stronger.
    float rim = 1.0 - abs(dot(NORMAL, VIEW));
    // Tinted by brightness, so the sides and slopes keep their shading and the shape reads.
    float lum = dot(COLOR.rgb, vec3(0.3, 0.55, 0.15));
    vec3 c = mix(COLOR.rgb, tint.rgb * (0.4 + 0.9 * lum), 0.8);
    float glow = pulse * (0.5 + 0.5 * sin(TIME * 4.0));
    ALBEDO = c + tint.rgb * 0.25 * glow;
    ALPHA = clamp(0.45 + 0.3 * rim + 0.15 * glow, 0.0, 0.9);
}
";

    /// <summary>The first pass: depth only, so the second draws each pixel's nearest face once.</summary>
    private const string DepthCode = @"
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_always, blend_mix;
uniform float slice_y = 100000.0;
varying float v_world_y;
void vertex() {
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    ALBEDO = vec3(0.0);
    ALPHA = 0.0;
}
";

    private Simulation _sim = null!;
    private ShaderMaterial _material = null!;
    private ShaderMaterial _depth = null!;
    private readonly Dictionary<int, MeshInstance3D> _shown = new();
    private readonly HashSet<int> _seen = new();
    /// <summary>Meshes by (building definition, its shape, first block still to come).</summary>
    private readonly Dictionary<(int Def, bool Base, int From), ArrayMesh?> _meshes = new();
    private MeshInstance3D _cursor = null!;

    /// <summary>Hides what is above level y of the height slice (a large y: nothing).</summary>
    public void SetSlice(float y)
    {
        _material.SetShaderParameter("slice_y", y);
        _depth.SetShaderParameter("slice_y", y);
    }

    /// <summary>Ghosts drawn now, by state (for the scripted checks): plans you cannot pay for, plans you can, sites.</summary>
    public (int Planned, int Ready, int Sites, int Ruins) Counts { get; private set; }

    public void Init(Simulation sim)
    {
        _sim = sim;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _depth = new ShaderMaterial { Shader = new Shader { Code = DepthCode }, NextPass = _material };
        _cursor = new MeshInstance3D { MaterialOverride = _depth, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_cursor);
    }

    public void UpdateGhosts(PlayerControl control)
    {
        float bs = ChunkMesher.BlockSize;
        int player = PlayerControl.LocalPlayer;
        _seen.Clear();
        int planned = 0, ready = 0, sites = 0;
        foreach (var b in _sim.Buildings.All)
        {
            if (b.Owner != player || b.Active) continue;
            Color tint;
            float pulse = 0;
            int from = 0;
            if (b.Planned)
            {
                bool canStart = _sim.Economy.CanStart(b) == PlaceResult.Ok;
                tint = canStart ? ReadyColor : PlannedColor;
                pulse = canStart ? 1 : 0;
                if (canStart) ready++; else planned++;
            }
            else
            {
                tint = SiteColor;
                from = b.Placed;
                sites++;
            }
            var mesh = MeshFor(b.Shape, from);
            if (mesh == null) continue;
            _seen.Add(b.Id);
            if (!_shown.TryGetValue(b.Id, out var node))
            {
                node = new MeshInstance3D { MaterialOverride = _depth, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(node);
                _shown[b.Id] = node;
            }
            node.Mesh = mesh;
            node.Position = new Vector3(b.X, b.Y, b.Z) * bs;
            node.SetInstanceShaderParameter("tint", tint);
            node.SetInstanceShaderParameter("pulse", pulse);
        }
        // Ruins to rebuild, keyed below every building id.
        var ruins = _sim.Automation.Ruins;
        int ruinCount = 0;
        for (int k = 0; k < ruins.Count; k++)
        {
            var r = ruins[k];
            if (r.Owner != player || MeshFor(r.Def.Shaped(_sim.Economy.LookOf(r.Owner)), 0) is not { } mesh) continue;
            int key = -1 - k;
            _seen.Add(key);
            ruinCount++;
            if (!_shown.TryGetValue(key, out var node))
            {
                node = new MeshInstance3D { MaterialOverride = _depth, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(node);
                _shown[key] = node;
            }
            node.Mesh = mesh;
            node.Position = new Vector3(r.X, r.Y, r.Z) * bs;
            node.SetInstanceShaderParameter("tint", RuinColor);
            node.SetInstanceShaderParameter("pulse", 0f);
        }
        Counts = (planned, ready, sites, ruinCount);
        if (_shown.Count > _seen.Count)
        {
            var gone = new List<int>();
            foreach (var id in _shown.Keys)
                if (!_seen.Contains(id)) gone.Add(id);
            foreach (int id in gone)
            {
                _shown[id].QueueFree();
                _shown.Remove(id);
            }
        }

        // The build tool's cursor: what it would make there.
        var ghost = control.Ghost;
        if (ghost.Visible && control.BuildDef is { } def && MeshFor(def.Shaped(_sim.Economy.LookOf(player)), 0) is { } m)
        {
            bool fits = ghost.Result is PlaceResult.Ok or PlaceResult.CannotAfford;
            var tint = !fits ? BlockedColor
                : !control.Planning ? SiteColor
                : ghost.Result == PlaceResult.Ok ? ReadyColor : PlannedColor;
            _cursor.Mesh = m;
            _cursor.Position = new Vector3(ghost.X, ghost.Y, ghost.Z) * bs;
            _cursor.SetInstanceShaderParameter("tint", tint);
            _cursor.SetInstanceShaderParameter("pulse", 0f);
            _cursor.Visible = true;
        }
        else _cursor.Visible = false;
    }

    /// <summary>The fine-voxel model of the building's blocks from index <paramref name="from"/> on (null if none are left), cached.</summary>
    private ArrayMesh? MeshFor(BuildingDef def, int from)
    {
        if (_meshes.TryGetValue((def.Index, def.IsBase, from), out var mesh)) return mesh;
        // A site's mesh changes as its blocks go up: keep only the newest few of each definition.
        if (from > 0 && _meshes.Count > 256) _meshes.Clear();
        mesh = Build(def, from);
        _meshes[(def.Index, def.IsBase, from)] = mesh;
        return mesh;
    }

    // Quad face numbering (0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z) as normals.
    private static readonly Vector3[] QuadNormals =
    {
        Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward,
    };

    // Quad corners are counter-clockwise from outside; Godot treats clockwise as front-facing.
    private static readonly int[] FineOrder = { 0, 2, 1, 0, 3, 2 };

    /// <summary>
    /// Meshes the template's blocks from <paramref name="from"/> on at fine-voxel scale, as the chunks draw
    /// buildings, in the building's own coordinates. Blocks already placed hide the faces against them.
    /// </summary>
    private static ArrayMesh? Build(BuildingDef def, int from)
    {
        const int f = StructureDetail.Size;
        var blocks = def.Blocks;
        if (from >= blocks.Count) return null;
        var all = new Dictionary<(int, int, int), int>();
        for (int k = 0; k < blocks.Count; k++) all[(blocks[k].X, blocks[k].Y, blocks[k].Z)] = k;
        int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue, z1 = int.MinValue;
        foreach (var t in blocks)
        {
            x0 = Math.Min(x0, t.X); y0 = Math.Min(y0, t.Y); z0 = Math.Min(z0, t.Z);
            x1 = Math.Max(x1, t.X); y1 = Math.Max(y1, t.Y); z1 = Math.Max(z1, t.Z);
        }
        int nx = (x1 - x0 + 1) * f, ny = (y1 - y0 + 1) * f, nz = (z1 - z0 + 1) * f;
        var grid = new byte[nx * ny * nz];
        var near = new BlockType[27];
        var shape = new byte[StructureDetail.Volume];
        bool any = false;
        for (int k = 0; k < blocks.Count; k++)
        {
            var t = blocks[k];
            int ox = (t.X - x0) * f, oy = (t.Y - y0) * f, oz = (t.Z - z0) * f;
            if (k < from || !StructureDetail.IsDetailed(t.Block))
            {
                Array.Fill(shape, StructurePalette.Occluder);
            }
            else
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    near[StructureDetail.NeighbourIndex(dx, dy, dz)] =
                        all.TryGetValue((t.X + dx, t.Y + dy, t.Z + dz), out int n) ? blocks[n].Block : BlockType.Air;
                StructureDetail.Fill(near, t.X, t.Y, t.Z, shape);
                any = true;
            }
            for (int y = 0; y < f; y++)
            for (int z = 0; z < f; z++)
                Array.Copy(shape, StructureDetail.VoxelIndex(0, y, z), grid, ((oy + y) * nz + oz + z) * nx + ox, f);
        }
        if (!any) return null;
        var quads = new List<FineQuad>();
        StructureMesher.Mesh(grid, nx, ny, nz, x0 * f, y0 * f, z0 * f, quads);
        if (quads.Count == 0) return null;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var q in quads)
        {
            st.SetNormal(QuadNormals[q.Face]);
            st.SetColor(ChunkMesher.PaletteColor(q.Colour) * ChunkMesher.Shade[q.Face]);
            foreach (int k in FineOrder)
            {
                var (cx, cy, cz) = q.Corner(k);
                st.AddVertex(new Vector3(cx, cy, cz) * ChunkMesher.FineSize);
            }
        }
        return st.Commit();
    }
}
