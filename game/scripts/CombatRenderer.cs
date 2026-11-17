using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;

namespace Rts.Game;

/// <summary>
/// Draws the fighting: a health bar over every hurt or selected unit and every damaged building, arrows
/// and blasts in flight and the flash of blows (from <see cref="CombatSystem.Shots"/>), a white ring
/// under units that have broken and are running away, and a wider gold ring under your units that wait
/// for a choice: those that can evolve (see <see cref="EvolutionSystem"/>) and the monsters you beat (see
/// <see cref="MonsterSystem"/>). Bars face the camera.
/// </summary>
public partial class CombatRenderer : Node3D
{
    private const int FloatsPerInstance = 16; // 12 transform + 4 colour

    private static readonly Color BarBack = new(0.07f, 0.07f, 0.08f);
    private static readonly Color Healthy = new(0.3f, 0.9f, 0.35f);
    private static readonly Color Hurt = new(0.95f, 0.8f, 0.2f);
    private static readonly Color Dying = new(0.95f, 0.25f, 0.2f);
    private static readonly Color Arrow = new(0.95f, 0.9f, 0.75f);
    private static readonly Color Blast = new(0.75f, 0.45f, 1f);
    private static readonly Color Blow = new(1f, 1f, 0.8f);
    private static readonly Color Routing = new(0.95f, 0.95f, 0.95f);
    private static readonly Color Evolvable = new(1f, 0.78f, 0.2f);

    /// <summary>Ticks an arrow and a blast take to fly, and that a blast's burst and a blow's flash last.</summary>
    private const float ArrowTicks = 4, BlastTicks = 6, BurstTicks = 3, FlashTicks = 2;

    private sealed class Batch
    {
        public required MultiMesh Mesh;
        public float[] Buffer = Array.Empty<float>();
        public int Count;

        public void Reserve(int n)
        {
            int needed = Math.Max(16, n);
            if (Mesh.InstanceCount >= needed) return;
            int cap = 1 << (int)Math.Ceiling(Math.Log2(needed));
            Mesh.InstanceCount = cap;
            var grown = new float[cap * FloatsPerInstance];
            Array.Copy(Buffer, grown, Math.Min(Buffer.Length, grown.Length));
            Buffer = grown;
        }

        /// <summary>Adds an instance with basis columns bx, by, bz at origin o.</summary>
        public void Add(Vector3 bx, Vector3 by, Vector3 bz, Vector3 o, Color c)
        {
            Reserve(Count + 1);
            int k = Count++ * FloatsPerInstance;
            var b = Buffer;
            b[k + 0] = bx.X; b[k + 1] = by.X; b[k + 2] = bz.X; b[k + 3] = o.X;
            b[k + 4] = bx.Y; b[k + 5] = by.Y; b[k + 6] = bz.Y; b[k + 7] = o.Y;
            b[k + 8] = bx.Z; b[k + 9] = by.Z; b[k + 10] = bz.Z; b[k + 11] = o.Z;
            b[k + 12] = c.R; b[k + 13] = c.G; b[k + 14] = c.B; b[k + 15] = 1;
        }

        public void Upload()
        {
            RenderingServer.MultimeshSetBuffer(Mesh.GetRid(), Buffer);
            Mesh.VisibleInstanceCount = Count;
        }
    }

    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private Camera3D _camera = null!;
    private ShaderMaterial _material = null!;
    private Batch _bars = null!, _fills = null!, _shots = null!, _rings = null!, _ready = null!;
    private readonly HashSet<int> _selected = new();

    public ShaderMaterial Material => _material;

