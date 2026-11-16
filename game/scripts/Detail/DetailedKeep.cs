using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;

namespace Rts.Game.Detail;

/// <summary>
/// Prototype of a detailed building: a stone castle eleven blocks square, built from voxels a sixteenth of a
/// block across (like <see cref="DetailedUnits"/>), with a curtain wall and walkway, round corner towers (one
/// roofed), a gatehouse with a studded door and hanging banners in its owner's colour, arrow slits, lit
/// windows, ivy, and a tall keep in the courtyard with a flag. Its stones, planks and shingles are drawn by
/// the shader (<see cref="DetailShader"/>), so the mesh only carries the shape.
///
/// It is scenery for measuring and looking at (--keeps=N): it is not a building of the rules, so units walk
/// through it. Every copy shares one mesh and is drawn as one instance of a MultiMesh.
/// </summary>
public partial class DetailedKeep : Node3D
{
    public const int Blocks = 11;
    private const int N = Blocks * DetailedUnits.PerBlock; // 176 voxels across

    public static int Triangles { get; private set; }
    public static int FarTriangles { get; private set; }

    /// <summary>Whether castles further than <see cref="LodDistance"/> are drawn with their coarse mesh (--keep-lod=off turns it off).</summary>
    public static bool Lod = true;
    public static float LodDistance = 60f;

    public ShaderMaterial? Material { get; private set; }
    public static int Voxels { get; private set; }
    public static double BuildMs { get; private set; }

    public int Count { get; private set; }

