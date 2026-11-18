using System;

namespace Rts.Sim.Meshing;

/// <summary>
/// Colours of the fine voxels buildings are drawn with (see <see cref="StructureDetail"/>). 0 is empty, and
/// <see cref="Occluder"/> marks space that hides faces but is not drawn (the terrain around a building).
/// </summary>
public static class StructurePalette
{
    public const byte Empty = 0;
    public const byte Timber = 1, TimberDark = 2;
    public const byte PlankA = 3, PlankB = 4, PlankC = 5;
    public const byte ThatchA = 6, ThatchB = 7, ThatchC = 8;
    public const byte TileA = 9, TileB = 10, TileC = 11;
    public const byte StoneA = 12, StoneB = 13, StoneC = 14, Mortar = 15;
    public const byte Glass = 16, Shutter = 17, Iron = 18, IronStud = 19;
    public const byte DoorA = 20, DoorB = 21, Soot = 22, SlateA = 23, SlateB = 24;
    public const byte EarthA = 25, EarthB = 26, Ore = 27, CoalA = 28, CoalB = 29;
    /// <summary>Trees: bark, its dark furrows, the pale wood of a cut log's end and its dark heart.</summary>
    public const byte BarkA = 30, BarkB = 31, BarkDark = 32, Heartwood = 33, Pith = 34;
    /// <summary>Leaves: mid, deep and sunlit greens, the shadowed underside, and the yellow-green of some clumps.</summary>
    public const byte LeafA = 35, LeafB = 36, LeafC = 37, LeafDark = 38, LeafYellow = 39;
    /// <summary>Pine bark (reddish plates); birch bark (white, cream, and its dark marks); a mushroom's stalk.</summary>
    public const byte PineBarkA = 40, PineBarkB = 41, BirchA = 42, BirchB = 43, BirchMark = 44, StemA = 45, StemB = 46;
    /// <summary>Pine needles, birch and willow leaves, and a dusk tree's violets (with the specks that glow).</summary>
    public const byte NeedleA = 47, NeedleB = 48, NeedleDark = 49, BirchLeafA = 50, BirchLeafB = 51,
        WillowA = 52, WillowB = 53, DuskA = 54, DuskB = 55, DuskDark = 56, DuskGlow = 57;
    /// <summary>A giant mushroom's cap: two reds, its pale spots and the gills underneath.</summary>
    public const byte CapA = 58, CapB = 59, CapSpot = 60, Gills = 61, GillsDark = 62;
    /// <summary>The dusk tree: grey bark in three shades, the cyan glow in its cracks, a skull's bone and its hollows, red sap.</summary>
    public const byte DuskBarkA = 63, DuskBarkB = 64, DuskBarkDark = 65, DuskVein = 66, Bone = 67, BoneDark = 68, Sap = 69;
    /// <summary>A barrier's stakes: weathered logs in two shades.</summary>
    public const byte StakeA = 70, StakeB = 71;
    /// <summary>A rampart's dressed stone: paler than a house's stone, in three shades, and its mortar.</summary>
    public const byte RampartA = 72, RampartB = 73, RampartC = 74, RampartMortar = 75;
    /// <summary>One more than the highest colour.</summary>
    public const byte Count = 76;

    /// <summary>Leaf colours: drawn swaying in the wind.</summary>
    public static bool IsLeaf(byte c) => c >= LeafA && c <= LeafYellow || c >= NeedleA && c <= DuskDark;

    /// <summary>Colours that glow by themselves (a dusk tree's specks).</summary>
    public static bool IsGlow(byte c) => c is DuskGlow or DuskVein;
    public const byte Occluder = 255;
}

/// <summary>
/// Character-scale detail for building blocks. The game's rules see a building as whole 0.5 m blocks; each
/// of those blocks is drawn as <see cref="Size"/>^3 fine voxels (6.25 cm, the size units are built from),
/// shaped by its material and its six neighbours: plank walls get a dark timber frame, recessed boards and
/// windows with shutters, doors get boards, iron straps and a frame, roofs slope at 45 degrees in shingle or
/// straw courses, stone gets coursed blocks with sunken mortar, wall tops get battlements and arrow slits,
/// chimneys a sooty mouth, gates banded boards, free-standing plank walls a parapet, and a barrier is a
/// row of pointed stakes. Ramparts are paler stone under a sunk deck with battlements on the outer edges, and
/// a watchtower is an open frame of posts and braces round a ladder, under a railed deck. Rubble is a heap of
/// lumps of what it was made of. Trees
/// are drawn the same way: round trunks of furrowed bark that flare into roots at the ground, crowns of
/// leaf clumps with ragged, rounded edges in several greens, and felled trees as logs lying along their
/// line with cut ends and leafy twigs. A shape never leaves its block, so what is drawn always
/// matches what blocks movement and what can be hit. Patterns use world coordinates, so they run on across
/// neighbouring blocks without seams. Integer-only and thread-safe.
/// </summary>
public static class StructureDetail
{
    public const int Size = 8;
    public const int Volume = Size * Size * Size;

    /// <summary>
    /// How many fine voxels the deck on top of a wall, gate or tower is sunk into its top block, so the
    /// parapet around it stands that much above it. The game draws whoever stands up there that much lower.
    /// </summary>
    public const int DeckSink = 4;

    // Directions, numbered as quad faces: 0 +X, 1 -X, 2 +Y, 3 -Y, 4 +Z, 5 -Z.
    private const int PX = 0, NX = 1, PY = 2, NY = 3, PZ = 4, NZ = 5;
    private static readonly int[] Horizontal = { PX, NX, PZ, NZ };

    /// <summary>Building blocks, drawn with fine detail instead of as plain blocks.</summary>
    public static bool IsStructure(BlockType t) =>
        t is BlockType.Planks or BlockType.Thatch or BlockType.RoofTile or BlockType.StoneBrick
            or BlockType.Door or BlockType.Gate or BlockType.Palisade or BlockType.Rampart or BlockType.Trestle
            or BlockType.Floor;

    /// <summary>
    /// Blocks drawn as fine voxels: building blocks, and the rubble collapses leave, drawn as heaps of lumps.
    /// </summary>
    public static bool IsDetailed(BlockType t) => IsStructure(t) || BlockInfo.IsRubble(t) || IsTree(t);

    /// <summary>Standing trees (every kind): trunks and leaves.</summary>
    public static bool IsTree(BlockType t) => BlockInfo.IsTrunk(t) || BlockInfo.IsLeaves(t) || BlockInfo.IsDuskTree(t);

