using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// Draws blood (see <see cref="BloodSystem"/>): a stain on the top face of every stained block you can see,
/// wider and darker the more blood there is and fainter as it ages, with splatter around a pool; and on every
/// bleeding unit you can see, a dark wet patch on its body and drops falling from it. The stains are flat
/// squares a hair over the block (lit like the ground, with a wet shine), not blocks, so chunk meshing is
/// untouched. They are rebuilt when the blood changes, at most every few ticks.
/// </summary>
public partial class BloodRenderer : Node3D
{
    private const int FloatsPerInstance = 16; // 12 transform + 4 colour

    private const string ShaderCode = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque;
uniform float slice_y = 100000.0;
varying float v_world_y;
void vertex() {
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    ALBEDO = COLOR.rgb;
    ALPHA = COLOR.a;
    ROUGHNESS = 0.22;
    SPECULAR = 0.7;
}
";

    // Fresh blood is a vivid red; old blood dries to a dark brownish red.
    private static readonly Color Fresh = new(0.78f, 0.02f, 0.04f);
    private static readonly Color Dried = new(0.42f, 0.05f, 0.04f);
    private static readonly Color Drop = new(0.9f, 0.04f, 0.06f);

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private ShaderMaterial _material = null!;
    private MultiMesh _stains = null!, _drops = null!;
    private float[] _stainBuffer = Array.Empty<float>(), _dropBuffer = Array.Empty<float>();
    private int _stainCount, _dropCount;
    private int _version = -1;
    private int _ground = -1;
    private ulong _groundAt;
    private const ulong GroundRebuildMs = 250;
    private long _tick = long.MinValue / 2;
    private const int RebuildTicks = 4;

