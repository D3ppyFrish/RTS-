using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Economy;

namespace Rts.Game;

/// <summary>
/// Draws a ring under every selected unit (green for yours, red for others), a paler ring under units
/// inside a selection box being dragged, a short-lived marker where an order was given, a frame around
/// the selected building or city-state town, the road being laid, the wall being drawn (green where a column goes up, red where not), the areas being dug (marked out, or being dug by your units), and
/// a ring where each selected unit would stand in a formation line being dragged out.
/// </summary>
public partial class SelectionRenderer : Node3D
{
    private const int FloatsPerInstance = 16; // 12 transform + 4 colour

    public static readonly Color Own = new(0.35f, 1f, 0.4f);
    public static readonly Color Other = new(1f, 0.3f, 0.25f);
    public static readonly Color Preview = new(0.8f, 1f, 0.8f);
    /// <summary>Marker for work orders: gathering, building, farming.</summary>
    public static readonly Color Work = new(1f, 0.85f, 0.3f);
    private static readonly Color RoadOk = new(0.95f, 0.85f, 0.55f);
    /// <summary>The places in a formation line being dragged out.</summary>
    private static readonly Color FormationSpot = new(1f, 1f, 1f);
    private static readonly Color DigMarked = new(0.75f, 0.55f, 0.3f);

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
    ALBEDO = COLOR.rgb;
}
";

    private Simulation _sim = null!;
    private MultiMesh _rings = null!;
    private float[] _buffer = Array.Empty<float>();
    private ShaderMaterial _material = null!;
    private MeshInstance3D _marker = null!;
    private StandardMaterial3D _markerMaterial = null!;
    private double _markerAge = double.MaxValue;
    private const double MarkerSeconds = 0.7;
    private MeshInstance3D _frame = null!;
    private StandardMaterial3D _frameMaterial = null!;
    private int _frameBuilding;
    private MultiMesh _road = null!;
    private float[] _roadBuffer = Array.Empty<float>();
    private MultiMesh _dig = null!;
    private float[] _digBuffer = Array.Empty<float>();
    private MultiMesh _spots = null!;
    private float[] _spotBuffer = Array.Empty<float>();
    private readonly List<(int X, int Y, int Z, Color C)> _digSquares = new();

    public ShaderMaterial Material => _material;

    public void Init(Simulation sim)
    {
        _sim = sim;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _rings = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = Ring(0.3f, 0.4f, 32),
        };
        var worldBox = new Aabb(new Vector3(-8, -8, -8),
            new Vector3(sim.World.SizeX + 16, sim.World.SizeY + 16, sim.World.SizeZ + 16) * ChunkMesher.BlockSize);
        AddChild(new MultiMeshInstance3D { Multimesh = _rings, MaterialOverride = _material, CustomAabb = worldBox });

        _markerMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = true,
        };
        _marker = new MeshInstance3D { Mesh = Ring(0.55f, 0.7f, 40), MaterialOverride = _markerMaterial, Visible = false };
        AddChild(_marker);

        _frameMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _frame = new MeshInstance3D { MaterialOverride = _frameMaterial, Visible = false };
        AddChild(_frame);
        _road = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = Square(0.42f),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _road, MaterialOverride = _material, CustomAabb = worldBox });
        _dig = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = Square(0.3f),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _dig, MaterialOverride = _material, CustomAabb = worldBox });
        _spots = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = Ring(0.22f, 0.34f, 24),
        };
        AddChild(new MultiMeshInstance3D { Multimesh = _spots, MaterialOverride = _material, CustomAabb = worldBox });
    }

    /// <summary>Updates the building being placed, the selected building's frame and the road preview.</summary>
    public void UpdateTools(PlayerControl control)
    {
        float bs = ChunkMesher.BlockSize;
        // The building being placed is drawn as a see-through model by GhostRenderer.
        var b = _sim.Buildings.Get(control.SelectedBuilding);
        var town = _sim.Cities.Get(control.SelectedCity);
        if (b != null)
        {
            if (_frameBuilding != b.Id) _frame.Mesh = Frame(b.Def.SizeX, b.Def.SizeZ);
            _frameBuilding = b.Id;
            _frame.Position = new Vector3(b.X, b.Y, b.Z) * bs + new Vector3(0, 0.03f, 0);
            _frameMaterial.AlbedoColor = b.Owner == PlayerControl.LocalPlayer ? Own : Other;
            _frame.Visible = true;
        }
        else if (town != null)
        {
            // Towns use negative keys, so a town's frame is never mistaken for a building's.
            int r = CityStateSystem.TownRadius;
            if (_frameBuilding != -town.Id) _frame.Mesh = Frame(2 * r + 1, 2 * r + 1);
            _frameBuilding = -town.Id;
            _frame.Position = new Vector3(town.X - r, town.Y, town.Z - r) * bs + new Vector3(0, 0.03f, 0);
            _frameMaterial.AlbedoColor = town.Holder == PlayerControl.LocalPlayer ? Own : Work;
            _frame.Visible = true;
        }
        else
        {
            _frame.Visible = false;
            _frameBuilding = 0;
        }

        var road = control.RoadPreview;
        int needed = Math.Max(16, road.Count);
        if (_road.InstanceCount < needed)
        {
            int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
            _road.InstanceCount = cap;
            _roadBuffer = new float[cap * FloatsPerInstance];
        }
        int stone = road.Count > 0 ? _sim.Economy.Stock(PlayerControl.LocalPlayer, Good.Stone) : 0;
        for (int k = 0; k < road.Count; k++)
        {
            var (x, y, z) = road[k];
            var color = k < stone / EconomySystem.StonePerRoad ? RoadOk : Other;
            int o = k * FloatsPerInstance;
            var r = _roadBuffer;
            r[o + 0] = 1; r[o + 1] = 0; r[o + 2] = 0; r[o + 3] = (x + 0.5f) * bs;
            r[o + 4] = 0; r[o + 5] = 1; r[o + 6] = 0; r[o + 7] = y * bs + 0.03f;
            r[o + 8] = 0; r[o + 9] = 0; r[o + 10] = 1; r[o + 11] = (z + 0.5f) * bs;
            r[o + 12] = color.R; r[o + 13] = color.G; r[o + 14] = color.B; r[o + 15] = 1;
        }
        RenderingServer.MultimeshSetBuffer(_road.GetRid(), _roadBuffer);
        _road.VisibleInstanceCount = road.Count;
        UpdateDig(control);
        UpdateSpots(control);
    }

    /// <summary>A ring on each place in the formation line being dragged out.</summary>
    private void UpdateSpots(PlayerControl control)
    {
        float bs = ChunkMesher.BlockSize;
        var line = control.FormationPreview;
        int needed = Math.Max(16, line.Count);
        if (_spots.InstanceCount < needed)
        {
            int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
            _spots.InstanceCount = cap;
            _spotBuffer = new float[cap * FloatsPerInstance];
        }
        for (int k = 0; k < line.Count; k++)
        {
            var p = line[k];
            int o = k * FloatsPerInstance;
            var r = _spotBuffer;
            r[o + 0] = 1; r[o + 1] = 0; r[o + 2] = 0; r[o + 3] = p.X * bs;
            r[o + 4] = 0; r[o + 5] = 1; r[o + 6] = 0; r[o + 7] = p.Y * bs + 0.04f;
            r[o + 8] = 0; r[o + 9] = 0; r[o + 10] = 1; r[o + 11] = p.Z * bs;
            r[o + 12] = FormationSpot.R; r[o + 13] = FormationSpot.G; r[o + 14] = FormationSpot.B; r[o + 15] = 1;
        }
        RenderingServer.MultimeshSetBuffer(_spots.GetRid(), _spotBuffer);
        _spots.VisibleInstanceCount = line.Count;
    }

    /// <summary>
    /// A small square on each column of the area the dig tool is marking out, and on each column of your dig
    /// areas that still has something to dig.
    /// </summary>
    private void UpdateDig(PlayerControl control)
    {
        float bs = ChunkMesher.BlockSize;
        _digSquares.Clear();
        foreach (var (x, y, z) in control.DigPreview) _digSquares.Add((x, y, z, Work));
        foreach (var (x, y, z, ok) in control.WallPreview) _digSquares.Add((x, y, z, ok ? Own : Other));
        var w = control.Fog.World;
        foreach (var a in _sim.Economy.DigAreas)
        {
            if (a.Owner != PlayerControl.LocalPlayer) continue;
            for (int z = a.Z1; z <= a.Z2; z++)
            for (int x = a.X1; x <= a.X2; x++)
            {
                int y = Rts.Sim.Units.Walk.GroundLevel(w, x, z);
                if (y > a.LowestDug(x, z)) _digSquares.Add((x, y, z, DigMarked));
            }
        }
        int needed = Math.Max(16, _digSquares.Count);
        if (_dig.InstanceCount < needed)
        {
            int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
            _dig.InstanceCount = cap;
            _digBuffer = new float[cap * FloatsPerInstance];
        }
        for (int k = 0; k < _digSquares.Count; k++)
        {
            var (x, y, z, color) = _digSquares[k];
            int o = k * FloatsPerInstance;
            var r = _digBuffer;
            r[o + 0] = 1; r[o + 1] = 0; r[o + 2] = 0; r[o + 3] = (x + 0.5f) * bs;
            r[o + 4] = 0; r[o + 5] = 1; r[o + 6] = 0; r[o + 7] = y * bs + 0.03f;
            r[o + 8] = 0; r[o + 9] = 0; r[o + 10] = 1; r[o + 11] = (z + 0.5f) * bs;
            r[o + 12] = color.R; r[o + 13] = color.G; r[o + 14] = color.B; r[o + 15] = 1;
        }
        RenderingServer.MultimeshSetBuffer(_dig.GetRid(), _digBuffer);
        _dig.VisibleInstanceCount = _digSquares.Count;
    }

    /// <summary>Shows the order marker at a world position (metres).</summary>
    public void ShowMarker(Vector3 position, Color color)
    {
        _marker.Position = position + new Vector3(0, 0.04f, 0);
        _markerMaterial.AlbedoColor = color;
        _markerAge = 0;
        _marker.Visible = true;
    }

    /// <summary>Rebuilds the rings for this frame; <paramref name="alpha"/> interpolates between ticks.</summary>
    public void UpdateRings(IReadOnlyList<int> selected, IReadOnlyList<int> preview, int localPlayer, float alpha, double delta)
    {
        int needed = Math.Max(16, selected.Count + preview.Count);
        if (_rings.InstanceCount < needed)
        {
            int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
            _rings.InstanceCount = cap;
            _buffer = new float[cap * FloatsPerInstance];
        }
        int count = 0;
        foreach (int id in selected) count = Add(id, null, localPlayer, count, alpha);
        foreach (int id in preview) count = Add(id, Preview, localPlayer, count, alpha);
        RenderingServer.MultimeshSetBuffer(_rings.GetRid(), _buffer);
        _rings.VisibleInstanceCount = count;

        if (_marker.Visible)
        {
            _markerAge += delta;
            float t = (float)(_markerAge / MarkerSeconds);
            if (t >= 1) _marker.Visible = false;
            else
            {
                _marker.Scale = Vector3.One * Mathf.Lerp(1.3f, 0.7f, t);
                var c = _markerMaterial.AlbedoColor;
                _markerMaterial.AlbedoColor = new Color(c.R, c.G, c.B, 1 - t);
            }
        }
    }

    private int Add(int id, Color? fixedColor, int localPlayer, int count, float alpha)
    {
        var u = _sim.Units;
        int i = u.IndexOf(id);
        if (i < 0) return count;
        var color = fixedColor ?? (u.Owner[i] == localPlayer ? Own : Other);
        float bs = ChunkMesher.BlockSize;
        float x = Mathf.Lerp(u.PrevX[i].ToFloat(), u.X[i].ToFloat(), alpha);
        float z = Mathf.Lerp(u.PrevZ[i].ToFloat(), u.Z[i].ToFloat(), alpha);
        float y = Mathf.Lerp(u.PrevY[i], u.Y[i], alpha) + UnitRenderer.DeckOffset(_sim, i);
        int o = count * FloatsPerInstance;
        var b = _buffer;
        b[o + 0] = 1; b[o + 1] = 0; b[o + 2] = 0; b[o + 3] = x * bs;
        b[o + 4] = 0; b[o + 5] = 1; b[o + 6] = 0; b[o + 7] = y * bs + 0.03f;
        b[o + 8] = 0; b[o + 9] = 0; b[o + 10] = 1; b[o + 11] = z * bs;
        b[o + 12] = color.R; b[o + 13] = color.G; b[o + 14] = color.B; b[o + 15] = 1;
        return count + 1;
    }

    /// <summary>A flat square in the ground plane, half its side in metres.</summary>
    private static ArrayMesh Square(float half)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        st.SetNormal(Vector3.Up);
        var a = new Vector3(-half, 0, -half);
        var b = new Vector3(half, 0, -half);
        var c = new Vector3(half, 0, half);
        var d = new Vector3(-half, 0, half);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        return st.Commit();
    }

    /// <summary>
    /// A flat frame around a building's footprint of sx by sz blocks, in the gap half a block outside it.
    /// Its origin is the footprint's corner.
    /// </summary>
    private static ArrayMesh Frame(int sx, int sz)
    {
        float bs = ChunkMesher.BlockSize, o = 0.55f * bs, t = 0.18f * bs;
        float x0 = -o, z0 = -o, x1 = sx * bs + o, z1 = sz * bs + o;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        st.SetNormal(Vector3.Up);
        void Bar(float ax, float az, float bx, float bz)
        {
            var p0 = new Vector3(ax, 0, az);
            var p1 = new Vector3(bx, 0, az);
            var p2 = new Vector3(bx, 0, bz);
            var p3 = new Vector3(ax, 0, bz);
            st.AddVertex(p0); st.AddVertex(p1); st.AddVertex(p2);
            st.AddVertex(p0); st.AddVertex(p2); st.AddVertex(p3);
        }
        Bar(x0, z0, x1, z0 + t);
        Bar(x0, z1 - t, x1, z1);
        Bar(x0, z0 + t, x0 + t, z1 - t);
        Bar(x1 - t, z0 + t, x1, z1 - t);
        return st.Commit();
    }

    /// <summary>A flat ring in the ground plane, radii in metres.</summary>
    internal static ArrayMesh Ring(float inner, float outer, int segments)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        for (int s = 0; s < segments; s++)
        {
            float a0 = Mathf.Tau * s / segments, a1 = Mathf.Tau * (s + 1) / segments;
            var i0 = new Vector3(Mathf.Cos(a0) * inner, 0, Mathf.Sin(a0) * inner);
            var o0 = new Vector3(Mathf.Cos(a0) * outer, 0, Mathf.Sin(a0) * outer);
            var i1 = new Vector3(Mathf.Cos(a1) * inner, 0, Mathf.Sin(a1) * inner);
            var o1 = new Vector3(Mathf.Cos(a1) * outer, 0, Mathf.Sin(a1) * outer);
            st.SetNormal(Vector3.Up);
            st.AddVertex(i0); st.AddVertex(o0); st.AddVertex(o1);
            st.AddVertex(i0); st.AddVertex(o1); st.AddVertex(i1);
        }
        return st.Commit();
    }
}
