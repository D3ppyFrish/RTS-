using System;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// Draws the dark power's corrupted ground (see <see cref="AwakeningSystem"/>): on the top face of every corrupted
/// column you have seen, a flat tile a hair over the ground, black-violet and blotchy, with glowing violet veins that
/// pulse slowly and burn brighter towards the heart; and over the heart itself, once it is awake, a violet light.
/// The tiles are not blocks, so chunk meshing is untouched. They are rebuilt when the corruption creeps further, and
/// every few seconds for the ground changing under them.
/// </summary>
public partial class CorruptionRenderer : Node3D
{
    private const int FloatsPerInstance = 16; // 12 transform + 4 colour

    private const string ShaderCode = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque;
uniform float slice_y = 100000.0;
varying float v_world_y;
varying float v_phase;
void vertex() {
    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_world_y = world.y;
    v_phase = world.x * 0.21 + world.z * 0.17;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    ALBEDO = COLOR.rgb;
    ALPHA = COLOR.a;
    ROUGHNESS = 0.95;
    SPECULAR = 0.05;
    // Bright violet veins glow and pulse; the dark ground hardly at all.
    float vein = clamp((COLOR.b - 0.3) * 2.5, 0.0, 1.0);
    EMISSION = COLOR.rgb * vein * (0.7 + 0.5 * sin(TIME * 1.4 + v_phase));
}
";

    private static readonly Color Blight = new(0.035f, 0.02f, 0.045f);
    private static readonly Color BlightLight = new(0.1f, 0.05f, 0.12f);
    private static readonly Color Vein = new(0.55f, 0.16f, 0.85f);

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private ShaderMaterial _material = null!;
    private MultiMesh _tiles = null!;
    private OmniLight3D _light = null!;
    private float[] _buffer = Array.Empty<float>();
    private int _count;
    private int _radius = -1;
    private ulong _builtAt;
    private const ulong RebuildMs = 3000;

    public ShaderMaterial Material => _material;

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        var worldBox = new Aabb(new Vector3(-8, -8, -8),
            new Vector3(sim.World.SizeX + 16, sim.World.SizeY + 16, sim.World.SizeZ + 16) * ChunkMesher.BlockSize);
        _tiles = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new QuadMesh { Size = Vector2.One } };
        AddChild(new MultiMeshInstance3D { Multimesh = _tiles, MaterialOverride = _material, CustomAabb = worldBox, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _light = new OmniLight3D { LightColor = new Color(0.7f, 0.3f, 1f), LightEnergy = 0f, OmniRange = 24f * ChunkMesher.BlockSize, Visible = false };
        AddChild(_light);
    }

    /// <summary>Rebuilds the tiles when the corrupted ground reaches further, or every few seconds; pulses the heart's light.</summary>
    public void UpdateFrame()
    {
        if (!_sim.Awakening) return;
        var dp = _sim.DarkPower;
        ulong now = Time.GetTicksMsec();
        if (dp.Radius != _radius || (dp.Radius > 0 && now - _builtAt >= RebuildMs))
        {
            _radius = dp.Radius;
            _builtAt = now;
            Rebuild();
        }
        float bs = ChunkMesher.BlockSize;
        _light.Visible = dp.Stage > 0 && _fog.HasSeen(dp.HeartX, dp.HeartZ);
        if (_light.Visible)
        {
            _light.Position = new Vector3(dp.HeartX + 0.5f, dp.ScarY + 3f, dp.HeartZ + 0.5f) * bs;
            _light.LightEnergy = (1.2f + 0.5f * dp.Stage) * (0.8f + 0.2f * Mathf.Sin(now / 700f));
        }
    }

    private void Rebuild()
    {
        var dp = _sim.DarkPower;
        var world = _fog.World;
        float bs = ChunkMesher.BlockSize;
        _count = 0;
        int r = dp.Radius + AwakeningSystem.Ragged;
        for (int z = dp.HeartZ - r; z <= dp.HeartZ + r; z++)
        for (int x = dp.HeartX - r; x <= dp.HeartX + r; x++)
        {
            if (!world.InBounds(x, 0, z) || !dp.Corrupt(x, z) || !_fog.HasSeen(x, z)) continue;
            int y = Walk.GroundLevel(world, x, z);
            if (y <= 0 || world.Get(x, y - 1, z) is BlockType.Water or BlockType.Lava) continue;
            uint h = Hash(x, z);
            float Rand(int k) => ((h >> (k * 5)) & 31) / 31f;
            // Nearer the heart the veins run thicker; each column is blotchy dark ground or a glowing vein.
            int d = IntMath.Distance(x, z, dp.HeartX, dp.HeartZ);
            float near = 1f - Mathf.Clamp(d / (float)Math.Max(1, dp.Radius), 0f, 1f);
            bool vein = Rand(0) < 0.04f + 0.16f * near * near;
            var ground = Blight.Lerp(BlightLight, Rand(1));
            // The edge thins out to patches.
            ground.A = Mathf.Lerp(0.7f, 0.97f, Mathf.Min(1f, near * 4f + 0.3f * Rand(2)));
            float top = (y + ChunkRenderer.GroundDrop(x + 0.5f, y, z + 0.5f) + 0.02f) * bs;
            var at = new Vector3((x + 0.5f) * bs, top, (z + 0.5f) * bs);
            Add(at, 1.04f * bs, 0, ground);
            if (vein)
            {
                var glow = Vein.Lerp(BlightLight, 0.4f * Rand(3));
                glow.A = 0.95f;
                Add(at + new Vector3(0, 0.004f * bs, 0), (0.35f + 0.35f * Rand(3)) * bs, Rand(4) * Mathf.Tau, glow);
            }
        }
        Reserve(_count);
        RenderingServer.MultimeshSetBuffer(_tiles.GetRid(), _buffer);
        _tiles.VisibleInstanceCount = _count;
    }

    private void Add(Vector3 p, float size, float angle, Color c)
    {
        Reserve(_count + 1);
        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
        // Lying flat, face up, turned by the angle.
        var bx = new Vector3(cos, 0, sin) * size;
        var by = new Vector3(sin, 0, -cos) * size;
        var bz = Vector3.Up;
        int k = _count++ * FloatsPerInstance;
        var b = _buffer;
        b[k + 0] = bx.X; b[k + 1] = by.X; b[k + 2] = bz.X; b[k + 3] = p.X;
        b[k + 4] = bx.Y; b[k + 5] = by.Y; b[k + 6] = bz.Y; b[k + 7] = p.Y;
        b[k + 8] = bx.Z; b[k + 9] = by.Z; b[k + 10] = bz.Z; b[k + 11] = p.Z;
        b[k + 12] = c.R; b[k + 13] = c.G; b[k + 14] = c.B; b[k + 15] = c.A;
    }

    private void Reserve(int n)
    {
        int needed = Math.Max(16, n);
        if (_tiles.InstanceCount >= needed) return;
        int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
        _tiles.InstanceCount = cap;
        var grown = new float[cap * FloatsPerInstance];
        Array.Copy(_buffer, grown, Math.Min(_buffer.Length, grown.Length));
        _buffer = grown;
    }

    private static uint Hash(int x, int z) => (uint)(x * 73856093 ^ z * 19349663) * 2654435761u;
}