    /// <summary>Index in a 3 x 3 x 3 neighbourhood of the block at offset (dx, dy, dz) from its middle.</summary>
    public static int NeighbourIndex(int dx, int dy, int dz) => ((dy + 1) * 3 + (dz + 1)) * 3 + (dx + 1);

    /// <summary>Index of fine voxel (x, y, z) in a shape.</summary>
    public static int VoxelIndex(int x, int y, int z) => (y * Size + z) * Size + x;

    /// <summary>
    /// Fills <paramref name="shape"/> (<see cref="Volume"/> colours, see <see cref="VoxelIndex"/>) with the fine
    /// voxels of the block in the middle of <paramref name="neighbours"/> (27 blocks, see
    /// <see cref="NeighbourIndex"/>), which is at world block (wx, wy, wz).
    /// </summary>
    public static void Fill(ReadOnlySpan<BlockType> neighbours, int wx, int wy, int wz, Span<byte> shape)
    {
        if (neighbours.Length < 27) throw new ArgumentException("Need 27 neighbours", nameof(neighbours));
        if (shape.Length < Volume) throw new ArgumentException("Shape is too small", nameof(shape));
        var c = new Block(neighbours, wx, wy, wz);
        for (int y = 0; y < Size; y++)
        for (int z = 0; z < Size; z++)
        for (int x = 0; x < Size; x++)
            shape[VoxelIndex(x, y, z)] = c.Voxel(x, y, z);
    }

