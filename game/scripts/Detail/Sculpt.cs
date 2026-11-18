using System;
using Godot;

namespace Rts.Game.Detail;

/// <summary>
/// Resolution-free modelling for detailed units. Shapes are given in sculpt units (u) of a thirty-second of a
/// block (1.56 cm) and filled into a <see cref="FineGrid"/> whose voxels are <see cref="Step"/> units across, a
/// voxel taking a shape's material if its centre lies inside the shape. So one model description gives the
/// finest look near the camera (step 1), and the same model in voxels two or four times bigger further out
/// (steps 2 and 4), with the same proportions, moss and colouring.
///
/// Besides solid shapes (boxes with bevelled edges, balls, tapered limbs, rods) there are passes over the
/// surface: patches of another colour (<see cref="Mottle"/>), moss on what faces up (<see cref="Moss"/>), and
/// voxels knocked off the surface (<see cref="Erode"/>), all driven by noise in sculpt units so they land in
/// the same places at every step.
/// Models face +Z with their feet at y = 0, centred on x and z, as in <see cref="UnitModels"/>.
/// </summary>
public sealed class Sculpt
{
    public const int UnitsPerBlock = 32;
    public const float UnitSize = ChunkMesher.BlockSize / UnitsPerBlock;

    /// <summary>What a shape does to the voxels whose centre it holds.</summary>
    public enum Op
    {
        /// <summary>Sets them to the material.</summary>
        Fill,
        /// <summary>Recolours only those already filled.</summary>
        Paint,
        /// <summary>Empties them.</summary>
        Carve,
    }

    public readonly FineGrid Grid;
    /// <summary>Sculpt units per voxel.</summary>
    public readonly int Step;

    /// <summary>A sculpt holding anything within [x0, x1] x [y0, y1] x [z0, z1] (sculpt units), in voxels <paramref name="step"/> units across.</summary>
    public Sculpt(int step, float x0, float y0, float z0, float x1, float y1, float z1)
    {
        Step = step;
        Grid = new FineGrid(V(x0), V(y0), V(z0), V(x1), V(y1), V(z1));
    }

    /// <summary>Metres across one voxel of this sculpt.</summary>
    public float VoxelSize => Step * UnitSize;

    private int V(float u) => (int)MathF.Floor(u / Step);
    private float Centre(int v) => (v + 0.5f) * Step;

    /// <summary>Applies <paramref name="inside"/> (sculpt units) to every voxel whose centre lies in the box [lo, hi].</summary>
    public Sculpt Shape(Vector3 lo, Vector3 hi, Func<Vector3, bool> inside, FineMaterial m, Op op = Op.Fill)
    {
        byte c = op == Op.Carve ? (byte)0 : Grid.Of(m);
        for (int y = V(lo.Y); y <= V(hi.Y); y++)
        for (int z = V(lo.Z); z <= V(hi.Z); z++)
        for (int x = V(lo.X); x <= V(hi.X); x++)
        {
            if (!Grid.Inside(x, y, z)) continue;
            if (op == Op.Paint && Grid.Get(x, y, z) == 0) continue;
            if (inside(new Vector3(Centre(x), Centre(y), Centre(z)))) Grid.Set(x, y, z, c);
        }
        return this;
    }

    /// <summary>A box from corner to corner.</summary>
    public Sculpt Box(float x0, float y0, float z0, float x1, float y1, float z1, FineMaterial m, Op op = Op.Fill)
    {
        var lo = new Vector3(MathF.Min(x0, x1), MathF.Min(y0, y1), MathF.Min(z0, z1));
        var hi = new Vector3(MathF.Max(x0, x1), MathF.Max(y0, y1), MathF.Max(z0, z1));
        return Shape(lo, hi, p => p.X >= lo.X && p.X <= hi.X && p.Y >= lo.Y && p.Y <= hi.Y && p.Z >= lo.Z && p.Z <= hi.Z, m, op);
    }

