using System;
using System.Collections.Generic;
using Godot;

namespace Rts.Game.Detail;

/// <summary>What a part of a detailed model does as the unit moves.</summary>
public enum Role
{
    /// <summary>A leg swinging forward as the walk cycle starts (left leg; a four-legged beast's front left and hind right).</summary>
    LegA,
    /// <summary>A leg swinging the other way.</summary>
    LegB,
    /// <summary>The body: bobs as it walks and leans into the stride or the blow. Exactly one per model.</summary>
    Body,
    /// <summary>The head: carried by the body, nodding against its lean; it bites when the model has no weapon arm.</summary>
    Head,
    /// <summary>The free arm, swinging against the legs.</summary>
    ArmA,
    /// <summary>The weapon arm: swings as it walks, strikes as it fights.</summary>
    ArmB,
    /// <summary>Carried by the body without moving of its own: a tail, a rider.</summary>
    Fixed,
}

/// <summary>
/// A detailed model: its parts, what each does and the point it turns about (sculpt units, see
/// <see cref="Sculpt"/>), how it walks, and how to build its parts at a given voxel size.
/// </summary>
public sealed class RigDef
{
    public required Role[] Roles;
    public required Vector3[] Pivots;
    /// <summary>The parts' voxels at a step (sculpt units per voxel: 1, 2 or 4).</summary>
    public required Func<int, FineGrid[]> Build;
    /// <summary>Blocks walked per stride.</summary>
    public float StrideBlocks = 1.6f;
    public float LegSwing = 0.6f, ArmSwing = 0.35f, Lean = 1f;
    /// <summary>How far (sculpt units) the body bobs at each step.</summary>
    public float Bob = 2.4f;
    /// <summary>How hard the weapon arm (or the head) swings in a blow.</summary>
    public float Blow = 1f;
    /// <summary>Whether far out it is drawn in voxels four units across, for a model with no plain one (<see cref="UnitModels"/>).</summary>
    public bool Far;
}

/// <summary>
/// Detailed, animated units, built from sculpts (<see cref="Sculpt"/>) split into rigid parts (legs, body,
/// head, arms; a tail, a rider) that swing as the unit walks or fights. Each part of each model at each level
/// of detail is one MultiMesh, so a crowd of any size is still a handful of draw calls; animating is only
/// writing a transform per part per unit each frame.
///
/// Levels of detail, by distance from the camera: within <see cref="NearDistance"/> voxels a thirty-second of a
/// block across (1.6 cm); within <see cref="Distance"/> voxels twice that size, which look the same from
/// there; further out the plain model (<see cref="UnitRenderer"/>, an eighth of a block, not animated), or for
/// a model that has none, its own sculpt in voxels four times the finest size. The first detailed Settlers
/// (worker, spearman, archer) were drawn by hand in voxels a sixteenth of a block across and have only that
/// level, out to <see cref="Distance"/>.
/// </summary>
public partial class DetailedUnits : Node3D
{
    public const int PerBlock = 16;
    public const float VoxelSize = ChunkMesher.BlockSize / PerBlock;

    /// <summary>Whether detailed units are drawn at all (--detail=off, or F7 in a match, turns them off).</summary>
    public static bool Enabled = true;

    /// <summary>How far from the camera (metres) units are drawn detailed.</summary>
    public static float Distance = 40f;

    /// <summary>How far from the camera (metres) sculpted units are drawn in their finest voxels (--detail-near=M).</summary>
    public static float NearDistance = 16f;

    /// <summary>Sculpted models are drawn only at the middle level (--detail-fine=off), to measure what the finest costs.</summary>
    public static bool Fine = true;

    private const int FloatsPerInstance = 20;

    // The hand-drawn Settlers' pivots (their voxels): hips, waist, neck and shoulders.
    private static readonly Vector3[] PersonPivot =
    {
        new(-2, 11, 0), new(2, 11, 0), new(0, 11, 0), new(0, 21, 0), new(-6.5f, 20, 0), new(6.5f, 20, 0),
    };
    private static readonly Role[] PersonRoles = { Role.LegA, Role.LegB, Role.Body, Role.Head, Role.ArmA, Role.ArmB };

    private sealed class Batch
    {
        public required MultiMesh Mesh;
        public float[] Buffer = new float[16 * FloatsPerInstance];
        public int Count;
    }