    /// <summary>A small deterministic hash for picking shades.</summary>
    private static int Hash(int a, int b, int c = 0)
    {
        unchecked
        {
            uint h = (uint)a * 73856093u ^ (uint)b * 19349663u ^ (uint)c * 83492791u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }

    private static bool IsTimberWall(BlockType t) => t is BlockType.Planks or BlockType.Door;

    /// <summary>The four corners of a block's level: the diagonal neighbours.</summary>
    private static readonly (int X, int Z)[] Corners = { (1, 1), (-1, 1), (1, -1), (-1, -1) };

    /// <summary>One block and what is around it, for working out its voxels.</summary>
    private readonly ref struct Block
    {
        private readonly BlockType _self;
        private readonly BlockType _px, _nx, _py, _ny, _pz, _nz;
        private readonly int _open; // bit per direction: nothing solid there, so that side shows
        private readonly int _openCorners; // bit per corner (see Corners): nothing solid there, at this level
        private readonly int _links;  // the dusk tree: bit per neighbour (NeighbourIndex) that is its wood
        private readonly int _wx, _wy, _wz;
        private readonly int _window; // the side a plank wall's window faces, or -1
        private readonly int _face;   // the side a door or gate faces, or -1
        private readonly Kind _kind;

        private enum Kind : byte { Plain, Slope, Battlement, Crown }

        public Block(ReadOnlySpan<BlockType> n, int wx, int wy, int wz)
        {
            _self = n[NeighbourIndex(0, 0, 0)];
            _px = n[NeighbourIndex(1, 0, 0)];
            _nx = n[NeighbourIndex(-1, 0, 0)];
            _py = n[NeighbourIndex(0, 1, 0)];
            _ny = n[NeighbourIndex(0, -1, 0)];
            _pz = n[NeighbourIndex(0, 0, 1)];
            _nz = n[NeighbourIndex(0, 0, -1)];
            _wx = wx; _wy = wy; _wz = wz;
            _open = 0;
            _links = 0;
            if (BlockInfo.IsDuskTree(_self))
                for (int k = 0; k < 27; k++)
                    if (k != 13 && BlockInfo.IsDuskTree(n[k])) _links |= 1 << k;
            for (int d = 0; d < 6; d++)
                if (!BlockInfo.IsSolid(NeighbourOf(d))) _open |= 1 << d;
            _openCorners = 0;
            for (int k = 0; k < Corners.Length; k++)
                if (!BlockInfo.IsSolid(n[NeighbourIndex(Corners[k].X, 0, Corners[k].Z)])) _openCorners |= 1 << k;

            bool xPair = Open(PX) && Open(NX), zPair = Open(PZ) && Open(NZ);
            bool anySide = (_open & (1 << PX | 1 << NX | 1 << PZ | 1 << NZ)) != 0;
            _kind = Kind.Plain;
            if (Open(PY) && anySide)
            {
                if (_self is BlockType.Thatch or BlockType.RoofTile) _kind = Kind.Slope;
                else if (_self == BlockType.StoneBrick)
                    _kind = xPair && zPair ? Kind.Crown : xPair || zPair ? Kind.Battlement : Kind.Slope;
            }

            _face = -1;
            foreach (int d in Horizontal)
                if (Open(d) && (_face < 0 || !Open(Opposite(d)))) _face = d;

            _window = -1;
            if (_self == BlockType.Planks && SideCount() == 1 && BlockInfo.IsSolid(_ny) && IsStructure(_ny)
                && _py == BlockType.Planks)
            {
                int d = _face;
                var (a, b) = Tangents(d);
                var ta = NeighbourOf(a);
                var tb = NeighbourOf(b);
                if (ta == BlockType.Planks && tb == BlockType.Planks && ((Tangent(d, wx, wz) + wy) & 1) == 0)
                    _window = d;
            }
        }

        /// <summary>
        /// Whether voxel column (x, z) of a deck block (the open top of a wall, gate or tower) is in its parapet:
        /// <paramref name="thick"/> voxels in from every open side, and the corner voxels on the inside of a turn.
        /// <paramref name="merlon"/>: a merlon stands there, not a gap to shoot through. Stone merlons and gaps
        /// are a block long each; boards (<paramref name="boards"/>) have a gap of three voxels in every block.
        /// Where two sides meet there is always a merlon.
        /// </summary>
        private bool Parapet(int x, int z, int thick, bool boards, out bool merlon)
        {
            merlon = false;
            int sides = 0;
            int gx = _wx * Size + x, gz = _wz * Size + z;
            foreach (int d in Horizontal)
            {
                if (!Open(d) || Depth(d, x, 0, z) >= thick) continue;
                sides++;
                int gu = Tangent(d, gx, gz);
                merlon |= boards ? (gu & (Size - 1)) < 5 : ((gu >> 3) & 1) == 0;
            }
            if (sides > 1) merlon = true;
            if (sides > 0) return true;
            for (int k = 0; k < Corners.Length; k++)
            {
                if ((_openCorners & (1 << k)) == 0) continue;
                int dx = Corners[k].X > 0 ? Size - 1 - x : x, dz = Corners[k].Z > 0 ? Size - 1 - z : z;
                if (dx >= thick || dz >= thick) continue;
                merlon = true;
                return true;
            }
            return false;
        }

        private BlockType NeighbourOf(int d) => d switch
        {
            PX => _px, NX => _nx, PY => _py, NY => _ny, PZ => _pz, _ => _nz,
        };

        private bool Open(int d) => (_open & (1 << d)) != 0;

        private int SideCount()
        {
            int n = 0;
            foreach (int d in Horizontal) if (Open(d)) n++;
            return n;
        }

        private static int Opposite(int d) => d ^ 1;

        /// <summary>The two horizontal directions along a side facing <paramref name="d"/>.</summary>
        private static (int, int) Tangents(int d) => d is PX or NX ? (PZ, NZ) : (PX, NX);

        /// <summary>How deep voxel (x, y, z) is from the side facing <paramref name="d"/> (0 on that side).</summary>
        private static int Depth(int d, int x, int y, int z) => d switch
        {
            PX => Size - 1 - x, NX => x, PY => Size - 1 - y, NY => y, PZ => Size - 1 - z, _ => z,
        };

        /// <summary>The world coordinate (blocks or fine voxels) running along a side facing <paramref name="d"/>.</summary>
        private static int Tangent(int d, int x, int z) => d is PX or NX ? z : x;

        /// <summary>The open side (up included) voxel (x, y, z) is nearest, and how deep it is; -1 if none.</summary>
        private (int Dir, int Depth) Nearest(int x, int y, int z, bool withTop)
        {
            int best = -1, depth = int.MaxValue;
            foreach (int d in Horizontal)
            {
                if (!Open(d)) continue;
                int k = Depth(d, x, y, z);
                if (k < depth) { best = d; depth = k; }
            }
            if (withTop && Open(PY) && Depth(PY, x, y, z) < depth) { best = PY; depth = Depth(PY, x, y, z); }
            return (best, depth);
        }

        public byte Voxel(int x, int y, int z) => _self switch
        {
            BlockType.Planks => TimberWall(x, y, z),
            BlockType.Door => Door(x, y, z),
            BlockType.Gate => Gate(x, y, z),
            BlockType.Palisade => Stakes(x, y, z),
            BlockType.Thatch or BlockType.RoofTile => _kind == Kind.Slope ? Slope(x, y, z) : RoofColour(x, y, z, -1),
            BlockType.StoneBrick => Stone(x, y, z),
            BlockType.Rampart => Rampart(x, y, z),
            BlockType.Trestle => Trestle(x, y, z),
            BlockType.Floor => Floorboards(x, y, z),
            BlockType.TreeRubble => Log(x, y, z),
            BlockType.DuskWood => DuskTrunk(x, y, z),
            BlockType.DuskBranch => DuskBranch(x, y, z),
            BlockType.MushroomCap => Cap(x, y, z),
            _ when BlockInfo.IsTrunk(_self) => Trunk(x, y, z),
            BlockType.WillowLeaves when Open(NY) => Strands(x, y, z),
            _ when BlockInfo.IsLeaves(_self) => Leaves(x, y, z),
            _ when BlockInfo.IsRubble(_self) => Rubble(x, y, z),
            _ => StructurePalette.PlankA,
        };

        /// <summary>
        /// A building's floor (<see cref="BlockType.Floor"/>), seen once its walls are knocked open: boards two
        /// voxels wide running along x, in turns of shade, each laid a little differently, on joists.
        /// </summary>
        private byte Floorboards(int x, int y, int z)
        {
            int board = (_wz * Size + z) >> 1;
            if (y < Size - 2) return (z & 1) == 0 && x % 4 == 0 ? StructurePalette.TimberDark : StructurePalette.Timber;
            if (((_wx * Size + x + Hash(board, _wy) % 7) % 11) == 0) return StructurePalette.TimberDark; // butt joints
            return (Hash(board, _wy, _wx) % 3) switch
            {
                0 => StructurePalette.PlankA,
                1 => StructurePalette.PlankB,
                _ => StructurePalette.PlankC,
            };
        }

        // --- Timber walls ------------------------------------------------------------------------------

        private byte TimberWall(int x, int y, int z)
        {
            int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
            // The top of a wall or tower that stands free (not a house's, which has a roof on it, nor a post):
            // a deck of boards sunk between parapets of boards with gaps to shoot through.
            if (Open(PY) && SideCount() < 4)
            {
                if (y >= Size - DeckSink)
                {
                    if (!Parapet(x, z, 1, true, out bool merlon) || !merlon) return StructurePalette.Empty;
                    return y == Size - 1 ? StructurePalette.Timber : Boards(gy, gx + gz);
                }
                if (y == Size - DeckSink - 1 && !Parapet(x, z, 1, true, out _)) return Boards(gz, gx);
            }
            var (d, depth) = Nearest(x, y, z, false);
            if (d < 0) return Boards(gy, gx + gz);
            int gu = Tangent(d, gx, gz), u = gu & (Size - 1);

            if (_window == d && y >= 1 && y <= 6)
            {
                bool inner = u >= 2 && u <= 5 && y >= 2 && y <= 5;
                bool ring = u >= 1 && u <= 6 && !inner;
                if (inner)
                {
                    if (depth < 2) return StructurePalette.Empty;
                    if (depth == 2) return y == 4 ? StructurePalette.TimberDark : StructurePalette.Glass;
                }
                else if (ring && depth == 0) return StructurePalette.Timber;
                else if ((u == 0 || u == 7) && y >= 2 && y <= 5 && depth == 0) return StructurePalette.Shutter;
            }

            // The frame: posts at corners (2 x 2) and every other block boundary, a sill and a top beam.
            var (a, b) = Tangents(d);
            if (depth <= 1 && ((Open(a) && Depth(a, x, y, z) <= 1) || (Open(b) && Depth(b, x, y, z) <= 1)))
                return StructurePalette.TimberDark;
            if (depth == 0)
            {
                if (u == 0 && ((gu >> 3) & 1) == 0) return StructurePalette.Timber;
                if (y == Size - 1 && !IsTimberWall(_py)) return StructurePalette.Timber;
                if (y == 0 && !IsTimberWall(_ny)) return StructurePalette.Timber;
                return StructurePalette.Empty; // boards sit a voxel back from the frame
            }
            return Boards(gy, gu);
        }

        /// <summary>Horizontal boards two voxels high, in lengths of two blocks, staggered row to row.</summary>
        private static byte Boards(int gy, int gu)
        {
            int row = gy >> 1;
            int piece = (gu + (row % 3) * 5) >> 4;
            return (Hash(row, piece) % 5) switch
            {
                0 or 1 => StructurePalette.PlankA,
                2 or 3 => StructurePalette.PlankB,
                _ => StructurePalette.PlankC,
            };
        }

        /// <summary>
        /// A barrier: upright stakes, four to a block, each a log with its corners rounded off, the bark left
        /// on, and at the top of the barrier cut to a pale point.
        /// </summary>
        private byte Stakes(int x, int y, int z)
        {
            int sx = x & 3, sz = z & 3;
            bool edgeX = sx is 0 or 3, edgeZ = sz is 0 or 3;
            if (edgeX && edgeZ) return StructurePalette.Empty;
            if (_py != BlockType.Palisade && y >= Size - 2)
            {
                if (y == Size - 1 && (edgeX || edgeZ)) return StructurePalette.Empty;
                return StructurePalette.Heartwood;
            }
            int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
            int stake = Hash(gx >> 2, gz >> 2, 3);
            // A dark furrow here and there up the bark, and a rope of darker wood binding the stakes.
            if ((gy & 15) == 9 && (edgeX || edgeZ)) return StructurePalette.TimberDark;
            if (Hash(gx, gz, stake) % 7 == 0) return StructurePalette.BarkA;
            return stake % 2 == 0 ? StructurePalette.StakeA : StructurePalette.StakeB;
        }

        // --- Doors and gates ---------------------------------------------------------------------------

        private byte Door(int x, int y, int z)
        {
            int gx = _wx * Size + x, gz = _wz * Size + z;
            if (_face < 0) return StructurePalette.DoorA;
            int d = _face, depth = Depth(d, x, y, z);
            int gu = Tangent(d, gx, gz), u = gu & (Size - 1);
            bool bottom = _ny != BlockType.Door, top = _py != BlockType.Door;
            if (u == 0 || u == Size - 1) return StructurePalette.Timber;
            if (top && y == Size - 1) return StructurePalette.Timber;
            if (top && y == Size - 2 && (u == 1 || u == Size - 2)) return StructurePalette.Timber;
            if (depth == 0)
            {
                if ((bottom && y == 2) || (top && y == 4)) return StructurePalette.Iron;
                if (bottom && y == 5 && u == 5) return StructurePalette.IronStud;
                return StructurePalette.Empty;
            }
            return ((gu >> 1) & 1) == 0 ? StructurePalette.DoorA : StructurePalette.DoorB;
        }

        private byte Gate(int x, int y, int z)
        {
            int gx = _wx * Size + x, gz = _wz * Size + z;
            var (d, depth) = Nearest(x, y, z, false);
            if (d < 0) return StructurePalette.DoorA;
            int gu = Tangent(d, gx, gz), u = gu & (Size - 1);
            var (a, b) = Tangents(d);
            if (depth == 0)
            {
                // Posts where the leaves meet the wall, a lintel under the stone, iron bands with studs.
                if ((u == 0 && NeighbourOf(b) != BlockType.Gate) || (u == Size - 1 && NeighbourOf(a) != BlockType.Gate))
                    return StructurePalette.Timber;
                if (y == Size - 1 && _py != BlockType.Gate) return StructurePalette.Timber;
                if (y == 1 || y == 6) return (gu & 1) == 0 ? StructurePalette.IronStud : StructurePalette.Iron;
                return StructurePalette.Empty;
            }
            return ((gu >> 1) & 1) == 0 ? StructurePalette.DoorA : StructurePalette.DoorB;
        }

        // --- Roofs -------------------------------------------------------------------------------------

        /// <summary>
        /// A roof block on the edge of its layer slopes down at 45 degrees to each open side, so a stepped roof
        /// reads as one smooth hip roof: full height on the inner edge, one voxel at the eave.
        /// </summary>
        private byte Slope(int x, int y, int z)
        {
            int best = -1, closest = -1;
            foreach (int d in Horizontal)
            {
                if (!Open(d)) continue;
                int c = Size - 1 - Depth(d, x, y, z);
                if (c > closest) { closest = c; best = d; }
            }
            if (best < 0) return RoofColour(x, y, z, -1);
            if (y > Size - 1 - closest) return StructurePalette.Empty;
            return RoofColour(x, y, z, best);
        }

        private byte RoofColour(int x, int y, int z, int side)
        {
            int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
            int gu = side < 0 ? gx + gz : Tangent(side, gx, gz);
            if (_self == BlockType.Thatch)
            {
                // Straw: streaks down the slope, changing every few rows.
                return (Hash(gu, gy >> 2) % 5) switch
                {
                    0 or 1 => StructurePalette.ThatchA,
                    2 or 3 => StructurePalette.ThatchB,
                    _ => StructurePalette.ThatchC,
                };
            }
            if (_self == BlockType.StoneBrick)
                return ((gy >> 1) & 1) == 0 ? StructurePalette.SlateA : StructurePalette.SlateB;
            // Tiles: courses two voxels high, tiles four wide, offset course to course.
            int row = gy >> 1;
            if ((gy & 1) == 0 && side >= 0) return StructurePalette.TileB; // the shadowed lower lip of each course
            return (Hash(row, (gu + (row & 1) * 2) >> 2) % 4) switch
            {
                0 => StructurePalette.TileC,
                _ => StructurePalette.TileA,
            };
        }

        // --- Stone -------------------------------------------------------------------------------------

        private byte Stone(int x, int y, int z)
        {
            switch (_kind)
            {
                case Kind.Slope:
                    return Slope(x, y, z);
                case Kind.Battlement when y >= Size / 2:
                {
                    // Merlons five voxels wide with three-voxel gaps, along the wall.
                    int gu = Open(PX) && Open(NX) ? _wz * Size + z : _wx * Size + x;
                    if ((gu & (Size - 1)) >= 5) return StructurePalette.Empty;
                    break;
                }
                case Kind.Crown when y >= Size / 2:
                {
                    bool inside = x >= 2 && x <= 5 && z >= 2 && z <= 5;
                    if (inside) return StructurePalette.Empty;
                    if (y >= Size - 2 && (x is 3 or 4 || z is 3 or 4)) return StructurePalette.Empty;
                    break;
                }
                case Kind.Crown when y == Size / 2 - 1 && x >= 2 && x <= 5 && z >= 2 && z <= 5:
                    return StructurePalette.Soot;
            }

            // Arrow slits in the middle course of a wall line.
            if (_kind == Kind.Plain && _py == BlockType.StoneBrick && IsStructure(_ny)
                && ((Open(PX) && Open(NX)) ^ (Open(PZ) && Open(NZ))))
            {
                int side = Open(PX) && Open(NX) ? PX : PZ;
                int tu = Tangent(side, _wx, _wz);
                if (Hash(tu, _wy, 7) % 3 == 0)
                {
                    int u = Tangent(side, x, z);
                    int depth = Math.Min(Depth(side, x, y, z), Depth(Opposite(side), x, y, z));
                    if (u is 3 or 4 && y >= 1 && y <= 6 && depth <= 1) return StructurePalette.Empty;
                    if (u is 3 or 4 && y >= 1 && y <= 6 && depth == 2) return StructurePalette.Soot;
                }
            }
            return Masonry(x, y, z);
        }

        /// <summary>
        /// A rampart (stone walls, gatehouses and towers): coursed stone, with arrow slits here and there in its
        /// outer faces. Its open top is a deck of flagstones sunk between battlements: merlons two voxels
        /// thick along every outer edge, a block long with a block's gap between them.
        /// </summary>
        private byte Rampart(int x, int y, int z)
        {
            if (Open(PY))
            {
                // A string course under the battlements: a smooth band of the palest stone round the outside.
                if (y <= 1 && Nearest(x, y, z, false) is (>= 0, 0)) return StructurePalette.RampartC;
                if (y >= Size - DeckSink)
                {
                    if (!Parapet(x, z, 2, false, out bool merlon) || !merlon) return StructurePalette.Empty;
                    int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
                    // A merlon: a capstone on dressed blocks.
                    if (y == Size - 1) return StructurePalette.RampartMortar;
                    return Pale(StoneShade(gy >> 2, (gx + gz) >> 2, 2));
                }
                if (y == Size - DeckSink - 1 && !Parapet(x, z, 2, false, out _))
                {
                    // The deck: flagstones of the darker stone houses are built of, so it reads as a walk.
                    int gx = _wx * Size + x, gz = _wz * Size + z;
                    bool joint = (gx & 3) == 3 || (gz & 7) == 7;
                    return joint ? StructurePalette.Mortar : StoneShade(gx >> 2, gz >> 3, 1);
                }
            }
            else if (SideCount() == 1 && _py == BlockType.Rampart && _ny == BlockType.Rampart)
            {
                // An arrow slit in an outer face, two voxels wide, in some blocks of every third course.
                int side = _face;
                if (_wy % 3 == 1 && Hash(Tangent(side, _wx, _wz), _wy, 7) % 2 == 0)
                {
                    int u = Tangent(side, x, z), depth = Depth(side, x, y, z);
                    if (u is 3 or 4 && y >= 1 && y <= 6 && depth <= 1) return StructurePalette.Empty;
                    if (u is 3 or 4 && y >= 1 && y <= 6 && depth == 2) return StructurePalette.Soot;
                }
            }
            return Pale(Masonry(x, y, z));
        }

        /// <summary>A rampart's stone is paler than a house's: the same pattern in its own shades.</summary>
        private static byte Pale(byte c) => c switch
        {
            StructurePalette.StoneA => StructurePalette.RampartA,
            StructurePalette.StoneB => StructurePalette.RampartB,
            StructurePalette.StoneC => StructurePalette.RampartC,
            StructurePalette.Mortar => StructurePalette.RampartMortar,
            _ => c,
        };

        // --- A watchtower's frame ----------------------------------------------------------------------

        /// <summary>
        /// A watchtower (<see cref="BlockType.Trestle"/>): an open timber frame. A post two voxels square stands
        /// at every outer corner; each outer face has a girt along the top of every level and a brace across it,
        /// leaning one way and the other by turns; a ladder runs up the middle of what is closed in on all
        /// sides. The top level is the deck: boards, sunk like a wall's (see <see cref="DeckSink"/>), on a rim
        /// of beams, with a rail on balusters round the open sides. The rest is empty, so the frame is seen
        /// through; it is solid all the same to whoever would walk or shoot through it.
        /// </summary>
        private byte Trestle(int x, int y, int z)
        {
            int gx = _wx * Size + x, gz = _wz * Size + z;
            var (d, depth) = Nearest(x, y, z, false);
            bool post = false;
            if (d >= 0 && depth <= 1)
            {
                var (a, b) = Tangents(d);
                post = (Open(a) && Depth(a, x, y, z) <= 1) || (Open(b) && Depth(b, x, y, z) <= 1);
            }
            if (Open(PY))
            {
                int deck = Size - DeckSink - 1;
                if (y > deck)
                {
                    // The rail: a top rail on the corner posts and on a baluster every four voxels.
                    if (d < 0 || depth > 0) return StructurePalette.Empty;
                    return y == Size - 1 || post || (Tangent(d, gx, gz) & 3) == 0 ? StructurePalette.Timber : StructurePalette.Empty;
                }
                if (y == deck) return Boards(gz, gx);
                // The beams the deck lies on: a rim round the outside, and a joist across the middle of each block.
                if (y >= deck - 2 && ((d >= 0 && depth <= 1) || x is 3 or 4)) return StructurePalette.Timber;
            }
            if (post) return StructurePalette.Timber;
            if (d < 0)
            {
                // Closed in on all sides: the ladder, two rails and a rung every other voxel.
                if (z is not (3 or 4)) return StructurePalette.Empty;
                if (x is 2 or 5) return StructurePalette.StakeB;
                return x is 3 or 4 && z == 3 && (y & 1) == 0 ? StructurePalette.PlankB : StructurePalette.Empty;
            }
            if (depth > 0) return StructurePalette.Empty;
            // An outer face: the girt along its top, and the brace.
            if (y == Size - 1) return StructurePalette.StakeB;
            int u = Tangent(d, gx, gz) & (Size - 1);
            bool lean = ((Tangent(d, _wx, _wz) + _wy) & 1) == 0;
            return (lean ? u : Size - 1 - u) == y ? StructurePalette.StakeA : StructurePalette.Empty;
        }

        /// <summary>
        /// Coursed stone: courses four voxels high, blocks a block long, every other course offset by half;
        /// the mortar between them is sunk a voxel on the face. Tops are flagstones.
        /// </summary>
        private byte Masonry(int x, int y, int z)
        {
            int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
            var (d, depth) = Nearest(x, y, z, true);
            if (d < 0) return StructurePalette.StoneA;
            if (d == PY)
            {
                bool joint = (gx & 7) == 7 || (gz & 7) == 7;
                return joint ? StructurePalette.Mortar : StoneShade(gx >> 3, gz >> 3, 1);
            }
            int gu = Tangent(d, gx, gz);
            int course = gy >> 2, shifted = gu + (course & 1) * 4;
            bool mortar = (gy & 3) == 3 || (shifted & 7) == 7;
            if (mortar)
            {
                bool onTop = Open(PY) && y == Size - 1;
                return depth == 0 && !onTop ? StructurePalette.Empty : StructurePalette.Mortar;
            }
            return StoneShade(course, shifted >> 3, d >> 1);
        }

        // --- Rubble ------------------------------------------------------------------------------------

        /// <summary>
        /// A heap of lumps two voxels across, three to eight high, lower towards open sides; full where something
        /// lies on top. Each lump has a shade of what the rubble was made of.
        /// </summary>
        private byte Rubble(int x, int y, int z)
        {
            int lx = _wx * (Size / 2) + (x >> 1), lz = _wz * (Size / 2) + (z >> 1);
            if (Open(PY))
            {
                int h = 3 + Hash(lx, lz, _wy) % 6;
                foreach (int d in Horizontal)
                    if (Open(d) && Depth(d, x, y, z) <= 1) h -= 2;
                if (y >= Math.Max(h, 1)) return StructurePalette.Empty;
            }
            int k = Hash(lx, lz, (_wy * Size + y) >> 1) % 6;
            return _self switch
            {
                BlockType.StoneRubble => k switch
                {
                    0 or 1 => StructurePalette.StoneA, 2 or 3 => StructurePalette.StoneB,
                    4 => StructurePalette.StoneC, _ => StructurePalette.Mortar,
                },
                BlockType.OreRubble => k < 3 ? StructurePalette.Ore : k < 5 ? StructurePalette.StoneB : StructurePalette.Mortar,
                BlockType.CoalRubble => k < 4 ? StructurePalette.CoalA : StructurePalette.CoalB,
                BlockType.WoodRubble => k switch
                {
                    0 or 1 => StructurePalette.PlankA, 2 => StructurePalette.PlankB,
                    3 => StructurePalette.PlankC, 4 => StructurePalette.Timber, _ => StructurePalette.TimberDark,
                },
                _ => k < 4 ? StructurePalette.EarthA : StructurePalette.EarthB,
            };
        }

        // --- Trees -------------------------------------------------------------------------------------

        /// <summary>
        /// A round trunk in bark that flares into roots at the ground; the top of a trunk with nothing over it
        /// shows its cut. Six voxels across in furrowed brown, the same in a pine's reddish plates, a slender
        /// four in a birch's white bark with dark marks, and a thick pale stalk for a mushroom.
        /// </summary>
        private byte Trunk(int x, int y, int z)
        {
            // Inside the crown it is never seen: a whole block, so it hides the leaves against it.
            if (BlockInfo.IsLeaves(_px) && BlockInfo.IsLeaves(_nx) && BlockInfo.IsLeaves(_pz) && BlockInfo.IsLeaves(_nz))
                return Bark(x, y, z);
            int dx = 2 * x - 7, dz = 2 * z - 7, d2 = dx * dx + dz * dz;
            bool foot = !BlockInfo.IsTrunk(_ny) && y < 2;
            int reach = _self switch
            {
                BlockType.BirchWood => foot ? 60 - 20 * y : 20,
                BlockType.MushroomStem => foot ? 116 - 30 * y : 58,
                _ => foot ? 116 - 36 * y : 40,
            };
            if (d2 > reach) return StructurePalette.Empty;
            if (!Open(PY) || y < Size - 1 || BlockInfo.IsLeaves(_py))
                return Bark(x, y, z);
            return d2 <= 8 ? StructurePalette.Pith : StructurePalette.Heartwood;
        }

        /// <summary>The bark of this block's kind of trunk at voxel (x, y, z).</summary>
        private byte Bark(int x, int y, int z)
        {
            int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
            switch (_self)
            {
                case BlockType.PineWood:
                    // Plates a few voxels tall, split by dark cracks.
                    int plate = Hash(gx >> 1, gy / 3, gz >> 1);
                    return plate % 6 == 0 ? StructurePalette.BarkDark : plate % 6 < 3 ? StructurePalette.PineBarkB : StructurePalette.PineBarkA;
                case BlockType.BirchWood:
                    // White, with short dark dashes running round the trunk.
                    if (Hash(gx >> 2, gy, gz >> 2) % 9 == 0) return StructurePalette.BirchMark;
                    return Hash(gx, gz) % 4 == 0 ? StructurePalette.BirchB : StructurePalette.BirchA;
                case BlockType.MushroomStem:
                    return Hash(gx, gz) % 3 == 0 ? StructurePalette.StemB : StructurePalette.StemA;
                default:
                    return Bark(gx, gz);
            }
        }

        /// <summary>Bark: furrows and plates in two browns, each running the trunk's height.</summary>
        private static byte Bark(int gx, int gz) => (Hash(gx, gz) % 5) switch
        {
            0 => StructurePalette.BarkDark,
            1 or 2 => StructurePalette.BarkB,
            _ => StructurePalette.BarkA,
        };

        /// <summary>
        /// Leaves: each block of the crown in one of a few colours of its kind (lighter on top, darker
        /// underneath), with its outer corners rounded off and, on top, clumps half a block across that stand
        /// proud or sink back, so the crown reads round and leafy. Pines are more ragged, birches let the light
        /// through in places, and dusk trees have specks that glow. Kept to big flat patches: a forest is a lot
        /// of crowns, and every patch is drawn.
        /// </summary>
        private byte Leaves(int x, int y, int z)
        {
            int qx = x >> 2, qz = z >> 2, qy = y >> 2;
            int hb = Hash(_wx, _wy, _wz);
            int h = Hash(_wx * 2 + qx, _wy * 2 + qy, _wz * 2 + qz);
            var (a, b, c, light, under) = LeafColours();
            // Outer corners and edges: the clump against two open sides goes (most of the time; always on a pine).
            int sides = 0;
            for (int d = 0; d < 6; d++)
                if (Open(d) && Depth(d, x, y, z) >> 2 == 0) sides++;
            if (sides >= 3 || sides == 2 && (h % 4 != 0 || _self == BlockType.PineNeedles)) return StructurePalette.Empty;
            // Birches are airy: now and then an outer clump is missing.
            if (_self == BlockType.BirchLeaves && sides == 1 && h % 5 == 0) return StructurePalette.Empty;
            bool top = Open(PY), bottom = Open(NY) && Depth(NY, x, y, z) < 4;
            if (top)
            {
                // Clumps on top sink back two voxels, or stand at full height in a lighter colour.
                int depth = Depth(PY, x, y, z);
                if (h % 4 == 0 && depth < 2) return StructurePalette.Empty;
                if (_self == BlockType.DuskLeaves && depth == 0 && Hash(_wx * Size + x, _wy, _wz * Size + z) % 13 == 0)
                    return StructurePalette.DuskGlow;
                if (depth < 4) return h % 4 == 1 ? light : h % 4 == 2 ? c : a;
            }
            if (bottom) return under;
            return (hb % 5) switch { 0 => b, 1 => c, _ => a };
        }

        /// <summary>This block's leaf colours: mid, deep, second, sunlit and underside.</summary>
        private (byte A, byte B, byte C, byte Light, byte Under) LeafColours() => _self switch
        {
            BlockType.PineNeedles => (StructurePalette.NeedleA, StructurePalette.NeedleB, StructurePalette.NeedleA,
                StructurePalette.NeedleB, StructurePalette.NeedleDark),
            BlockType.BirchLeaves => (StructurePalette.BirchLeafA, StructurePalette.BirchLeafB, StructurePalette.LeafYellow,
                StructurePalette.LeafYellow, StructurePalette.LeafA),
            BlockType.WillowLeaves => (StructurePalette.WillowA, StructurePalette.WillowB, StructurePalette.WillowA,
                StructurePalette.BirchLeafA, StructurePalette.WillowB),
            BlockType.DuskLeaves => (StructurePalette.DuskA, StructurePalette.DuskB, StructurePalette.DuskA,
                StructurePalette.DuskB, StructurePalette.DuskDark),
            _ => (StructurePalette.LeafA, StructurePalette.LeafB, StructurePalette.LeafC, StructurePalette.LeafYellow,
                StructurePalette.LeafDark),
        };

        /// <summary>
        /// The hanging edge of a willow: strands two voxels across, each ending at its own length, so the
        /// curtain frays out at the bottom.
        /// </summary>
        private byte Strands(int x, int y, int z)
        {
            int gx = _wx * Size + x, gz = _wz * Size + z;
            int strand = Hash(gx >> 1, gz >> 1, _wy);
            if (strand % 3 == 0) return StructurePalette.Empty;
            if (y < (strand >> 4) % 7) return StructurePalette.Empty;
            // Against the open sides only: the middle of the block is left hollow (it is never seen).
            if (Nearest(x, y, z, withTop: false).Depth > 3) return StructurePalette.Empty;
            return (strand >> 8) % 3 == 0 ? StructurePalette.BirchLeafA : (strand >> 8) % 3 == 1 ? StructurePalette.WillowB : StructurePalette.WillowA;
        }

        /// <summary>
        /// A giant mushroom's cap: red, its rim bevelled into a dome, pale spots on top, and ribbed gills
        /// underneath.
        /// </summary>
        private byte Cap(int x, int y, int z)
        {
            var (side, depth) = Nearest(x, y, z, withTop: false);
            int top = Open(PY) ? Depth(PY, x, y, z) : Size;
            // The rim curves down: cut the corner between an open side and the open top.
            if (side >= 0 && top < Size && depth + top < 4) return StructurePalette.Empty;
            // Outer corners of the cap round off.
            int open = 0;
            foreach (int d in Horizontal)
                if (Open(d) && Depth(d, x, y, z) < 3) open++;
            if (open >= 2) return StructurePalette.Empty;
            int gx = _wx * Size + x, gz = _wz * Size + z;
            if (Open(NY) && Depth(NY, x, y, z) < 2)
                return ((gx + gz) & 1) == 0 ? StructurePalette.Gills : StructurePalette.GillsDark;
            if (top < 2 && Hash(gx >> 1, gz >> 1, _wy) % 9 == 0) return StructurePalette.CapSpot;
            return Hash(_wx, _wy, _wz) % 3 == 0 ? StructurePalette.CapB : StructurePalette.CapA;
        }

        /// <summary>
        /// A felled tree's log, lying along the line of rubble it is part of (or a way picked from its place for
        /// a block on its own), six voxels thick on the ground, with the pale cut showing at an open end, and
        /// twigs with tufts of leaves along its top.
        /// </summary>
        private byte Log(int x, int y, int z)
        {
            bool alongX = _px == BlockType.TreeRubble || _nx == BlockType.TreeRubble ? true
                : _pz == BlockType.TreeRubble || _nz == BlockType.TreeRubble ? false
                : ((Hash(_wx, _wz) + _wy) & 1) == 0;
            int a = alongX ? x : z, u = alongX ? z : x;
            int du = 2 * u - 7, dy = 2 * y - 5, d2 = du * du + dy * dy;
            int ga = alongX ? _wx * Size + x : _wz * Size + z, gu = alongX ? _wz * Size + z : _wx * Size + x;
            if (d2 <= 40)
            {
                bool endA = a == 0 && !IsLogAt(alongX ? NX : NZ), endB = a == Size - 1 && !IsLogAt(alongX ? PX : PZ);
                if (endA || endB) return d2 <= 6 ? StructurePalette.Pith : d2 <= 30 ? StructurePalette.Heartwood : StructurePalette.BarkDark;
                if (Hash(ga, _wy * Size + y, gu) % 4 == 0 && d2 >= 26) return StructurePalette.BarkDark;
                return (Hash(ga >> 2, gu, _wy * Size + y) % 3) == 2 ? StructurePalette.BarkB : StructurePalette.BarkA;
            }
            if (y < 5 || !Open(PY)) return StructurePalette.Empty;
            // Twigs and leaf tufts on top, in clumps two voxels across.
            int h = Hash(ga >> 1, gu >> 1, _wy);
            if (h % 5 >= 2) return StructurePalette.Empty;
            if (h % 5 == 0 && (u == 3 || u == 4) && y == 5) return StructurePalette.BarkB;
            return ((h >> 6) % 3) switch { 0 => StructurePalette.LeafA, 1 => StructurePalette.LeafB, _ => StructurePalette.LeafYellow };
        }

        private bool IsLogAt(int d) => NeighbourOf(d) == BlockType.TreeRubble;

        // --- The dusk tree -------------------------------------------------------------------------------

        /// <summary>
        /// The dusk tree's trunk and roots: a lumpy column of grey bark that flares out at its foot into a mound
        /// of roots and buttresses three blocks across (each block of it filled more the lower it sits, tapering up
        /// to the trunk), with limbs reaching to the wood beside it, cyan glowing cracks, and on the west root a
        /// skull half sunk in the bark with red sap running down over it.
        /// </summary>
        private byte DuskTrunk(int x, int y, int z)
        {
            int gx = _wx * Size + x, gy = _wy * Size + y, gz = _wz * Size + z;
            bool column = Linked(0, 1, 0) || Linked(0, -1, 0);
            bool foot = !BlockInfo.IsDuskTree(_ny);
            // The skull's block: the root west of the trunk (the trunk east of it, roots north and south, nothing
            // further west), or the trunk itself if nothing grew west of it.
            bool skull = foot && Linked(1, 0, 0) && !Linked(-1, 0, 0) && (Linked(0, 0, 1) && Linked(0, 0, -1) || column);
            if (skull && Skull(x, y, z) is var bone && bone != 0) return bone;
            // How far the foot flares here: fully filled at the ground, less with each voxel up; and somewhat on the
            // block over the roots (wood under it on three sides or more).
            int below = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (Linked(dx, -1, dz)) below++;
            int flare = foot ? 150 - 11 * y : below >= 3 ? 76 - 5 * y : 0;
            bool topless = !Linked(0, 1, 0) && !AnyAbove();
            int dx2 = 2 * x - 7, dz2 = 2 * z - 7, d2 = dx2 * dx2 + dz2 * dz2;
            // Lumps: the bark bulges out a little or sinks back in each eighth round the trunk, every two voxels up.
            int octant = (dx2 >= 0 ? 4 : 0) + (dz2 >= 0 ? 2 : 0) + (Math.Abs(dx2) >= Math.Abs(dz2) ? 1 : 0);
            int reach = 34 + 10 * (Hash(gy >> 1, octant, _wx * 31 + _wz) % 3);
            // A buttress's top (nothing over it) rounds off rather than ending flat.
            if (topless && !foot) reach -= 5 * y;
            reach = Math.Max(reach, flare);
            bool inside = column && d2 <= reach;
            int edge = inside ? reach - d2 : -1;
            // Limbs out to the wood beside it (roots, the buttresses, the first block of each branch).
            int limb = column ? 20 : 36;
            if (Limbs(x, y, z, Math.Max(limb, flare), out int le) && le > edge) (inside, edge) = (true, le);
            if (!inside) return StructurePalette.Empty;
            if (skull && x <= 1 && Math.Abs(z - 3) <= 1 && y >= 5 && y < 9 && (y < 7 || x == 0)) return StructurePalette.Sap;
            return DuskBark(gx, gy, gz, edge, false);
        }

        private bool AnyAbove()
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (Linked(dx, 1, dz)) return true;
            return false;
        }

        /// <summary>A branch or twig of the dusk tree: limbs two voxels thick reaching to the wood beside it.</summary>
        private byte DuskBranch(int x, int y, int z)
        {
            int count = 0;
            for (int k = 0; k < 27; k++)
                if ((_links >> k & 1) != 0) count++;
            int r2 = count <= 1 ? 9 : 16; // a twig's end is thinner
            int dx = 2 * x + 1 - Size, dy = 2 * y + 1 - Size, dz = 2 * z + 1 - Size;
            bool knot = dx * dx + dy * dy + dz * dz <= r2;
            if (!knot && !Limbs(x, y, z, r2, out _)) return StructurePalette.Empty;
            return DuskBark(_wx * Size + x, _wy * Size + y, _wz * Size + z, 0, false);
        }

        private bool Linked(int dx, int dy, int dz) => (_links >> NeighbourIndex(dx, dy, dz) & 1) != 0;

        /// <summary>
        /// True if voxel (x, y, z) is within a limb from the block's middle toward a linked neighbour's, of
        /// (doubled) radius squared <paramref name="r2"/>; <paramref name="edge"/> is how far inside it is.
        /// </summary>
        private bool Limbs(int x, int y, int z, int r2, out int edge)
        {
            edge = -1;
            int px = 2 * x + 1 - Size, py = 2 * y + 1 - Size, pz = 2 * z + 1 - Size;
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if ((dx | dy | dz) == 0 || !Linked(dx, dy, dz)) continue;
                int ex = dx * 2 * Size, ey = dy * 2 * Size, ez = dz * 2 * Size;
                long num = (long)px * ex + (long)py * ey + (long)pz * ez, den = (long)ex * ex + (long)ey * ey + (long)ez * ez;
                long p2 = (long)px * px + (long)py * py + (long)pz * pz;
                long d2 = num <= 0 ? p2 : num >= den
                    ? (long)(px - ex) * (px - ex) + (long)(py - ey) * (py - ey) + (long)(pz - ez) * (pz - ez)
                    : p2 - num * num / den;
                if (d2 > r2) continue;
                edge = Math.Max(edge, (int)(r2 - d2));
            }
            return edge >= 0;
        }

