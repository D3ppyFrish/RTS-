using System;
using System.Collections.Generic;
using Godot;

namespace Rts.Game.Detail;

/// <summary>
/// Prototype of detailed, animated units: voxels a sixteenth of a block across (3.1 cm, half the size of
/// <see cref="UnitModels"/>' voxels), split into six rigid parts (two legs, body, head, two arms) that swing
/// as the unit walks or fights. Each part of each model is one MultiMesh, so a crowd of any size is still a
/// handful of draw calls; animating is only writing a transform per part per unit each frame.
///
/// Only units near the camera are drawn this way (<see cref="Distance"/>); further out they keep their
/// plain model (<see cref="UnitRenderer"/>), which looks the same from there and is far cheaper.
/// </summary>
public partial class DetailedUnits : Node3D
{
    public const int PerBlock = 16;
    public const float VoxelSize = ChunkMesher.BlockSize / PerBlock;

    /// <summary>Whether detailed units are drawn at all (--detail=off, or F7 in a match, turns them off).</summary>
    public static bool Enabled = true;

    /// <summary>How far from the camera (metres) units are drawn detailed.</summary>
    public static float Distance = 40f;

    private const int FloatsPerInstance = 20;

    private enum Part { LegL, LegR, Body, Head, ArmL, ArmR }
    private const int Parts = 6;

    // Pivots (voxels): hips, waist, neck and shoulders.
    private static readonly Vector3[] Pivot =
    {
        new(-2, 11, 0), new(2, 11, 0), new(0, 11, 0), new(0, 21, 0), new(-6.5f, 20, 0), new(6.5f, 20, 0),
    };

    private sealed class Batch
    {
        public required MultiMesh Mesh;
        public float[] Buffer = new float[16 * FloatsPerInstance];
        public int Count;
    }

    private readonly Dictionary<string, Batch[]> _rigs = new();
    private ShaderMaterial _material = null!;
    private Aabb _box;

    /// <summary>Triangles of one detailed unit of each model, for the benchmark report.</summary>
    public static readonly Dictionary<string, int> Triangles = new();

    /// <summary>Units drawn detailed this frame.</summary>
    public int Drawn { get; private set; }

    public ShaderMaterial Material => _material;

    public static bool Has(string model) => Array.IndexOf(Models, model) >= 0;