    /// <summary>One level of detail of a model: a batch per part, and the furthest it is drawn (metres; infinity for the last).</summary>
    private sealed class Level
    {
        public required Batch[] Parts;
        public required ArrayMesh[] Meshes;
        /// <summary>
        /// For the finest level: the same parts at the middle level, drawn only into shadows. Shadows are drawn
        /// several times a frame (once per cascade), and from the middle level they look the same at a quarter of
        /// the triangles.
        /// </summary>
        public Batch[]? Shadow;
        public float Reach;
        public bool Near;
    }

    private sealed class Rig
    {
        public required Role[] Roles;
        /// <summary>Pivots in metres.</summary>
        public required Vector3[] Pivots;
        public required Level[] Levels;
        public int Body;
        public bool HasWeaponArm;
        public float StrideMetres, LegSwing, ArmSwing, Lean, Bob, Blow;
    }

    private readonly Dictionary<string, Rig?> _rigs = new();
    private ShaderMaterial _material = null!;
    private Aabb _box;

    /// <summary>Triangles of one detailed unit of each model (at each level, finest first), for the benchmark report.</summary>
    public static readonly Dictionary<string, string> Triangles = new();

    /// <summary>Units drawn detailed this frame, and of those, how many in the finest voxels.</summary>
    public int Drawn { get; private set; }
    public int DrawnFine { get; private set; }

    public ShaderMaterial Material => _material;

    public static bool Has(string model) => Array.IndexOf(Models, model) >= 0 || Sculpted.Rigs.ContainsKey(model) || Sculpted.SameAs.ContainsKey(model);

    public void Init(Aabb worldBox)
    {
        _box = worldBox;
        _material = MaterialFor(VoxelSize);
        // Build every detailed model now, while the match loads, rather than on the frame one first shows.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var model in Models) RigOf(model);
        foreach (var model in Sculpted.Rigs.Keys) RigOf(model);
        BuildMs = timer.Elapsed.TotalMilliseconds;
    }

    /// <summary>The hand-drawn models that have a detailed version (more come from <see cref="Sculpted"/>).</summary>
    public static readonly string[] Models = { "worker", "spearman", "archer" };

    /// <summary>How long building the detailed models took.</summary>
    public static double BuildMs { get; private set; }

    // One material per voxel size: the shader shades each voxel a little on its own, so it has to know their size.
    private readonly Dictionary<float, ShaderMaterial> _materials = new();

    private ShaderMaterial MaterialFor(float voxelSize)
    {
        if (!_materials.TryGetValue(voxelSize, out var m))
            _materials[voxelSize] = m = DetailShader.Make(1f / voxelSize);
        return m;
    }

    /// <summary>Hides everything above <paramref name="y"/> metres, as the height slice does.</summary>
    public void SetSlice(float y)
    {
        foreach (var m in _materials.Values) m.SetShaderParameter("slice_y", y);
    }