    public ShaderMaterial Material => _material;

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        var worldBox = new Aabb(new Vector3(-8, -8, -8),
            new Vector3(sim.World.SizeX + 16, sim.World.SizeY + 16, sim.World.SizeZ + 16) * ChunkMesher.BlockSize);
        MultiMesh Make(Mesh mesh)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
            AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _material, CustomAabb = worldBox, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            return mm;
        }
        _stains = Make(Splat());
        _drops = Make(new BoxMesh { Size = Vector3.One });
    }

    /// <summary>Rebuilds the stains if the blood changed since the last tick, and the drops every frame.</summary>
    public void UpdateFrame(float alpha)
    {
        // Blood changes slowly: a few rebuilds a second are enough, however busy the battle. The same goes for
        // the ground drawn under the stains moving (half-size blocks coming and going as the camera moves).
        ulong now = Time.GetTicksMsec();
        bool bled = _sim.Blood.Version != _version && (_sim.Tick - _tick >= RebuildTicks || _sim.Tick < _tick);
        bool moved = ChunkRenderer.GroundVersion != _ground && now - _groundAt >= GroundRebuildMs;
        if (bled || moved)
        {
            _tick = _sim.Tick;
            _version = _sim.Blood.Version;
            _ground = ChunkRenderer.GroundVersion;
            _groundAt = now;
            RebuildStains();
        }
        RebuildDrops(alpha);
    }

    private void RebuildStains()
    {
        var blood = _sim.Blood;
        float bs = ChunkMesher.BlockSize;
        int life = blood.LifeSeconds;
        _stainCount = 0;
        _trail.Clear();
        for (int s = 0; s < blood.Count; s++)
        {
            var st = blood[s];
            if (!_fog.Sees(st.X, st.Z)) continue;
            float amount = st.Amount / (float)BloodSystem.MaxAmount;
            float age = Math.Clamp(st.Age / (float)life, 0f, 1f);
            // A per-column scatter, the same every frame.
            uint h = Hash(st.X, st.Z);
            float Rand(int k) => ((h >> (k * 5)) & 31) / 31f;
            var color = Tint(st, life);
            float size = Mathf.Lerp(0.22f, 1.1f, Mathf.Sqrt(amount));
            float top = Top(st);
            // Where the blood actually fell in the column, so stains run across the tile edges.
            var centre = Spot(st);
            // A drop is a little oval along the way; a pool is a broad, uneven splat.
            float stretch = amount < 0.2f ? 1.6f : 1.1f;
            Add(centre with { Y = top }, size * stretch * bs, size * bs, Rand(2) * Mathf.Tau, color);
            // More blood splashes around: a drop or two beside a trail, a spatter round a pool.
            int splashes = amount > 0.5f ? 4 : amount > 0.12f ? 2 : 1;
            for (int k = 0; k < splashes; k++)
            {
                float a = (Rand(3) + k / (float)splashes) * Mathf.Tau, r = (0.45f + 0.5f * Rand(4 + k % 2)) * size;
                var p = centre + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r) * bs;
                float drop = size * (0.18f + 0.15f * Rand(5 + k % 2));
                Add(p with { Y = top + 0.002f * bs }, drop * 1.8f * bs, drop * bs, a, color);
            }
            if (st.Source != 0) _trail.Add(s);
        }
        DrawTrails(bs, life);
        RenderingServer.MultimeshSetBuffer(_stains.GetRid(), Fit(ref _stainBuffer, _stains, _stainCount));
        _stains.VisibleInstanceCount = _stainCount;
    }

    /// <summary>
    /// Joins each unit's stains in the order its blood fell (within <see cref="TrailGapTicks"/> of each other and
    /// a few blocks apart, on the same level) with a smear and a line of drops, so a wounded unit leaves a
    /// trail along the way it walked, not a stain per tile.
    /// </summary>
    private void DrawTrails(float bs, int life)
    {
        var blood = _sim.Blood;
        _trail.Sort((a, b) =>
        {
            var x = blood[a];
            var y = blood[b];
            int c = x.Source.CompareTo(y.Source);
            return c != 0 ? c : x.Fresh.CompareTo(y.Fresh);
        });
        for (int k = 1; k < _trail.Count; k++)
        {
            var a = blood[_trail[k - 1]];
            var b = blood[_trail[k]];
            if (a.Source != b.Source || b.Fresh - a.Fresh > TrailGapTicks || a.Y != b.Y) continue;
            var pa = Spot(a);
            var pb = Spot(b);
            var d = pb - pa;
            float length = d.Length();
            if (length < 0.05f * bs || length > TrailReach * bs) continue;
            var color = Tint(a, life);
            color.A *= 0.85f;
            float amount = Mathf.Sqrt(Math.Min(a.Amount, b.Amount) / (float)BloodSystem.MaxAmount);
            float width = Mathf.Lerp(0.06f, 0.2f, amount) * bs;
            float angle = Mathf.Atan2(d.Z, d.X);
            float top = Top(a) + 0.001f * bs;
            // A thin smear from one drop to the next...
            Add((pa + d / 2) with { Y = top }, length, width, angle, color);
            // ...and drops along it, a little to either side.
            uint h = Hash(b.X * 31 + a.X, b.Z * 17 + a.Z);
            int drops = 1 + (int)(h % 3);
            for (int j = 0; j < drops; j++)
            {
                float t = (j + 0.5f + 0.4f * (((h >> (4 + j * 6)) & 15) / 15f - 0.5f)) / drops;
                float side = ((((h >> (8 + j * 6)) & 15) / 15f) - 0.5f) * 0.35f * bs;
                var normal = new Vector3(-d.Z, 0, d.X) / length;
                var p = pa + d * t + normal * side;
                float r = (0.07f + 0.08f * (((h >> (12 + j * 4)) & 7) / 7f)) * bs;
                Add(p with { Y = top + 0.001f * bs }, r * 1.6f, r, angle, color);
            }
        }
    }

    /// <summary>Most ticks between two drops of one unit that are joined into a trail.</summary>
    private const int TrailGapTicks = 2 * Simulation.TicksPerSecond;
    /// <summary>Furthest apart (blocks) two drops are joined.</summary>
    private const float TrailReach = 3.2f;

    private readonly System.Collections.Generic.List<int> _trail = new();

    private static uint Hash(int x, int z) => (uint)(x * 73856093 ^ z * 19349663) * 2654435761u;

    /// <summary>Where the stain's blood last fell, at ground level.</summary>
    private static Vector3 Spot(Stain st) =>
        new Vector3(st.X + (st.FX + 0.5f) / 16f, 0, st.Z + (st.FZ + 0.5f) / 16f) * ChunkMesher.BlockSize;

    /// <summary>The height (metres) of the ground the stain lies on, as it is drawn where the blood fell.</summary>
    private static float Top(Stain st) =>
        (st.Y + 1 + ChunkRenderer.GroundDrop(st.X + (st.FX + 0.5f) / 16f, st.Y + 1, st.Z + (st.FZ + 0.5f) / 16f) + 0.015f) * ChunkMesher.BlockSize;

    /// <summary>Bright and wet when fresh, drying darker and browner and fading as it ages; thin blood shows less.</summary>
    private static Color Tint(Stain st, int life)
    {
        float amount = st.Amount / (float)BloodSystem.MaxAmount;
        float age = Math.Clamp(st.Age / (float)life, 0f, 1f);
        var color = Fresh.Lerp(Dried, Mathf.Min(1f, age * 1.6f));
        color.A = Mathf.Lerp(0.97f, 0.2f, age) * Mathf.Lerp(0.7f, 1f, Mathf.Sqrt(amount));
        return color;
    }

    private void Add(Vector3 p, float length, float width, float angle, Color c)
    {
        Reserve(ref _stainBuffer, _stains, _stainCount + 1);
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        // Lying flat: the splat's x runs along the angle (its length), its y across (its width), its face up.
        Write(_stainBuffer, _stainCount++, new Vector3(cos, 0, sin) * length, new Vector3(sin, 0, -cos) * width, Vector3.Up, p, c);
    }

    /// <summary>On each bleeding unit in sight, a wet patch on its body and a drop falling from it, in time with the sim's drips.</summary>
    private void RebuildDrops(float alpha)
    {
        var u = _sim.Units;
        float bs = ChunkMesher.BlockSize;
        _dropCount = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Bleeding[i] == 0 || !_fog.Shows(i)) continue;
            float x = Mathf.Lerp(u.PrevX[i].ToFloat(), u.X[i].ToFloat(), alpha);
            float z = Mathf.Lerp(u.PrevZ[i].ToFloat(), u.Z[i].ToFloat(), alpha);
            float y = Mathf.Lerp(u.PrevY[i], u.Y[i], alpha) + ChunkRenderer.GroundDrop(x, u.Y[i], z);
            int parts = 0;
            for (int p = 0; p < Body.Parts; p++)
                if ((u.Bleeding[i] & (1 << p)) != 0) parts++;
            // The patch on its body: higher for a head wound, lower for the legs.
            float at = (u.Bleeding[i] & 1) != 0 ? 1.55f : (u.Bleeding[i] & 2) != 0 ? 1.05f : 0.6f;
            float patch = (0.22f + 0.06f * parts) * bs;
            Cube(new Vector3(x * bs, (y + at) * bs, z * bs), patch, Drop);
            // A drop falls from it to the ground once per drip.
            float phase = ((_sim.Tick + u.Id[i]) % BloodSystem.DripTicks + alpha) / BloodSystem.DripTicks;
            float fall = Mathf.Lerp(at, 0.05f, phase * phase);
            Cube(new Vector3((x + 0.12f) * bs, (y + fall) * bs, (z + 0.08f) * bs), 0.09f * bs, Drop);
        }
        RenderingServer.MultimeshSetBuffer(_drops.GetRid(), Fit(ref _dropBuffer, _drops, _dropCount));
        _drops.VisibleInstanceCount = _dropCount;
    }

    private void Cube(Vector3 p, float size, Color c)
    {
        Reserve(ref _dropBuffer, _drops, _dropCount + 1);
        Write(_dropBuffer, _dropCount++, Vector3.Right * size, Vector3.Up * size, Vector3.Back * size, p, c);
    }

    private static void Reserve(ref float[] buffer, MultiMesh mesh, int n)
    {
        int needed = Math.Max(16, n);
        if (mesh.InstanceCount >= needed) return;
        int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
        mesh.InstanceCount = cap;
        var grown = new float[cap * FloatsPerInstance];
        Array.Copy(buffer, grown, Math.Min(buffer.Length, grown.Length));
        buffer = grown;
    }

    private static float[] Fit(ref float[] buffer, MultiMesh mesh, int count)
    {
        Reserve(ref buffer, mesh, count);
        return buffer;
    }

    private static void Write(float[] b, int index, Vector3 bx, Vector3 by, Vector3 bz, Vector3 o, Color c)
    {
        int k = index * FloatsPerInstance;
        b[k + 0] = bx.X; b[k + 1] = by.X; b[k + 2] = bz.X; b[k + 3] = o.X;
        b[k + 4] = bx.Y; b[k + 5] = by.Y; b[k + 6] = bz.Y; b[k + 7] = o.Y;
        b[k + 8] = bx.Z; b[k + 9] = by.Z; b[k + 10] = bz.Z; b[k + 11] = o.Z;
        b[k + 12] = c.R; b[k + 13] = c.G; b[k + 14] = c.B; b[k + 15] = c.A;
    }

    /// <summary>A rough round splat, 1 across, in the XY plane, facing +Z: a fan of uneven radius.</summary>
    private static ArrayMesh Splat()
    {
        const int Sides = 10;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        st.SetNormal(Vector3.Back);
        Vector3 Rim(int k)
        {
            float a = k * Mathf.Tau / Sides, r = 0.5f * (k % 3 == 0 ? 1f : k % 3 == 1 ? 0.78f : 0.9f);
            return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0);
        }
        for (int k = 0; k < Sides; k++)
        {
            st.AddVertex(Vector3.Zero);
            st.AddVertex(Rim(k));
            st.AddVertex(Rim(k + 1));
        }
        return st.Commit();
    }
}