        /// <summary>Grey bark with dark furrows; cracks glow cyan at the surface; red sap where it runs.</summary>
        private static byte DuskBark(int gx, int gy, int gz, int edge, bool sap)
        {
            bool surface = edge < 14;
            if (sap && surface) return StructurePalette.Sap;
            int col = Hash(gx, gz, 7);
            if (surface && col % 13 == 0 && Hash(gx >> 1, gy >> 2, gz >> 1) % 4 != 0) return StructurePalette.DuskVein;
            return (Hash(gx, gy >> 1, gz) % 6) switch
            {
                0 => StructurePalette.DuskBarkDark,
                1 or 2 => StructurePalette.DuskBarkB,
                _ => StructurePalette.DuskBarkA,
            };
        }

        /// <summary>
        /// A skull five voxels wide in the trunk's foot, facing -X, with dark eyes and nose and a row of teeth;
        /// 0 where voxel (x, y, z) is not part of it.
        /// </summary>
        private static byte Skull(int x, int y, int z)
        {
            if (x > 3 || y > 4 || z < 1 || z > 5) return 0;
            bool corner = (x == 3 || x == 0 && y == 4) && (z == 1 || z == 5) || y == 4 && (z == 1 || z == 5) && x == 3;
            if (corner) return 0;
            if (x == 0 && y == 2 && (z == 2 || z == 4)) return StructurePalette.BoneDark;
            if (x == 0 && y == 1 && z == 3) return StructurePalette.BoneDark;
            if (x == 0 && y == 0 && (z & 1) == 0) return StructurePalette.BoneDark;
            return StructurePalette.Bone;
        }

        private static byte StoneShade(int a, int b, int c) => (Hash(a, b, c) % 6) switch
        {
            0 or 1 or 2 => StructurePalette.StoneA,
            3 or 4 => StructurePalette.StoneB,
            _ => StructurePalette.StoneC,
        };
    }
}