    /// <summary>
    /// Places up to <paramref name="count"/> castles in a square spiral round block (fx, fz), each on the
    /// highest ground under it, skipping places where the ground is too uneven or under water. Returns their
    /// corners (in blocks).
    /// </summary>
    public List<Vector3I> Place(Simulation sim, int count, int fx, int fz, Color team)
    {
        var spots = new List<Vector3I>();
        var w = sim.World;
        int spacing = Blocks + 3;
        // Spiral out from the focus, ring by ring, so the first castle is the nearest.
        for (int ring = 0; spots.Count < count && ring < 60; ring++)
        for (int dz = -ring; dz <= ring && spots.Count < count; dz++)
        for (int dx = -ring; dx <= ring && spots.Count < count; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
            int x0 = fx + 4 + dx * spacing, z0 = fz - Blocks - 8 + dz * spacing;
            if (x0 < 1 || z0 < 1 || x0 + Blocks >= w.SizeX - 1 || z0 + Blocks >= w.SizeZ - 1) continue;
            int lo = int.MaxValue, hi = int.MinValue;
            bool wet = false;
            for (int z = z0; z < z0 + Blocks; z++)
            for (int x = x0; x < x0 + Blocks; x++)
            {
                int h = w.SurfaceHeight(x, z);
                lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                var top = w.Get(x, h, z);
                if (top == BlockType.Water) wet = true;
            }
            if (wet || hi - lo > 3) continue;
            spots.Add(new Vector3I(x0, hi + 1, z0));
        }

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var grid = Build();
        var (mesh, tris) = FineModel.Build(grid, DetailedUnits.VoxelSize);
        // The far version: a voxel for every 4x4x4 of the fine ones (a quarter of a block), which from
        // further than LodDistance looks the same. The shader still draws its stones at the fine size.
        var coarse = Downsample(grid, 4);
        var (far, farTris) = FineModel.Build(coarse, DetailedUnits.VoxelSize * 4);
        BuildMs = timer.Elapsed.TotalMilliseconds;
        Triangles = tris;
        FarTriangles = farTris;
        Voxels = grid.Count();
        var material = DetailShader.Make(DetailedUnits.PerBlock / ChunkMesher.BlockSize);
        foreach (var spot in spots)
        {
            var at = new Vector3(spot.X, spot.Y, spot.Z) * ChunkMesher.BlockSize;
            AddChild(new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = material, Position = at,
                VisibilityRangeEnd = Lod ? LodDistance : 0,
            });
            if (Lod)
                AddChild(new MeshInstance3D { Mesh = far, MaterialOverride = material, Position = at, VisibilityRangeBegin = LodDistance });
        }
        material.SetShaderParameter("team_tint", team);
        Material = material;
        Count = spots.Count;
        return spots;
    }

    /// <summary>
    /// A grid <paramref name="k"/> times coarser: each k^3 cell becomes the material most of its voxels
    /// have, or empty if fewer than half of them are filled.
    /// </summary>
    public static FineGrid Downsample(FineGrid g, int k)
    {
        int Floor(int v) => (int)MathF.Floor(v / (float)k);
        var c = new FineGrid(Floor(g.X0), Floor(g.Y0), Floor(g.Z0), Floor(g.X0 + g.NX - 1), Floor(g.Y0 + g.NY - 1), Floor(g.Z0 + g.NZ - 1));
        var votes = new int[256];
        for (int y = c.Y0; y < c.Y0 + c.NY; y++)
        for (int z = c.Z0; z < c.Z0 + c.NZ; z++)
        for (int x = c.X0; x < c.X0 + c.NX; x++)
        {
            Array.Clear(votes);
            int filled = 0;
            for (int dy = 0; dy < k; dy++)
            for (int dz = 0; dz < k; dz++)
            for (int dx = 0; dx < k; dx++)
            {
                byte v = g.Get(x * k + dx, y * k + dy, z * k + dz);
                if (v == 0) continue;
                votes[v]++;
                filled++;
            }
            if (filled * 2 < k * k * k) continue;
            int best = 1;
            for (int v = 2; v < g.Palette.Count; v++) if (votes[v] > votes[best]) best = v;
            c.Set(x, y, z, g.Palette[best]);
        }
        return c;
    }

    private static FineMaterial M(float r, float g, float b, Surface s = Surface.Plain) => new(new Color(r, g, b), s);

    private static readonly FineMaterial Stone = M(0.76f, 0.74f, 0.7f, Surface.Brick);
    private static readonly FineMaterial StoneKeep = M(0.82f, 0.8f, 0.76f, Surface.Brick);
    private static readonly FineMaterial StoneDark = M(0.6f, 0.58f, 0.55f, Surface.Brick);
    private static readonly FineMaterial StoneCap = M(0.72f, 0.7f, 0.66f);
    private static readonly FineMaterial Cobble = M(0.56f, 0.54f, 0.5f, Surface.Brick);
    private static readonly FineMaterial Shadow = M(0.1f, 0.09f, 0.09f);
    private static readonly FineMaterial Planks = M(0.5f, 0.33f, 0.18f, Surface.Planks);
    private static readonly FineMaterial PlanksDark = M(0.38f, 0.25f, 0.13f, Surface.Planks);
    private static readonly FineMaterial Beam = M(0.32f, 0.2f, 0.1f);
    private static readonly FineMaterial Roof = M(0.36f, 0.3f, 0.36f, Surface.Shingles);
    private static readonly FineMaterial RoofRed = M(0.6f, 0.24f, 0.16f, Surface.Shingles);
    private static readonly FineMaterial Iron = M(0.3f, 0.3f, 0.32f, Surface.Metal);
    private static readonly FineMaterial Gold = M(0.95f, 0.75f, 0.25f, Surface.Metal);
    private static readonly FineMaterial Window = M(1f, 0.72f, 0.35f, Surface.Glow);
    private static readonly FineMaterial Flame = M(1f, 0.55f, 0.15f, Surface.Glow);
    private static readonly FineMaterial Ivy = M(0.27f, 0.45f, 0.18f), IvyDark = M(0.2f, 0.35f, 0.14f);
    private static readonly FineMaterial Grass = M(0.36f, 0.56f, 0.24f);
    private static readonly FineMaterial Hay = M(0.85f, 0.72f, 0.38f);
    private static readonly FineMaterial Team = new(new Color(1f, 0, 0, 0)), TeamDark = new(new Color(0.7f, 0, 0, 0));

    /// <summary>A small deterministic hash for scattering ivy and loose stones the same way every time.</summary>
    private static uint Hash(int x, int y, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return h;
    }

    /// <summary>The castle's voxels: x and z 0..175, y from -32 (its footing, sunk into the ground) up.</summary>
    public static FineGrid Build()
    {
        var g = new FineGrid(-8, -32, -8, N + 7, 300, N + 7);
        const int wall0 = 16, wall1 = N - 17, wallTop = 119, thick = 14;

        // Footing: sunk two blocks into the ground so a castle on a slope does not float.
        g.Box(6, -32, 6, N - 7, 7, N - 7, StoneDark);
        g.Box(6, 7, 6, N - 7, 7, N - 7, StoneCap);

        // Courtyard floor.
        g.Box(wall0, 8, wall0, wall1, 8, wall1, Cobble);
        g.Box(wall0 + thick, 8, wall0 + thick, wall0 + thick + 20, 8, wall0 + thick + 12, Grass);

        // Curtain walls, a walkway on top and merlons along the outer edge.
        void Wall(int x0, int z0, int x1, int z1)
        {
            g.Box(x0, 8, z0, x1, wallTop, z1, Stone);
            g.Box(x0, wallTop - 1, z0, x1, wallTop, z1, StoneCap);
        }
        Wall(wall0, wall0, wall1, wall0 + thick - 1);           // back (-Z)
        Wall(wall0, wall1 - thick + 1, wall1, wall1);           // front (+Z)
        Wall(wall0, wall0, wall0 + thick - 1, wall1);           // left (-X)
        Wall(wall1 - thick + 1, wall0, wall1, wall1);           // right (+X)
        for (int k = wall0; k <= wall1; k += 14)
        {
            g.Box(k, wallTop + 1, wall0, Math.Min(k + 7, wall1), wallTop + 12, wall0 + 3, Stone);
            g.Box(k, wallTop + 1, wall1 - 3, Math.Min(k + 7, wall1), wallTop + 12, wall1, Stone);
            g.Box(wall0, wallTop + 1, k, wall0 + 3, wallTop + 12, Math.Min(k + 7, wall1), Stone);
            g.Box(wall1 - 3, wallTop + 1, k, wall1, wallTop + 12, Math.Min(k + 7, wall1), Stone);
        }
        // A string course (a ledge of stone) round the outside at a third of the height.
        g.Box(wall0 - 1, 44, wall0 - 1, wall1 + 1, 46, wall1 + 1, StoneCap);
        g.Box(wall0, 44, wall0, wall1, 46, wall1, Stone);
        g.Box(wall0 + thick, 8, wall0 + thick, wall1 - thick, 8, wall1 - thick, Cobble);

        // Arrow slits: narrow and dark, a few voxels deep.
        for (int k = wall0 + 28; k <= wall1 - 28; k += 22)
        {
            if (k > 60 && k < 116) continue; // the gatehouse is there on the front, the keep's view at the back
            foreach (int y in new[] { 64, 92 })
            {
                g.Box(k, y, wall0, k + 2, y + 13, wall0 + 3, Shadow);
                g.Box(k, y, wall1 - 3, k + 2, y + 13, wall1, Shadow);
                g.Box(wall0, y, k, wall0 + 3, y + 13, k + 2, Shadow);
                g.Box(wall1 - 3, y, k, wall1, y + 13, k + 2, Shadow);
                g.Clear(k, y, wall0, k + 2, y + 13, wall0);
                g.Clear(k, y, wall1, k + 2, y + 13, wall1);
                g.Clear(wall0, y, k, wall0, y + 13, k + 2);
                g.Clear(wall1, y, k, wall1, y + 13, k + 2);
            }
        }

        // Round towers at the corners, standing proud of the walls, crenellated; the back left one roofed.
        var corners = new[] { (wall0 + 4, wall0 + 4), (wall1 - 3, wall0 + 4), (wall0 + 4, wall1 - 3), (wall1 - 3, wall1 - 3) };
        for (int t = 0; t < 4; t++)
        {
            var (cx, cz) = corners[t];
            const int r = 22, top = 155;
            g.Cylinder(cx, cz, r + 2, 8, 20, StoneDark);              // battered foot
            g.Cylinder(cx, cz, r, 8, top, Stone);
            g.Cylinder(cx, cz, r + 1, 44, 46, StoneCap);
            g.Cylinder(cx, cz, r + 2, top - 3, top, StoneCap);        // corbelled ring
            g.Cylinder(cx, cz, r + 2, top + 1, top + 12, Stone);
            g.Cylinder(cx, cz, r - 2, top + 1, top + 12, default);    // hollow top
            g.Cylinder(cx, cz, r - 2, top, top, Planks);
            for (int a = 0; a < 16; a += 2)
            {
                // Gaps between merlons: cut every other sixteenth of the ring.
                float a0 = a * MathF.Tau / 16, a1 = (a + 1) * MathF.Tau / 16;
                for (int y = top + 6; y <= top + 12; y++)
                for (int z = cz - r - 3; z <= cz + r + 3; z++)
                for (int x = cx - r - 3; x <= cx + r + 3; x++)
                {
                    float ang = MathF.Atan2(z + 0.5f - cz, x + 0.5f - cx);
                    if (ang < 0) ang += MathF.Tau;
                    if (ang >= a0 && ang < a1) g.Set(x, y, z, 0);
                }
            }
            // Slit windows facing outwards.
            int sx = cx < N / 2 ? -1 : 1, sz = cz < N / 2 ? -1 : 1;
            foreach (int y in new[] { 72, 112 })
            {
                int wx = cx + sx * (r - 1), wz = cz + sz * (r - 1);
                g.Box(wx - 1, y, cz - 1 + sz * 6, wx + 1, y + 11, cz + 1 + sz * 6, Window);
                g.Box(cx - 1 + sx * 6, y, wz - 1, cx + 1 + sx * 6, y + 11, wz + 1, Window);
            }
            if (t == 0)
            {
                g.Cone(cx, cz, r + 6, top + 4, top + 62, RoofRed);
                g.Cylinder(cx, cz, r + 6, top + 4, top + 5, Beam);
                g.Box(cx, top + 62, cz, cx + 1, top + 90, cz + 1, Beam);               // flagpole
                g.Box(cx + 2, top + 76, cz, cx + 26, top + 89, cz + 1, Team);         // pennant
                g.Box(cx + 2, top + 76, cz, cx + 26, top + 77, cz + 1, TeamDark);
                g.Clear(cx + 18, top + 78, cz, cx + 26, top + 83, cz + 1);
            }
        }

        // Gatehouse: two square towers flanking an arched gate in the front wall.
        const int gx0 = 70, gx1 = 105, gate0 = 79, gate1 = 96;
        g.Box(gx0 - 12, 8, wall1 - 18, gx0 + 1, 141, wall1 + 8, Stone);
        g.Box(gx1 - 1, 8, wall1 - 18, gx1 + 12, 141, wall1 + 8, Stone);
        g.Box(gx0, 108, wall1 - 18, gx1, 141, wall1 + 8, Stone);              // the room over the gate
        g.Box(gx0 - 13, 128, wall1 - 19, gx1 + 13, 130, wall1 + 9, StoneCap);
        for (int k = gx0 - 13; k <= gx1 + 13; k += 10)
        {
            g.Box(k, 142, wall1 + 5, k + 5, 153, wall1 + 9, Stone);
            g.Box(k, 142, wall1 - 19, k + 5, 153, wall1 - 15, Stone);
        }
        for (int k = wall1 - 19; k <= wall1 + 9; k += 10)
        {
            g.Box(gx0 - 13, 142, k, gx0 - 9, 153, k + 5, Stone);
            g.Box(gx1 + 9, 142, k, gx1 + 13, 153, k + 5, Stone);
        }
        // The passage, with a round arch on top.
        for (int y = 8; y <= 70; y++)
        {
            float half = (gate1 - gate0 + 1) / 2f, mid = (gate0 + gate1 + 1) / 2f;
            float w = y <= 58 ? half : MathF.Sqrt(MathF.Max(0, half * half - (y - 58) * (y - 58) * 1.4f));
            if (w < 1) continue;
            g.Clear((int)MathF.Ceiling(mid - w), y, wall1 - 18, (int)MathF.Floor(mid + w) - 1, y, wall1 + 8);
        }
        g.Box(gate0, 9, wall1 - 18, gate1, 9, wall1 + 8, Cobble);
        // Voussoirs round the arch and the door itself, studded, set back in the passage.
        for (int y = 52; y <= 74; y++)
        for (int x = gate0 - 3; x <= gate1 + 3; x++)
            if (g.Get(x, y, wall1 + 8) != 0 && (g.Get(x, y - 2, wall1 + 8) == 0 || g.Get(x - 2, y, wall1 + 8) == 0 || g.Get(x + 2, y, wall1 + 8) == 0))
                g.Set(x, y, wall1 + 9, StoneCap);
        g.Box(gate0, 8, wall1 - 2, gate1, 70, wall1 - 1, PlanksDark);
        for (int y = 16; y <= 64; y += 12) g.Box(gate0, y, wall1, gate1, y + 1, wall1, Iron);
        for (int y = 12; y <= 64; y += 6)
        for (int x = gate0 + 2; x <= gate1 - 2; x += 5)
            g.Set(x, y, wall1, Iron);
        g.Box((gate0 + gate1) / 2, 8, wall1, (gate0 + gate1) / 2, 70, wall1, Beam);
        // Portcullis raised in the arch: its teeth show.
        for (int x = gate0 + 1; x <= gate1 - 1; x += 3) g.Box(x, 62, wall1 + 5, x, 70, wall1 + 5, Iron);
        g.Box(gate0, 66, wall1 + 5, gate1, 66, wall1 + 5, Iron);
        // Windows over the gate, and banners down the gatehouse towers.
        g.Box(80, 112, wall1 + 8, 84, 124, wall1 + 8, Window).Box(91, 112, wall1 + 8, 95, 124, wall1 + 8, Window);
        g.Box(79, 111, wall1 + 9, 96, 111, wall1 + 9, StoneCap);
        foreach (int bx in new[] { gx0 - 9, gx1 + 2 })
        {
            g.Box(bx, 66, wall1 + 9, bx + 7, 124, wall1 + 9, Team);
            g.Box(bx, 66, wall1 + 9, bx, 124, wall1 + 9, Gold).Box(bx + 7, 66, wall1 + 9, bx + 7, 124, wall1 + 9, Gold);
            g.Box(bx + 2, 96, wall1 + 10, bx + 5, 104, wall1 + 10, Gold).Box(bx + 3, 92, wall1 + 10, bx + 4, 108, wall1 + 10, Gold);
            g.Box(bx - 1, 125, wall1 + 9, bx + 8, 126, wall1 + 10, Beam);
            for (int k = 0; k < 4; k++) g.Box(bx + k, 62 + k, wall1 + 9, bx + 7 - k, 62 + k, wall1 + 9, Team); // swallowtail
            g.Clear(bx + 3, 62, wall1 + 9, bx + 4, 65, wall1 + 9);
        }
        // Torches either side of the gate.
        foreach (int tx in new[] { gate0 - 6, gate1 + 5 })
        {
            g.Box(tx, 40, wall1 + 9, tx + 1, 48, wall1 + 11, Beam);
            g.Box(tx, 49, wall1 + 10, tx + 1, 53, wall1 + 11, Flame);
            g.Box(tx, 54, wall1 + 10, tx, 55, wall1 + 10, Flame);
        }

        // The keep in the courtyard.
        const int k0 = 54, k1 = 121, kz0 = 40, kz1 = 107, kTop = 207;
        g.Box(k0 - 2, 8, kz0 - 2, k1 + 2, 24, kz1 + 2, StoneDark);
        g.Box(k0, 8, kz0, k1, kTop, kz1, StoneKeep);
        // Pilaster buttresses at the corners and middles.
        foreach (int x in new[] { k0 - 2, (k0 + k1) / 2 - 3, k1 - 5 })
        {
            g.Box(x, 8, kz0 - 3, x + 7, kTop - 8, kz0 - 1, StoneKeep);
            g.Box(x, 8, kz1 + 1, x + 7, kTop - 8, kz1 + 3, StoneKeep);
        }
        foreach (int z in new[] { kz0 - 2, (kz0 + kz1) / 2 - 3, kz1 - 5 })
        {
            g.Box(k0 - 3, 8, z, k0 - 1, kTop - 8, z + 7, StoneKeep);
            g.Box(k1 + 1, 8, z, k1 + 3, kTop - 8, z + 7, StoneKeep);
        }
        g.Box(k0 - 4, kTop - 7, kz0 - 4, k1 + 4, kTop - 4, kz1 + 4, StoneCap);   // corbel table
        g.Box(k0 - 4, kTop - 3, kz0 - 4, k1 + 4, kTop + 10, kz1 + 4, StoneKeep);
        g.Clear(k0, kTop - 3, kz0, k1, kTop + 10, kz1);
        g.Box(k0, kTop - 3, kz0, k1, kTop - 3, kz1, Planks);
        for (int k = 0; k < 80; k += 12)
        {
            g.Clear(k0 - 4 + k + 6, kTop + 4, kz0 - 4, k0 - 4 + k + 11, kTop + 10, kz1 + 4);
            g.Clear(k0 - 4, kTop + 4, kz0 - 4 + k + 6, k1 + 4, kTop + 10, kz0 - 4 + k + 11);
        }
        g.Box(k0, kTop - 3, kz0, k1, kTop - 3, kz1, Planks);
        g.Box(k0 + 4, kTop - 2, kz0 + 4, k1 - 4, kTop + 10, kz1 - 4, default);
        // Arched, lit windows on every face, two storeys.
        foreach (int y in new[] { 96, 150 })
        foreach (int c in new[] { 70, 99 })
        {
            int wx0 = c, wx1 = c + 6, top = y + 18;
            for (int yy = y; yy <= top + 3; yy++)
            {
                int inset = yy > top ? yy - top : 0;
                g.Box(wx0 + inset, yy, kz1, wx1 - inset, yy, kz1, Window);
                g.Box(wx0 + inset, yy, kz0, wx1 - inset, yy, kz0, Window);
                g.Box(k0, yy, wx0 - 16 + inset, k0, yy, wx1 - 16 - inset, Window);
                g.Box(k1, yy, wx0 - 16 + inset, k1, yy, wx1 - 16 - inset, Window);
            }
            g.Box(wx0 - 1, y - 2, kz1 + 1, wx1 + 1, y - 1, kz1 + 2, StoneCap);   // sills
            g.Box(wx0 - 1, y - 2, kz0 - 2, wx1 + 1, y - 1, kz0 - 1, StoneCap);
            g.Box(wx0 + 3, y, kz1, wx0 + 3, top + 2, kz1, Beam);                // mullions
            g.Box(wx0 + 3, y, kz0, wx0 + 3, top + 2, kz0, Beam);
        }
        // The keep's door, up a flight of steps.
        g.Box(82, 8, kz1 + 1, 93, 40, kz1 + 1, Shadow);
        g.Box(83, 26, kz1 + 1, 92, 39, kz1 + 1, PlanksDark);
        for (int s = 0; s < 9; s++) g.Box(80, 8 + s * 2, kz1 + 2 + (8 - s) * 3, 95, 9 + s * 2, kz1 + 4 + (8 - s) * 3, StoneCap);
        g.Box(80, 8, kz1 + 2, 95, 25, kz1 + 6, StoneCap);
        // A turret on the keep's back right corner, roofed in slate, flying the owner's banner.
        g.Cylinder(k1 - 2, kz0 + 2, 11, kTop - 4, kTop + 34, StoneKeep);
        g.Cylinder(k1 - 2, kz0 + 2, 13, kTop + 33, kTop + 35, StoneCap);
        g.Cone(k1 - 2, kz0 + 2, 15, kTop + 36, kTop + 76, Roof);
        g.Box(k1 - 5, kTop + 18, kz0 + 13, k1 + 1, kTop + 26, kz0 + 13, Window);
        g.Box(k1 - 2, kTop + 76, kz0 + 2, k1 - 1, kTop + 104, kz0 + 3, Beam);
        g.Box(k1, kTop + 84, kz0 + 2, k1 + 30, kTop + 103, kz0 + 3, Team);
        g.Box(k1, kTop + 84, kz0 + 2, k1 + 30, kTop + 85, kz0 + 3, TeamDark);
        g.Box(k1 + 10, kTop + 90, kz0 + 1, k1 + 18, kTop + 98, kz0 + 4, Gold);

        // A lean-to against the inside of the left wall: timber posts, a shingle roof, hay and barrels.
        const int lx0 = wall0 + thick, lz0 = 70, lz1 = 108;
        foreach (int z in new[] { lz0, lz1 - 2 }) g.Box(lx0 + 20, 9, z, lx0 + 21, 52, z + 1, Beam);
        for (int i = 0; i <= 22; i++) g.Box(lx0 + i - 1, 70 - i, lz0 - 2, lx0 + i, 72 - i, lz1 + 1, RoofRed);
        g.Box(lx0, 9, lz0 + 4, lx0 + 8, 18, lz0 + 14, Hay);
        g.Box(lx0 + 1, 19, lz0 + 5, lx0 + 7, 21, lz0 + 12, Hay);
        foreach (int z in new[] { lz0 + 20, lz0 + 28 })
        {
            g.Cylinder(lx0 + 6, z, 4, 9, 22, Planks);
            g.Cylinder(lx0 + 6, z, 4.4f, 11, 12, Iron);
            g.Cylinder(lx0 + 6, z, 4.4f, 19, 20, Iron);
        }
        g.Box(lx0 + 12, 9, lz1 - 12, lx0 + 19, 16, lz1 - 5, Planks).Box(lx0 + 12, 12, lz1 - 12, lx0 + 19, 12, lz1 - 5, Beam);
        // Stairs up to the wall walk, along the inside of the back wall.
        for (int s = 0; s < 37; s++)
            g.Box(140 - s * 2 - 1, 9, wall0 + thick, 140 - s * 2, 9 + s * 3 + 2, wall0 + thick + 8, Stone);

        // Ivy climbing the lower walls, and the odd stone standing proud.
        for (int y = 8; y < 100; y++)
        for (int k = wall0; k <= wall1; k++)
        {
            foreach (var (x, z, ox, oz) in new[] { (k, wall0 - 1, 0, 0), (k, wall1 + 1, 0, 0), (wall0 - 1, k, 0, 0), (wall1 + 1, k, 0, 0) })
            {
                if (g.Get(x - ox, y, z - oz) != 0) continue;
                bool backed = g.Get(x, y, z + 1) != 0 || g.Get(x, y, z - 1) != 0 || g.Get(x + 1, y, z) != 0 || g.Get(x - 1, y, z) != 0;
                if (!backed) continue;
                // Ivy grows in patches from the foot: a patch's height falls off with distance from its root.
                int patch = (k / 24) * 24 + 12;
                uint h = Hash(patch, 0, x == k ? z : x);
                int height = (int)(h % 70);
                if (h % 3 == 0 && y < 8 + height - Math.Abs(k - patch) * 2 && (Hash(x, y, z) & 3) != 0)
                    g.Set(x, y, z, (Hash(x, y, z) & 4) != 0 ? Ivy : IvyDark);
                else if (y > 20 && y % 4 == 1 && (Hash(x, y / 4, z / 8 + x / 8) % 31) == 0)
                    for (int yy = y; yy < y + 3; yy++) g.Set(x, yy, z, StoneDark);
            }
        }
        return g;
    }
}