    public void Init(Aabb worldBox)
    {
        _box = worldBox;
        _material = DetailShader.Make(PerBlock / ChunkMesher.BlockSize);
        // Build every detailed model now, while the match loads, rather than on the frame one first shows.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var model in Models) BatchesOf(model);
        BuildMs = timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>The models that have a detailed version.</summary>
    public static readonly string[] Models = { "worker", "spearman", "archer" };

    /// <summary>How long building the detailed models took.</summary>
    public static double BuildMs { get; private set; }

    private Batch[]? BatchesOf(string model)
    {
        if (_rigs.TryGetValue(model, out var b)) return b;
        var grids = Rig(model);
        if (grids == null)
        {
            _rigs[model] = null!;
            return null;
        }
        b = new Batch[Parts];
        int tris = 0;
        for (int p = 0; p < Parts; p++)
        {
            var (mesh, t) = FineModel.Build(grids[p], VoxelSize);
            tris += t;
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = 16,
            };
            AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = _material, CustomAabb = _box });
            b[p] = new Batch { Mesh = mm };
        }
        Triangles[model] = tris;
        return _rigs[model] = b;
    }

    public void BeginFrame()
    {
        Drawn = 0;
        foreach (var rig in _rigs.Values)
            if (rig != null)
                foreach (var b in rig) b.Count = 0;
    }

    /// <summary>
    /// Draws one unit of <paramref name="model"/> standing at <paramref name="pos"/> (metres) facing
    /// <paramref name="yaw"/>. <paramref name="walk"/> is the walk cycle's phase (radians) and
    /// <paramref name="stride"/> how much of a stride it takes (0 standing, 1 walking);
    /// <paramref name="strike"/> is how far it is from one blow to the next (0..1, the hit landing at 0;
    /// negative when not fighting).
    /// <paramref name="time"/> drives breathing so a crowd standing still is not frozen.
    /// Returns false if the model has no detailed version.
    /// </summary>
    public bool Add(string model, Vector3 pos, float yaw, Color team, float walk, float stride, float strike, float time, int id)
    {
        var rig = BatchesOf(model);
        if (rig == null) return false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Drawn++;
        float swing = Mathf.Sin(walk) * stride;
        float bob = Mathf.Abs(Mathf.Cos(walk)) * stride * 1.2f + Mathf.Sin(time * 2.1f + id * 1.3f) * 0.25f * (1 - stride);
        // A blow, timed by the unit's attack cooldown: a quick thrust as the hit lands, then the arm comes
        // back to rest and winds up for the next one. strike < 0: not fighting.
        float blow = 0;
        if (strike >= 0)
        {
            if (strike < 0.3f) blow = 0.5f - 1.9f * Mathf.Sin(strike / 0.3f * Mathf.Pi);
            else if (strike < 0.6f) blow = 0.5f * (1 - Mathf.SmoothStep(0.3f, 0.6f, strike));
            else blow = 0.5f * Mathf.SmoothStep(0.6f, 1f, strike);
        }
        float lean = strike < 0 ? stride * 0.08f : -blow * 0.06f;
        float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
        Span<float> angle = stackalloc float[Parts];
        angle[(int)Part.LegL] = swing * 0.6f;
        angle[(int)Part.LegR] = -swing * 0.6f;
        angle[(int)Part.Body] = lean;
        angle[(int)Part.Head] = lean * -0.5f;
        angle[(int)Part.ArmL] = -swing * 0.35f;
        angle[(int)Part.ArmR] = strike >= 0 ? blow : swing * 0.35f;
        // Every joint turns about the X axis, so each part is one turn about X (its own angle, plus the body's
        // lean for the head and arms) and an offset in the Y-Z plane, then the unit's yaw and position.
        // Worked out by hand: Godot's Transform3D maths is slow enough to matter for thousands of parts.
        float wy = Pivot[(int)Part.Body].Y * VoxelSize, wz = Pivot[(int)Part.Body].Z * VoxelSize;
        float cl = Mathf.Cos(lean), sl = Mathf.Sin(lean);
        for (int p = 0; p < Parts; p++)
        {
            float py = Pivot[p].Y * VoxelSize, pz = Pivot[p].Z * VoxelSize;
            float ca = Mathf.Cos(angle[p]), sa = Mathf.Sin(angle[p]);
            // Turn about the pivot: v -> Ra (v - P) + P, so the offset is P - Ra P.
            float oy = py - (ca * py - sa * pz), oz = pz - (sa * py + ca * pz);
            float theta = angle[p];
            if (p >= (int)Part.Body)
            {
                // Everything above the hips bobs; the head and arms also lean with the body round the waist.
                oy += bob * VoxelSize;
                if (p != (int)Part.Body)
                {
                    float ry = oy - wy, rz = oz - wz;
                    oy = cl * ry - sl * rz + wy;
                    oz = sl * ry + cl * rz + wz;
                    theta += lean;
                }
            }
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            // Basis = Ryaw * Rx(theta); origin = Ryaw * (0, oy, oz) + pos.
            Write(rig[p], cy, sy * s, sy * c, sy * oz + pos.X,
                          0, c, -s, oy + pos.Y,
                          -sy, cy * s, cy * c, cy * oz + pos.Z, team);
        }
        PlaceMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return true;
    }

    /// <summary>Total milliseconds spent placing parts (Add) and handing them to the renderer (EndFrame).</summary>
    public double PlaceMs, UploadMs;
    public int Frames;

    public void EndFrame()
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Frames++;
        foreach (var rig in _rigs.Values)
        {
            if (rig == null) continue;
            foreach (var b in rig)
            {
                RenderingServer.MultimeshSetBuffer(b.Mesh.GetRid(), b.Buffer);
                b.Mesh.VisibleInstanceCount = b.Count;
            }
        }
        UploadMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private static void Write(Batch b, float m00, float m01, float m02, float ox, float m10, float m11, float m12, float oy,
        float m20, float m21, float m22, float oz, Color tint)
    {
        // Capacity from our own buffer: asking the MultiMesh is a call into the engine, costly per part.
        if ((b.Count + 1) * FloatsPerInstance > b.Buffer.Length)
        {
            int cap = b.Buffer.Length / FloatsPerInstance * 2;
            b.Mesh.InstanceCount = cap;
            Array.Resize(ref b.Buffer, cap * FloatsPerInstance);
        }
        int o = b.Count++ * FloatsPerInstance;
        var buf = b.Buffer;
        buf[o + 0] = m00; buf[o + 1] = m01; buf[o + 2] = m02; buf[o + 3] = ox;
        buf[o + 4] = m10; buf[o + 5] = m11; buf[o + 6] = m12; buf[o + 7] = oy;
        buf[o + 8] = m20; buf[o + 9] = m21; buf[o + 10] = m22; buf[o + 11] = oz;
        buf[o + 12] = 1; buf[o + 13] = 1; buf[o + 14] = 1; buf[o + 15] = 1;
        buf[o + 16] = tint.R; buf[o + 17] = tint.G; buf[o + 18] = tint.B; buf[o + 19] = 1;
    }

    // ---- Models -------------------------------------------------------------------------------------

    private static FineMaterial M(float r, float g, float b, Surface s = Surface.Plain) => new(new Color(r, g, b), s);

    private static readonly FineMaterial Skin = M(0.88f, 0.68f, 0.52f), SkinDark = M(0.74f, 0.54f, 0.4f);
    private static readonly FineMaterial Hair = M(0.36f, 0.22f, 0.12f), Eye = M(0.1f, 0.08f, 0.08f), EyeWhite = M(0.95f, 0.95f, 0.92f);
    private static readonly FineMaterial Mouth = M(0.55f, 0.3f, 0.26f), Beard = M(0.42f, 0.27f, 0.15f);
    private static readonly FineMaterial Boot = M(0.24f, 0.16f, 0.1f), BootSole = M(0.12f, 0.09f, 0.07f);
    private static readonly FineMaterial Pants = M(0.36f, 0.3f, 0.22f), PantsDark = M(0.28f, 0.23f, 0.17f);
    private static readonly FineMaterial Leather = M(0.45f, 0.29f, 0.15f), LeatherDark = M(0.32f, 0.2f, 0.1f);
    private static readonly FineMaterial Mail = M(0.55f, 0.57f, 0.6f, Surface.Metal), MailDark = M(0.4f, 0.42f, 0.45f, Surface.Metal);
    private static readonly FineMaterial Steel = M(0.78f, 0.8f, 0.84f, Surface.Metal), SteelDark = M(0.5f, 0.52f, 0.56f, Surface.Metal);
    private static readonly FineMaterial Gold = M(0.95f, 0.75f, 0.25f, Surface.Metal);
    private static readonly FineMaterial Wood = M(0.52f, 0.34f, 0.17f), WoodDark = M(0.38f, 0.24f, 0.12f);
    private static readonly FineMaterial Cloth = M(0.86f, 0.82f, 0.72f), Rope = M(0.7f, 0.6f, 0.4f);
    private static readonly FineMaterial Feather = M(0.95f, 0.95f, 0.9f);
    private static readonly FineMaterial Team = new(new Color(1f, 0, 0, 0)), TeamDark = new(new Color(0.68f, 0, 0, 0)), TeamDeep = new(new Color(0.48f, 0, 0, 0));

    /// <summary>The six parts of a detailed model, or null if it has none. Models face +Z, feet at y = 0.</summary>
    private static FineGrid[]? Rig(string model) => model switch
    {
        "spearman" => Spearman(),
        "worker" => Worker(),
        "archer" => Archer(),
        _ => null,
    };

    /// <summary>A grid big enough for any part (with gear held out), centred on x and z.</summary>
    private static FineGrid Grid() => new(-14, -2, -12, 14, 50, 14);

    /// <summary>
    /// The body every detailed person shares, 28 voxels (0.9 m) tall: booted legs, a belted tunic in the
    /// owner's colour, sleeves, hands, and a head with eyes, brows, nose, mouth and hair.
    /// </summary>
    private static FineGrid[] Person(FineMaterial tunic, FineMaterial sleeve, bool beard = false)
    {
        var p = new FineGrid[Parts];
        for (int i = 0; i < Parts; i++) p[i] = Grid();
        // Legs: x -4..-1 and 0..3, z -2..1, hips at 11.
        foreach (var (leg, x0) in new[] { (p[(int)Part.LegL], -4), (p[(int)Part.LegR], 0) })
        {
            leg.Box(x0, 0, -2, x0 + 3, 10, 1, Pants)
               .Box(x0, 6, -2, x0, 10, 1, PantsDark)                        // inner shadow seam
               .Box(x0, 0, -2, x0 + 3, 4, 2, Boot).Box(x0, 0, -2, x0 + 3, 0, 2, BootSole)
               .Box(x0, 4, -2, x0 + 3, 4, 1, LeatherDark);                  // boot cuff
        }
        // Body: x -5..4, z -3..2, waist at 11 up to the neck at 20.
        p[(int)Part.Body].Box(-5, 11, -3, 4, 20, 2, tunic)
            .Box(-5, 8, -3, 4, 11, 2, tunic)                                 // skirt of the tunic over the hips
            .Box(-5, 8, -3, 4, 8, 2, TeamDeep)                               // its hem
            .Box(-5, 12, -3, 4, 13, 2, Leather).Box(-1, 12, 3, 0, 13, 3, Gold) // belt and buckle
            .Box(-2, 18, 3, 1, 20, 3, sleeve).Box(-1, 19, 3, 0, 20, 3, Skin)  // collar opening
            .Box(-4, 20, -2, 3, 20, 1, sleeve);                              // shoulders
        // Head: x -4..3, z -4..3, neck at 21.
        var h = p[(int)Part.Head];
        h.Box(-2, 21, -2, 1, 21, 1, SkinDark)                                // neck
         .Box(-4, 22, -4, 3, 29, 3, Skin)
         .Box(-4, 28, -4, 3, 30, 3, Hair).Box(-4, 23, -4, 3, 29, -4, Hair)   // hair on top and at the back
         .Box(-4, 25, -3, -4, 29, 1, Hair).Box(3, 25, -3, 3, 29, 1, Hair)    // sides
         .Box(-3, 26, 4, -2, 26, 4, EyeWhite).Box(1, 26, 4, 2, 26, 4, EyeWhite)
         .Box(-2, 26, 4, -2, 26, 4, Eye).Box(1, 26, 4, 1, 26, 4, Eye)        // eyes looking ahead
         .Box(-3, 27, 4, -2, 27, 4, Hair).Box(1, 27, 4, 2, 27, 4, Hair)      // brows
         .Box(-1, 24, 4, 0, 25, 5, SkinDark)                                 // nose
         .Box(-1, 23, 4, 0, 23, 4, Mouth)
         .Box(-5, 25, -1, -5, 26, 0, SkinDark).Box(4, 25, -1, 4, 26, 0, SkinDark); // ears
        if (beard) h.Box(-4, 22, 3, 3, 24, 4, Beard).Box(-1, 23, 4, 0, 23, 4, Mouth).Box(-4, 22, -1, -4, 25, 3, Beard).Box(3, 22, -1, 3, 25, 3, Beard);
        // Arms: x -8..-5 and 5..8, z -2..1, shoulders at 20, hands at 9..11.
        foreach (var (arm, x0) in new[] { (p[(int)Part.ArmL], -8), (p[(int)Part.ArmR], 5) })
        {
            arm.Box(x0, 12, -2, x0 + 3, 20, 1, sleeve)
               .Box(x0, 12, -2, x0 + 3, 13, 1, LeatherDark)                  // glove cuff
               .Box(x0, 9, -2, x0 + 3, 11, 1, Skin)                          // hand
               .Box(x0, 9, 1, x0 + 3, 9, 1, SkinDark);
        }
        return p;
    }

    /// <summary>A spearman: mail under a tabard, a nasal helm with a crest, a cape, a long spear and a kite shield.</summary>
    private static FineGrid[] Spearman()
    {
        var p = Person(Team, Mail);
        var body = p[(int)Part.Body];
        body.Box(-5, 11, -3, 4, 20, 2, Mail)                                 // mail shirt
            .Box(-4, 9, 3, 3, 19, 3, Team).Box(-4, 9, 3, 3, 9, 3, Gold)      // tabard with a gold hem
            .Box(-1, 11, 3, 0, 18, 3, Gold).Box(-3, 15, 3, 2, 16, 3, Gold)   // and a cross
            .Box(-4, 9, -3, 3, 19, -3, Team)                                 // tabard at the back
            .Box(-5, 12, -3, 4, 13, 2, Leather).Box(-1, 12, 3, 0, 13, 3, Leather)
            .Box(-6, 3, -5, 5, 20, -5, TeamDark).Box(-6, 19, -5, 5, 20, -4, Team) // cape from the shoulders
            .Box(-6, 3, -5, 5, 3, -5, TeamDeep);
        var head = p[(int)Part.Head];
        head.Box(-5, 27, -5, 4, 31, 4, Steel).Box(-5, 27, -5, 4, 27, 4, SteelDark) // helm with a brow band
            .Box(-4, 32, -4, 3, 32, 3, Steel).Box(-3, 33, -3, 2, 33, 2, SteelDark)
            .Box(-1, 23, 5, 0, 27, 5, Steel)                                 // nasal guard
            .Box(-5, 22, -5, -5, 27, 0, MailDark).Box(4, 22, -5, 4, 27, 0, MailDark).Box(-5, 22, -5, 4, 27, -5, MailDark) // mail coif
            .Box(-1, 34, -5, 0, 36, 4, Team).Box(-1, 33, -6, 0, 35, -6, TeamDark); // crest
        p[(int)Part.LegL].Box(-4, 5, -2, -1, 10, 1, MailDark).Box(-4, 0, -2, -1, 4, 2, Boot).Box(-4, 0, -2, -1, 0, 2, BootSole);
        p[(int)Part.LegR].Box(0, 5, -2, 3, 10, 1, MailDark).Box(0, 0, -2, 3, 4, 2, Boot).Box(0, 0, -2, 3, 0, 2, BootSole);
        // Spear in the right hand, held upright in front of the hand.
        p[(int)Part.ArmR].Box(6, 0, 2, 7, 42, 3, Wood)
            .Box(6, 9, 2, 7, 11, 3, Leather)                                 // grip
            .Box(6, 43, 2, 7, 44, 3, SteelDark)                              // socket
            .Box(5, 45, 1, 8, 47, 4, Steel).Box(6, 48, 2, 7, 48, 3, Steel)   // blade
            .Box(5, 40, 2, 8, 40, 3, Team);                                  // pennon ring
        // Kite shield on the left arm.
        p[(int)Part.ArmL].Box(-11, 6, -5, -10, 20, 5, Team)
            .Box(-11, 6, -5, -11, 20, -5, Steel).Box(-11, 6, 5, -11, 20, 5, Steel)
            .Box(-11, 20, -5, -11, 20, 5, Steel).Box(-11, 6, -3, -11, 6, 3, Steel)
            .Box(-11, 3, -2, -10, 5, 2, Team).Box(-11, 3, -2, -11, 3, 2, Steel)
            .Box(-12, 12, -1, -12, 14, 1, Gold)                              // boss
            .Box(-11, 16, -1, -11, 18, 1, TeamDark).Box(-11, 9, -1, -11, 10, 1, TeamDark)
            .Box(-9, 10, -1, -9, 16, 0, Leather);                            // strap
        return p;
    }

    /// <summary>A worker: shirt, apron and cap, a beard, a pickaxe in hand and a satchel on the hip.</summary>
    private static FineGrid[] Worker()
    {
        var p = Person(Team, TeamDark, beard: true);
        p[(int)Part.Body].Box(-4, 8, 3, 3, 17, 3, Leather).Box(-4, 17, 3, -3, 19, 3, Leather).Box(2, 17, 3, 3, 19, 3, Leather) // apron
            .Box(-4, 14, 3, 3, 14, 3, LeatherDark)
            .Box(5, 8, -2, 6, 12, 1, LeatherDark).Box(-4, 18, -3, 4, 20, -3, Rope);  // satchel and its strap
        p[(int)Part.Head].Box(-5, 29, -5, 4, 31, 4, TeamDark).Box(-5, 29, 4, 4, 29, 6, TeamDark) // cap with a brim
            .Box(-4, 32, -4, 3, 32, 3, TeamDeep);
        // Pickaxe: haft in the right hand, head at the top.
        p[(int)Part.ArmR].Box(6, 4, 2, 7, 26, 3, Wood).Box(6, 9, 2, 7, 11, 3, WoodDark)
            .Box(6, 25, -4, 7, 27, 9, Steel).Box(6, 24, 8, 7, 24, 9, SteelDark).Box(6, 24, -4, 7, 24, -3, SteelDark);
        return p;
    }

    /// <summary>An archer: a hood, leather jerkin, a longbow in the left hand and a quiver of arrows on the back.</summary>
    private static FineGrid[] Archer()
    {
        var p = Person(Leather, TeamDark);
        p[(int)Part.Body].Box(-4, 9, 3, 3, 19, 3, Team).Box(-4, 9, -3, 3, 19, -3, Team)
            .Box(-5, 12, -3, 4, 13, 2, LeatherDark).Box(-1, 12, 3, 0, 13, 3, Gold)
            .Box(-1, 8, -6, 2, 21, -4, LeatherDark).Box(-1, 8, -6, 2, 8, -4, Leather)  // quiver
            .Box(-1, 22, -6, -1, 24, -6, Wood).Box(1, 22, -5, 1, 25, -5, Wood).Box(0, 22, -6, 0, 23, -6, Wood)
            .Box(-1, 25, -6, -1, 25, -6, Feather).Box(1, 26, -5, 1, 26, -5, Feather).Box(0, 24, -6, 0, 24, -6, Team);
        p[(int)Part.Head].Box(-5, 23, -5, 4, 31, 4, Team).Clear(-4, 22, 4, 3, 28, 4)  // hood framing the face
            .Box(-4, 31, -5, 3, 31, 2, TeamDark).Box(-3, 32, -4, 2, 32, -1, TeamDark).Box(-2, 30, -6, 1, 32, -6, TeamDark)
            .Box(-4, 27, 4, 3, 28, 4, Team);
        // Longbow held upright in the left hand, its string toward the body.
        var arm = p[(int)Part.ArmL];
        arm.Box(-8, 9, 3, -7, 11, 3, Leather)
           .Box(-8, 12, 3, -7, 19, 3, Wood).Box(-8, 20, 2, -7, 24, 2, Wood).Box(-8, 25, 1, -7, 27, 1, WoodDark)
           .Box(-8, 3, 3, -7, 8, 3, Wood).Box(-8, -1, 2, -7, 2, 2, Wood).Box(-8, -3, 1, -7, -2, 1, WoodDark)
           .Box(-8, -2, 0, -8, 26, 0, Cloth);                                // string
        return p;
    }
}
