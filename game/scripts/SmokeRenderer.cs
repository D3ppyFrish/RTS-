using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// Draws Clan Kage's smoke bombs (see <see cref="CombatSystem.Clouds"/>): each cloud as a heap of grey voxel puffs over
/// the ground where it was thrown, as wide as the cloud reaches, swelling as it is thrown, churning slowly, and thinning
/// out in its last seconds. A cloud shows where you can see its middle; the units in it are hidden by the rules.
/// Their shadow steps (see <see cref="CombatSystem.ShadowSteps"/>) as a swirl of dark violet puffs where the units went
/// into the shadows and where they came out, for a moment after the step, where you can see.
/// </summary>
public partial class SmokeRenderer : Node3D
{
    private const string ShaderCode = @"
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;
uniform float slice_y = 100000.0;
varying float v_alpha;
varying float v_shade;
varying float v_world_y;
varying float v_dark;
void vertex() {
    vec4 c = INSTANCE_CUSTOM; // x: seed, y: how thick the cloud is (0 to 1), z: how far it has swelled (0 to 1), w: 1 for shadow
    v_dark = c.w;
    float t = TIME * 0.6 + c.x * 40.0;
    vec3 churn = vec3(sin(t), sin(t * 0.7 + 1.3) * 0.5 + 0.5, cos(t * 0.8)) * 0.18;
    VERTEX = VERTEX * mix(0.35, 1.0, c.z) + churn;
    v_alpha = c.y;
    v_shade = 0.52 + 0.16 * fract(c.x * 7.0);
    v_world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}
void fragment() {
    if (v_world_y > slice_y) discard;
    vec3 grey = vec3(v_shade, v_shade, v_shade * 1.03);
    vec3 shadow = vec3(0.19, 0.08, 0.30) + vec3(0.10, 0.04, 0.14) * (v_shade - 0.52) * 6.0;
    ALBEDO = mix(grey, shadow, v_dark);
    ALPHA = v_alpha * mix(0.7, 0.55, v_dark);
}
";

    /// <summary>Puffs drawn for each cloud.</summary>
    private const int Puffs = 44;

    /// <summary>Puffs drawn at each end of a shadow step.</summary>
    private const int StepPuffs = 22;

    /// <summary>Seconds a thrown bomb takes to swell to its full cloud, and the last seconds it thins out over.</summary>
    private const float Swell = 0.6f, Thin = 2f;

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private MultiMesh _multi = null!;
    private ShaderMaterial _material = null!;

    public ShaderMaterial Material => _material;

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = new BoxMesh { Size = Vector3.One * ChunkMesher.BlockSize },
        };
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _multi,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    public void UpdateFrame()
    {
        var clouds = _sim.Combat.Clouds;
        var rules = _sim.Content.Combat.Smoke;
        float s = ChunkMesher.BlockSize, tps = Simulation.TicksPerSecond;
        var shown = new List<CombatSystem.SmokeCloud>();
        foreach (var c in clouds)
            if (_fog.Sees(c.X.FloorToInt(), c.Z.FloorToInt())) shown.Add(c);
        // Each end of a step made lately that you can see.
        var ends = new List<(float X, float Y, float Z, float Age, float Left, uint Seed)>();
        float show = CombatSystem.StepShowTicks / tps;
        foreach (var st in _sim.Combat.ShadowSteps)
        {
            float age = show - st.Left / tps, left = st.Left / tps;
            uint seed = (uint)(st.FromX * 73856093 ^ st.ToZ * 19349663 ^ st.Left / 1000);
            if (_fog.Sees(st.FromX, st.FromZ)) ends.Add((st.FromX + 0.5f, st.FromY, st.FromZ + 0.5f, age, left, seed));
            if (_fog.Sees(st.ToX, st.ToZ)) ends.Add((st.ToX + 0.5f, st.ToY, st.ToZ + 0.5f, age, left, seed * 31 + 7));
        }
        _multi.InstanceCount = shown.Count * Puffs + ends.Count * StepPuffs;
        int k = 0;
        foreach (var e in ends)
        {
            float swell = Mathf.Clamp(e.Age / 0.3f, 0f, 1f), thick = Mathf.Clamp(e.Left / 1.2f, 0f, 1f);
            for (int p = 0; p < StepPuffs; p++)
            {
                // A low swirl round the spot, rising as it fades.
                uint h = Hash(e.Seed + (uint)p * 977);
                float a = (h & 1023) / 1023f * Mathf.Tau + e.Age * 2.5f;
                float d = (0.4f + ((h >> 10) & 1023) / 1023f * 2.2f) * swell;
                float up = ((h >> 20) & 255) / 255f * 2.2f + e.Age * 0.6f;
                float size = 0.35f + ((h >> 28) & 15) / 15f * 0.4f;
                var at = new Vector3((e.X + MathF.Cos(a) * d) * s, (e.Y + 0.3f + up) * s, (e.Z + MathF.Sin(a) * d) * s);
                _multi.SetInstanceTransform(k, new Transform3D(Basis.FromScale(Vector3.One * size), at));
                _multi.SetInstanceCustomData(k, new Color((h & 0xFFFF) / 65535f, thick, swell, 1));
                k++;
            }
        }
        foreach (var c in shown)
        {
            float age = (_sim.Tick - c.Thrown) / tps, left = c.Left / tps;
            float swell = Mathf.Clamp(age / Swell, 0f, 1f);
            float thick = Mathf.Clamp(left / Thin, 0f, 1f);
            float cx = c.X.ToFloat(), cz = c.Z.ToFloat(), ground = c.Y;
            for (int p = 0; p < Puffs; p++)
            {
                // Each puff its own place in the cloud, the same every frame: fuller in the middle, low to the ground.
                uint h = Hash((uint)(c.Thrown * 31 + c.Owner * 7 + p * 977));
                float a = (h & 1023) / 1023f * Mathf.Tau;
                float d = MathF.Sqrt(((h >> 10) & 1023) / 1023f) * rules.Radius * swell;
                float up = ((h >> 20) & 255) / 255f * (1.4f + rules.Height * 0.4f) * (1f - 0.5f * d / Math.Max(1, rules.Radius));
                float size = 0.8f + ((h >> 28) & 15) / 15f * 0.9f;
                var at = new Vector3((cx + MathF.Cos(a) * d) * s, (ground + 0.4f + up) * s, (cz + MathF.Sin(a) * d) * s);
                _multi.SetInstanceTransform(k, new Transform3D(Basis.FromScale(Vector3.One * size), at));
                _multi.SetInstanceCustomData(k, new Color((h & 0xFFFF) / 65535f, thick, swell, 0));
                k++;
            }
        }
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x;
    }
}