    public void Init(Simulation sim, Camera3D camera, Shader shader, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
        _camera = camera;
        _material = new ShaderMaterial { Shader = shader };
        var worldBox = new Aabb(new Vector3(-8, -8, -8),
            new Vector3(sim.World.SizeX + 16, sim.World.SizeY + 16, sim.World.SizeZ + 16) * ChunkMesher.BlockSize);
        Batch Make(Mesh mesh)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh };
            AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _material, CustomAabb = worldBox, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            return new Batch { Mesh = mm };
        }
        _bars = Make(Quad());
        _fills = Make(Quad());
        _shots = Make(new BoxMesh { Size = Vector3.One });
        _rings = Make(SelectionRenderer.Ring(0.32f, 0.42f, 24));
        _ready = Make(SelectionRenderer.Ring(0.47f, 0.55f, 32)); // outside the selection ring, so both show
    }

    /// <summary>Rebuilds everything for this frame; <paramref name="alpha"/> is how far we are between ticks.</summary>
    public void UpdateFrame(float alpha, IReadOnlyList<int> selected)
    {
        _bars.Count = _fills.Count = _shots.Count = _rings.Count = _ready.Count = 0;
        _selected.Clear();
        foreach (int id in selected) _selected.Add(id);
        var basis = _camera.GlobalTransform.Basis;
        Vector3 right = basis.X.Normalized(), up = basis.Y.Normalized(), back = basis.Z.Normalized();
        float bs = ChunkMesher.BlockSize;

        var u = _sim.Units;
        var defs = _sim.Content.Units;
        var evolution = _sim.Evolution;
        bool evolves = evolution.Rules(PlayerControl.LocalPlayer) != null;
        for (int i = 0; i < u.Count; i++)
        {
            if (!_fog.Shows(i)) continue;
            float x = Mathf.Lerp(u.PrevX[i].ToFloat(), u.X[i].ToFloat(), alpha);
            float z = Mathf.Lerp(u.PrevZ[i].ToFloat(), u.Z[i].ToFloat(), alpha);
            float y = Mathf.Lerp(u.PrevY[i], u.Y[i], alpha) + ChunkRenderer.GroundDrop(x, u.Y[i], z);
            if (u.Rout[i] > 0)
                _rings.Add(Vector3.Right, Vector3.Up, Vector3.Back, new Vector3(x * bs, y * bs + 0.04f, z * bs), Routing);
            if (u.Owner[i] == PlayerControl.LocalPlayer && (u.Subdued[i] || (evolves && evolution.CanEvolve(i))))
                _ready.Add(Vector3.Right, Vector3.Up, Vector3.Back, new Vector3(x * bs, y * bs + 0.05f, z * bs), Evolvable);
            int max = _sim.MaxHpOf(i);
            if (u.Hp[i] >= max && !_selected.Contains(u.Id[i])) continue;
            Bar(new Vector3(x, y + 2.5f, z) * bs, 1.2f * bs, 0.18f * bs, (float)u.Hp[i] / Math.Max(1, max), right, up, back);
        }
        foreach (var b in _sim.Buildings.All)
        {
            if (b.MaxHealth <= 0 || b.Health >= b.MaxHealth || !_fog.Shows(b)) continue;
            var top = new Vector3(b.X + b.Def.SizeX / 2f, b.Y + b.Def.Height + 1.5f, b.Z + b.Def.SizeZ / 2f) * bs;
            Bar(top, Math.Max(2f, b.Def.SizeX * 0.8f) * bs, 0.22f * bs, (float)b.Health / b.MaxHealth, right, up, back);
        }

        long now = _sim.Tick - 1; // the tick that last ran
        foreach (var shot in _sim.Combat.Shots)
        {
            if (!_fog.Sees(shot.FromX.FloorToInt(), shot.FromZ.FloorToInt()) && !_fog.Sees(shot.ToX.FloorToInt(), shot.ToZ.FloorToInt())) continue;
            float age = now - shot.Tick + alpha;
            var from = new Vector3(shot.FromX.ToFloat(), shot.FromY + 1.3f, shot.FromZ.ToFloat()) * bs;
            var to = new Vector3(shot.ToX.ToFloat(), shot.ToY + 1f, shot.ToZ.ToFloat()) * bs;
            if (!shot.Ranged)
            {
                if (age < FlashTicks) Cube(from.Lerp(to, 0.7f), 0.45f * bs * (1 - age / FlashTicks / 2), Blow);
                continue;
            }
            float flight = shot.Splash ? BlastTicks : ArrowTicks;
            if (age < flight)
            {
                // Along a shallow arc, pointing the way it flies.
                float t = age / flight, h = 0.08f * from.DistanceTo(to);
                var p = from.Lerp(to, t) + new Vector3(0, 4 * h * t * (1 - t), 0);
                var dir = to - from + new Vector3(0, 4 * h * (1 - 2 * t), 0);
                if (shot.Splash) Cube(p, 0.5f * bs, Blast);
                else Stick(p, dir, 1.4f * bs, 0.14f * bs, Arrow);
            }
            else if (shot.Splash && age < flight + BurstTicks)
            {
                Cube(to, Mathf.Lerp(0.8f, 2.2f, (age - flight) / BurstTicks) * bs, Blast);
            }
        }

        _bars.Upload();
        _fills.Upload();
        _shots.Upload();
        _rings.Upload();
        _ready.Upload();
    }

    /// <summary>A bar of the given width and height centred at p, facing the camera, filled to f (0..1) from the left.</summary>
    private void Bar(Vector3 p, float width, float height, float f, Vector3 right, Vector3 up, Vector3 back)
    {
        f = Mathf.Clamp(f, 0, 1);
        _bars.Add(right * (width + 0.04f), up * (height + 0.04f), back, p, BarBack);
        if (f <= 0) return;
        var color = f > 0.6f ? Healthy : f > 0.3f ? Hurt : Dying;
        var fill = p - right * (width * (1 - f) / 2) + back * 0.01f;
        _fills.Add(right * (width * f), up * height, back, fill, color);
    }

    private void Cube(Vector3 p, float size, Color c) =>
        _shots.Add(Vector3.Right * size, Vector3.Up * size, Vector3.Back * size, p, c);

    /// <summary>A thin box of the given length along dir, centred at p.</summary>
    private void Stick(Vector3 p, Vector3 dir, float length, float thickness, Color c)
    {
        var d = dir.LengthSquared() > 1e-8f ? dir.Normalized() : Vector3.Forward;
        var side = d.Cross(Vector3.Up);
        side = side.LengthSquared() > 1e-6f ? side.Normalized() : Vector3.Right;
        var up = side.Cross(d).Normalized();
        _shots.Add(side * thickness, up * thickness, d * length, p, c);
    }

    /// <summary>A 1 by 1 square in the XY plane, centred on the origin and facing +Z.</summary>
    private static ArrayMesh Quad()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        st.SetNormal(Vector3.Back);
        var a = new Vector3(-0.5f, -0.5f, 0);
        var b = new Vector3(0.5f, -0.5f, 0);
        var c = new Vector3(0.5f, 0.5f, 0);
        var d = new Vector3(-0.5f, 0.5f, 0);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
        st.AddVertex(a); st.AddVertex(c); st.AddVertex(d);
        return st.Commit();
    }
}