    private MultiMeshInstance3D Instance(ArrayMesh mesh, ShaderMaterial material, out MultiMesh mm,
        GeometryInstance3D.ShadowCastingSetting shadows = GeometryInstance3D.ShadowCastingSetting.On)
    {
        mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = 16,
        };
        var node = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material, CustomAabb = _box, CastShadow = shadows };
        AddChild(node);
        return node;
    }

    private Level MakeLevel(FineGrid[] grids, float voxelSize, float reach, bool near, List<int> tris)
    {
        var parts = new Batch[grids.Length];
        var meshes = new ArrayMesh[grids.Length];
        int t = 0;
        for (int p = 0; p < grids.Length; p++)
        {
            var (mesh, n) = FineModel.Build(grids[p], voxelSize, ao: true);
            t += n;
            meshes[p] = mesh;
            // The finest level casts its shadows from the middle level's parts (see Level.Shadow).
            Instance(mesh, MaterialFor(voxelSize), out var mm, near ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On);
            parts[p] = new Batch { Mesh = mm };
        }
        tris.Add(t);
        return new Level { Parts = parts, Meshes = meshes, Reach = reach, Near = near };
    }

    /// <summary>Batches drawing <paramref name="from"/>'s parts into shadows only.</summary>
    private Batch[] ShadowsOf(Level from, float voxelSize)
    {
        var parts = new Batch[from.Meshes.Length];
        for (int p = 0; p < parts.Length; p++)
        {
            Instance(from.Meshes[p], MaterialFor(voxelSize), out var mm, GeometryInstance3D.ShadowCastingSetting.ShadowsOnly);
            parts[p] = new Batch { Mesh = mm };
        }
        return parts;
    }

    private Rig? RigOf(string model)
    {
        if (_rigs.TryGetValue(model, out var rig)) return rig;
        if (Sculpted.SameAs.TryGetValue(model, out var same)) return _rigs[model] = RigOf(same);
        var tris = new List<int>();
        if (Sculpted.Rigs.TryGetValue(model, out var def))
        {
            var levels = new List<Level>
            {
                MakeLevel(def.Build(1), Sculpt.UnitSize, float.NaN, true, tris),
                MakeLevel(def.Build(2), Sculpt.UnitSize * 2, float.NaN, false, tris),
            };
            levels[0].Shadow = ShadowsOf(levels[1], Sculpt.UnitSize * 2);
            if (def.Far) levels.Add(MakeLevel(def.Build(4), Sculpt.UnitSize * 4, float.PositiveInfinity, false, tris));
            var pivots = new Vector3[def.Pivots.Length];
            for (int p = 0; p < pivots.Length; p++) pivots[p] = def.Pivots[p] * Sculpt.UnitSize;
            rig = new Rig
            {
                Roles = def.Roles, Pivots = pivots, Levels = levels.ToArray(),
                StrideMetres = def.StrideBlocks * ChunkMesher.BlockSize, LegSwing = def.LegSwing, ArmSwing = def.ArmSwing,
                Lean = def.Lean, Bob = def.Bob * Sculpt.UnitSize, Blow = def.Blow,
            };
        }
        else if (Hand(model) is { } grids)
        {
            var pivots = new Vector3[PersonPivot.Length];
            for (int p = 0; p < pivots.Length; p++) pivots[p] = PersonPivot[p] * VoxelSize;
            rig = new Rig
            {
                Roles = PersonRoles, Pivots = pivots, Levels = new[] { MakeLevel(grids, VoxelSize, float.NaN, false, tris) },
                StrideMetres = 1.6f * ChunkMesher.BlockSize, LegSwing = 0.6f, ArmSwing = 0.35f, Lean = 1f, Bob = VoxelSize, Blow = 1f,
            };
        }
        if (rig != null)
        {
            rig.Body = Array.IndexOf(rig.Roles, Role.Body);
            rig.HasWeaponArm = Array.IndexOf(rig.Roles, Role.ArmB) >= 0;
            Triangles[model] = string.Join("/", tris);
        }
        return _rigs[model] = rig;
    }

    /// <summary>
    /// Whether a unit of <paramref name="model"/> this far (squared, metres) from the camera is drawn detailed:
    /// the plain model is drawn otherwise.
    /// </summary>
    public bool Covers(string model, float distanceSquared) =>
        RigOf(model) is { } rig && LevelAt(rig, distanceSquared) != null;

    private static Level? LevelAt(Rig rig, float d2)
    {
        foreach (var level in rig.Levels)
        {
            if (level.Near && !Fine) continue;
            float reach = float.IsNaN(level.Reach) ? (level.Near ? NearDistance : Distance) : level.Reach;
            if (d2 <= reach * reach) return level;
        }
        return null;
    }

    public void BeginFrame()
    {
        Drawn = 0;
        DrawnFine = 0;
        foreach (var rig in _rigs.Values)
            if (rig != null)
                foreach (var level in rig.Levels)
                {
                    foreach (var b in level.Parts) b.Count = 0;
                    if (level.Shadow != null)
                        foreach (var b in level.Shadow) b.Count = 0;
                }
    }

    /// <summary>Blocks walked per stride by a model (for the walk cycle), or the usual 1.6.</summary>
    public float StrideBlocks(string model) => RigOf(model) is { } rig ? rig.StrideMetres / ChunkMesher.BlockSize : 1.6f;

    /// <summary>
    /// Draws one unit of <paramref name="model"/> standing at <paramref name="pos"/> (metres) facing
    /// <paramref name="yaw"/>, <paramref name="distanceSquared"/> (metres, squared) from the camera.
    /// <paramref name="walk"/> is the walk cycle's phase (radians) and <paramref name="stride"/> how much of a
    /// stride it takes (0 standing, 1 walking); <paramref name="strike"/> is how far it is from one blow to the
    /// next (0..1, the hit landing at 0; negative when not fighting).
    /// <paramref name="time"/> drives breathing so a crowd standing still is not frozen.
    /// Returns false if the model has no detailed version at that distance.
    /// </summary>
    public bool Add(string model, Vector3 pos, float distanceSquared, float yaw, Color team, float walk, float stride, float strike, float time, int id)
    {
        if (RigOf(model) is not { } rig || LevelAt(rig, distanceSquared) is not { } level) return false;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Drawn++;
        if (level.Near) DrawnFine++;
        float swing = Mathf.Sin(walk) * stride;
        float bob = (Mathf.Abs(Mathf.Cos(walk)) * stride * 1.2f + Mathf.Sin(time * 2.1f + id * 1.3f) * 0.25f * (1 - stride)) * rig.Bob;
        // A blow, timed by the unit's attack cooldown: a quick thrust as the hit lands, then the arm comes
        // back to rest and winds up for the next one. strike < 0: not fighting.
        float blow = 0;
        if (strike >= 0)
        {
            if (strike < 0.3f) blow = 0.5f - 1.9f * Mathf.Sin(strike / 0.3f * Mathf.Pi);
            else if (strike < 0.6f) blow = 0.5f * (1 - Mathf.SmoothStep(0.3f, 0.6f, strike));
            else blow = 0.5f * Mathf.SmoothStep(0.6f, 1f, strike);
            blow *= rig.Blow;
        }
        float lean = (strike < 0 ? stride * 0.08f : -blow * 0.06f) * rig.Lean;
        float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
        // Every joint turns about the X axis, so each part is one turn about X (its own angle, plus the body's
        // lean for what the body carries) and an offset in the Y-Z plane, then the unit's yaw and position.
        // Worked out by hand: Godot's Transform3D maths is slow enough to matter for thousands of parts.
        var waist = rig.Pivots[rig.Body];
        float wy = waist.Y, wz = waist.Z;
        float cl = Mathf.Cos(lean), sl = Mathf.Sin(lean);
        for (int p = 0; p < rig.Roles.Length; p++)
        {
            var role = rig.Roles[p];
            float angle = role switch
            {
                Role.LegA => swing * rig.LegSwing,
                Role.LegB => -swing * rig.LegSwing,
                Role.Body => lean,
                Role.Head => rig.HasWeaponArm ? lean * -0.5f : lean * -0.5f + blow * 0.35f,
                Role.ArmA => -swing * rig.ArmSwing,
                Role.ArmB => strike >= 0 ? blow : swing * rig.ArmSwing,
                _ => 0f,
            };
            float py = rig.Pivots[p].Y, pz = rig.Pivots[p].Z;
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            // Turn about the pivot: v -> Ra (v - P) + P, so the offset is P - Ra P.
            float oy = py - (ca * py - sa * pz), oz = pz - (sa * py + ca * pz);
            float theta = angle;
            if (role is not (Role.LegA or Role.LegB))
            {
                // Everything above the legs bobs; what the body carries also leans with it round the waist.
                oy += bob;
                if (role != Role.Body)
                {
                    float ry = oy - wy, rz = oz - wz;
                    oy = cl * ry - sl * rz + wy;
                    oz = sl * ry + cl * rz + wz;
                    theta += lean;
                }
            }
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            // Basis = Ryaw * Rx(theta); origin = Ryaw * (0, oy, oz) + pos.
            Write(level.Parts[p], cy, sy * s, sy * c, sy * oz + pos.X,
                          0, c, -s, oy + pos.Y,
                          -sy, cy * s, cy * c, cy * oz + pos.Z, team);
            if (level.Shadow != null)
                Write(level.Shadow[p], cy, sy * s, sy * c, sy * oz + pos.X,
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
            foreach (var level in rig.Levels)
            {
                Upload(level.Parts);
                if (level.Shadow != null) Upload(level.Shadow);
            }
        }
        UploadMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private static void Upload(Batch[] parts)
    {
        foreach (var b in parts)
        {
            // Only hand over what changed: a level nobody is drawn at stays empty.
            if (b.Count == 0 && b.Mesh.VisibleInstanceCount == 0) continue;
            RenderingServer.MultimeshSetBuffer(b.Mesh.GetRid(), b.Buffer);
            b.Mesh.VisibleInstanceCount = b.Count;
        }
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
    private static FineGrid[]? Hand(string model) => model switch
    {
        "spearman" => Spearman(),
        "worker" => Worker(),
        "archer" => Archer(),
        _ => null,
    };

    // The hand-drawn models' six parts.
    private enum Part { LegL, LegR, Body, Head, ArmL, ArmR }
    private const int Parts = 6;

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