    /// <summary>
    /// A box centred on <paramref name="c"/> with half sizes <paramref name="h"/>, its edges and corners rounded
    /// off by <paramref name="r"/>: armour plates, stones, bellies.
    /// </summary>
    public Sculpt RoundBox(Vector3 c, Vector3 h, float r, FineMaterial m, Op op = Op.Fill)
    {
        r = MathF.Min(r, MathF.Min(h.X, MathF.Min(h.Y, h.Z)));
        var inner = h - new Vector3(r, r, r);
        return Shape(c - h, c + h, p =>
        {
            var q = (p - c).Abs() - inner;
            var outside = new Vector3(MathF.Max(q.X, 0), MathF.Max(q.Y, 0), MathF.Max(q.Z, 0));
            return outside.Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0) <= r;
        }, m, op);
    }

    /// <summary>An ellipsoid centred on <paramref name="c"/> with radii <paramref name="r"/>.</summary>
    public Sculpt Ball(Vector3 c, Vector3 r, FineMaterial m, Op op = Op.Fill) =>
        Shape(c - r, c + r, p =>
        {
            var d = (p - c) / r;
            return d.LengthSquared() <= 1f;
        }, m, op);

    public Sculpt Ball(Vector3 c, float r, FineMaterial m, Op op = Op.Fill) => Ball(c, new Vector3(r, r, r), m, op);

    /// <summary>A limb: a capsule from <paramref name="a"/> (radius <paramref name="ra"/>) to <paramref name="b"/> (radius <paramref name="rb"/>).</summary>
    public Sculpt Limb(Vector3 a, Vector3 b, float ra, float rb, FineMaterial m, Op op = Op.Fill)
    {
        float r = MathF.Max(ra, rb);
        var lo = a.Min(b) - new Vector3(r, r, r);
        var hi = a.Max(b) + new Vector3(r, r, r);
        var ab = b - a;
        float len2 = MathF.Max(ab.LengthSquared(), 1e-6f);
        return Shape(lo, hi, p =>
        {
            float t = Math.Clamp((p - a).Dot(ab) / len2, 0f, 1f);
            float rr = ra + (rb - ra) * t;
            return (p - (a + ab * t)).LengthSquared() <= rr * rr;
        }, m, op);
    }

    public Sculpt Limb(Vector3 a, Vector3 b, float r, FineMaterial m, Op op = Op.Fill) => Limb(a, b, r, r, m, op);

    /// <summary>A rod with flat ends from <paramref name="a"/> to <paramref name="b"/>: shafts, hafts, round shields (short and wide).</summary>
    public Sculpt Rod(Vector3 a, Vector3 b, float r, FineMaterial m, Op op = Op.Fill)
    {
        var lo = a.Min(b) - new Vector3(r, r, r);
        var hi = a.Max(b) + new Vector3(r, r, r);
        var ab = b - a;
        float len2 = MathF.Max(ab.LengthSquared(), 1e-6f);
        return Shape(lo, hi, p =>
        {
            float t = (p - a).Dot(ab) / len2;
            return t >= 0 && t <= 1 && (p - (a + ab * t)).LengthSquared() <= r * r;
        }, m, op);
    }

    /// <summary>
    /// An armour plate: a bevelled slab, its face a shade darker inside a raised rim, with a rivet in each corner.
    /// <paramref name="c"/> is its middle, <paramref name="h"/> its half sizes; it is thinnest along the axis
    /// <paramref name="h"/> is smallest on, and the rim and rivets are on the side <paramref name="facing"/>.
    /// </summary>
    public Sculpt Plate(Vector3 c, Vector3 h, Vector3 facing, FineMaterial plate, FineMaterial face, FineMaterial rivet, float rim = 2.5f)
    {
        RoundBox(c, h, MathF.Min(1.5f, MathF.Min(h.X, MathF.Min(h.Y, h.Z))), plate);
        // The face inside the rim, a shade darker and sunk a little.
        var inset = h - new Vector3(rim, rim, rim);
        var n = facing.Normalized();
        var thin = new Vector3(MathF.Abs(n.X), MathF.Abs(n.Y), MathF.Abs(n.Z));
        inset = inset * (Vector3.One - thin) + h * thin; // the whole thickness along the facing
        Box(c.X - inset.X, c.Y - inset.Y, c.Z - inset.Z, c.X + inset.X, c.Y + inset.Y, c.Z + inset.Z, face, Op.Paint);
        var front = c + n * (h * thin).Length();
        var across = inset * (Vector3.One - thin);
        var a = front - n * 1.2f - across;
        var b = front + n * 0.2f + across;
        Box(a.X, a.Y, a.Z, b.X, b.Y, b.Z, default, Op.Carve);
        // Rivets just inside the corners, standing proud of the rim.
        var side = h - new Vector3(rim * 0.5f + 0.5f, rim * 0.5f + 0.5f, rim * 0.5f + 0.5f);
        foreach (int su in new[] { -1, 1 })
        foreach (int sv in new[] { -1, 1 })
        {
            var at = front;
            if (thin.X > 0.5f) at += new Vector3(0, su * side.Y, sv * side.Z);
            else if (thin.Y > 0.5f) at += new Vector3(su * side.X, 0, sv * side.Z);
            else at += new Vector3(su * side.X, sv * side.Y, 0);
            Ball(at + n * 0.5f, 1.3f, rivet);
        }
        return this;
    }

    /// <summary>
    /// Recolours what is already there within <paramref name="half"/> units of the plane through
    /// <paramref name="through"/> with normal <paramref name="normal"/>: a strap or sash round a body.
    /// </summary>
    public Sculpt Band(Vector3 through, Vector3 normal, float half, FineMaterial m)
    {
        var n = normal.Normalized();
        var lo = new Vector3(Grid.X0, Grid.Y0, Grid.Z0) * Step;
        var hi = new Vector3(Grid.X0 + Grid.NX, Grid.Y0 + Grid.NY, Grid.Z0 + Grid.NZ) * Step;
        return Shape(lo, hi, p => MathF.Abs((p - through).Dot(n)) <= half, m, Op.Paint);
    }

    // ---- Surface passes ------------------------------------------------------------------------------

    /// <summary>Recolours voxels of <paramref name="target"/> to <paramref name="with"/> in patches about <paramref name="scale"/> units across, covering about <paramref name="amount"/> of it.</summary>
    public Sculpt Mottle(FineMaterial target, FineMaterial with, float scale, float amount, int seed)
    {
        byte from = Grid.Of(target), to = Grid.Of(with);
        Each((x, y, z, c) =>
        {
            if (c == from && Noise(x, y, z, scale, seed) < amount) Grid.Set(x, y, z, to);
        });
        return this;
    }

    /// <summary>
    /// Moss on the voxels of the given materials that face up, in patches (<paramref name="amount"/> of them, about
    /// <paramref name="scale"/> units across), some of it hanging a voxel down the sides and some standing a
    /// voxel proud as tufts.
    /// </summary>
    public Sculpt Moss(FineMaterial moss, FineMaterial mossDark, float scale, float amount, int seed, params FineMaterial[] on)
    {
        var onSet = new bool[256];
        foreach (var m in on) onSet[Grid.Of(m)] = true;
        byte light = Grid.Of(moss), dark = Grid.Of(mossDark);
        var tops = new System.Collections.Generic.List<Vector3I>();
        Each((x, y, z, c) =>
        {
            if (onSet[c] && Grid.Get(x, y + 1, z) == 0 && Noise(x, y, z, scale, seed) < amount) tops.Add(new Vector3I(x, y, z));
        });
        foreach (var p in tops)
        {
            float n = Noise(p.X, p.Y, p.Z, scale * 0.35f, seed + 7);
            Grid.Set(p.X, p.Y, p.Z, n < 0.45f ? dark : light);
            // Tufts a voxel proud; and moss creeping down an open side.
            if (Hash(p.X, p.Y, p.Z, seed + 3) < 0.22f && Grid.Get(p.X, p.Y + 1, p.Z) == 0) Grid.Set(p.X, p.Y + 1, p.Z, light);
            if (Hash(p.X, p.Y, p.Z, seed + 5) < 0.5f)
                foreach (var d in Sides)
                    if (Grid.Get(p.X + d.X, p.Y, p.Z + d.Z) == 0 && onSet[Grid.Get(p.X, p.Y - 1, p.Z)])
                    {
                        Grid.Set(p.X, p.Y - 1, p.Z, dark);
                        break;
                    }
        }
        return this;
    }

    /// <summary>Knocks voxels of the given materials off the surface where the noise is under <paramref name="amount"/>: chipped stone.</summary>
    public Sculpt Erode(float scale, float amount, int seed, params FineMaterial[] on)
    {
        var onSet = new bool[256];
        foreach (var m in on) onSet[Grid.Of(m)] = true;
        var gone = new System.Collections.Generic.List<Vector3I>();
        Each((x, y, z, c) =>
        {
            if (!onSet[c] || !Exposed(x, y, z)) return;
            if (Noise(x, y, z, scale, seed) < amount) gone.Add(new Vector3I(x, y, z));
        });
        foreach (var p in gone) Grid.Set(p.X, p.Y, p.Z, (byte)0);
        return this;
    }

    private static readonly Vector3I[] Sides = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };

    private bool Exposed(int x, int y, int z) =>
        Grid.Get(x + 1, y, z) == 0 || Grid.Get(x - 1, y, z) == 0 || Grid.Get(x, y + 1, z) == 0 ||
        Grid.Get(x, y - 1, z) == 0 || Grid.Get(x, y, z + 1) == 0 || Grid.Get(x, y, z - 1) == 0;

    private void Each(Action<int, int, int, byte> f)
    {
        var g = Grid;
        for (int y = g.Y0; y < g.Y0 + g.NY; y++)
        for (int z = g.Z0; z < g.Z0 + g.NZ; z++)
        for (int x = g.X0; x < g.X0 + g.NX; x++)
        {
            byte c = g.Get(x, y, z);
            if (c != 0) f(x, y, z, c);
        }
    }

    /// <summary>Smooth value noise (0..1) at voxel (x, y, z)'s centre, with features about <paramref name="scale"/> sculpt units across.</summary>
    private float Noise(int x, int y, int z, float scale, int seed)
    {
        float fx = Centre(x) / scale, fy = Centre(y) / scale, fz = Centre(z) / scale;
        int ix = (int)MathF.Floor(fx), iy = (int)MathF.Floor(fy), iz = (int)MathF.Floor(fz);
        float tx = Smooth(fx - ix), ty = Smooth(fy - iy), tz = Smooth(fz - iz);
        float L(int dx, int dy, int dz) => Hash(ix + dx, iy + dy, iz + dz, seed);
        float x00 = Lerp(L(0, 0, 0), L(1, 0, 0), tx), x10 = Lerp(L(0, 1, 0), L(1, 1, 0), tx);
        float x01 = Lerp(L(0, 0, 1), L(1, 0, 1), tx), x11 = Lerp(L(0, 1, 1), L(1, 1, 1), tx);
        return Lerp(Lerp(x00, x10, ty), Lerp(x01, x11, ty), tz);
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>A fixed pseudo-random number in [0, 1) for a lattice point.</summary>
    public static float Hash(int x, int y, int z, int seed)
    {
        uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu ^ (uint)seed * 0x9E3779B9u;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}
