using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>
/// Generates the slice's region from a seed, with integer maths only:
/// ocean on the west and east edges with sandy coasts, a river running north to south through the
/// middle (crossable at three shallow fords), rolling grassland with forests, a hill range with stone
/// ridges in the north, a swamp in the south with patches of poison bog, one cavern with ore and crystal under the
/// hills, and a few pools of lava up in the hills. Bigger maps get more of them, not just bigger ones (issue #67):
/// for each further 512 by 512 blocks of land, one more (smaller) hill range with its own cavern and lava in
/// the swamp's half of the map, and one more swamp in the hills' half (see <see cref="FeatureSets"/>). Old stone
/// ruins stand here and there in the open country, <see cref="RuinsPer512"/> for each 512 by 512 blocks.
/// Each player starts on a flat clearing
/// near their coast, with food, wood, stone and iron placed close by (mirrored, so the starts are balanced).
///
/// Feature sizes are defined for a 512x64x512 region and scale with the world size, so small test
/// worlds still get every feature.
/// </summary>
public static partial class RegionGenerator
{
    /// <summary>Distance (blocks, horizontal) within which each start is guaranteed its basic resources.</summary>
    public const int StartResourceRadius = 40;
    /// <summary>Sinkholes (see <see cref="HazardSystem.MakeSinkhole"/>) per 512 by 512 blocks of map.</summary>
    public const int SinkholesPer512 = 12;
    /// <summary>Patches of quicksand (see <see cref="HazardSystem.MakeQuicksand"/>) per 512 by 512 blocks of map.</summary>
    public const int QuicksandPer512 = 10;
    /// <summary>Rockfalls (see <see cref="HazardSystem.MakeRockfall"/>) per 512 by 512 blocks of map.</summary>
    public const int RockfallsPer512 = 10;
    /// <summary>Columns out from a cliff that its rockfall's scree reaches.</summary>
    public const int ScreeReach = 4;
    /// <summary>Columns from a quicksand patch's middle to its edge.</summary>
    public const int QuicksandRadius = 2;
    /// <summary>Ruins (see the builder's PlaceRuins) per 512 by 512 blocks of map.</summary>
    public const int RuinsPer512 = 3;
    /// <summary>A ruin's walls stand this many columns from its middle (so it is 2 * this + 1 across).</summary>
    public const int RuinHalf = 4;
    /// <summary>The most hill ranges (and swamps) a map gets, however big.</summary>
    public const int MaxFeatureSets = 3;

    /// <summary>
    /// How many hill ranges a map of this size gets, and as many swamps (issue #67): one for each 512 by 512 blocks
    /// of land (rounded down, at least one, at most <see cref="MaxFeatureSets"/>). The 512 map gets one of each,
    /// the default 768 map two.
    /// </summary>
    public static int FeatureSets(int sizeX, int sizeZ) => Math.Clamp((int)((long)sizeX * sizeZ / (512 * 512)), 1, MaxFeatureSets);

    /// <summary>
    /// The default map's width and depth in blocks (issue #45: half again as wide as the first slice's 512, so
    /// 2.25 times the land), and its height: the 64 layers the land is laid out in, and under them
    /// <see cref="DeepLayers"/> more of solid rock with the deep caves running through it. Features are laid out
    /// for 512 and scale with the size.
    /// </summary>
    public const int DefaultSize = 768, DefaultHeight = SurfaceLayers + 64;

    /// <summary>
    /// How many times bigger than the first trees a generated map's trees grow (see
    /// <see cref="VoxelWorld.TreeScale"/>): twice, so the world's trees tower over its people.
    /// </summary>
    public const int DefaultTreeScale = 2;

    public static RegionMap Generate(ulong seed, int sizeX, int sizeY, int sizeZ, int treeScale = DefaultTreeScale) =>
        new Builder(seed, new VoxelWorld(sizeX, sizeY, sizeZ) { TreeScale = Math.Clamp(treeScale, 1, Trees.MaxScale) }).Build();

    /// <summary>Fills an existing empty world. The map's biomes and starts are returned.</summary>
    public static RegionMap Generate(VoxelWorld world, ulong seed) => new Builder(seed, world).Build();

    public static int WaterLevel(int sizeY)
    {
        int deep = DeepLayers(sizeY);
        return deep + Math.Max(3, (sizeY - deep) * 14 / 64);
    }

    /// <summary>
    /// The kind of tree a biome grows, from a roll of 0 to 99: broad-leaved trees and birches in the
    /// grassland, those and pines in the forest, pines in the hills, and willows and giant mushrooms (with a
    /// few broad-leaved trees) in the swamp. The one dusk tree is placed on its own (see <see cref="RegionMap"/>).
    /// </summary>
    public static TreeKind KindFor(Biome biome, int roll) => biome switch
    {
        Biome.Forest => roll < 50 ? TreeKind.Broadleaf : roll < 72 ? TreeKind.Pine : TreeKind.Birch,
        Biome.Grassland => roll < 55 ? TreeKind.Broadleaf : TreeKind.Birch,
        Biome.Hills => TreeKind.Pine,
        Biome.Swamp => roll < 50 ? TreeKind.Willow : roll < 85 ? TreeKind.Mushroom : TreeKind.Broadleaf,
        _ => TreeKind.Broadleaf,
    };

    private static readonly (int Dx, int Dz)[] Compass =
        { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

    /// <summary>
    /// Grows the dusk tree's wood with its trunk at column (x, z) on ground at level g (see
    /// the builder's PlaceDuskTree); the branches' ways depend on the seed. Only fills open air.
    /// </summary>
    public static void GrowDuskTree(VoxelWorld w, int x, int g, int z, ulong seed)
    {
        const int trunk = 10;
        void Wood(int wx, int wy, int wz, BlockType t = BlockType.DuskBranch)
        {
            if (w.InBounds(wx, wy, wz) && w.Get(wx, wy, wz) == BlockType.Air) w.Set(wx, wy, wz, t);
        }
        for (int y = 1; y <= trunk; y++) Wood(x, g + y, z, BlockType.DuskWood);
        // A wide, flared foot: roots all round the trunk, buttresses up its four sides, and two that reach higher.
        // All of it within a column of the trunk, so the columns two off it stay open to stand in.
        foreach (var (dx, dz) in Compass) Wood(x + dx, g + 1, z + dz, BlockType.DuskWood);
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) Wood(x + dx, g + 2, z + dz, BlockType.DuskWood);
        uint fh = DeterministicRandom.Hash(x, z, seed + 32);
        int side = (int)(fh % 4) * 2;
        var (ax, az) = Compass[side];
        var (bx2, bz2) = Compass[(side + 4 + (int)(fh >> 2 & 1) * 2) % 8];
        var (cx, cz) = Compass[(int)(fh >> 3 & 3) * 2 + 1];
        Wood(x + ax, g + 3, z + az, BlockType.DuskWood);
        Wood(x + bx2, g + 3, z + bz2, BlockType.DuskWood);
        Wood(x + cx, g + 2, z + cz, BlockType.DuskWood);
        // A crooked top.
        Wood(x + 1, g + trunk + 1, z);
        Wood(x + 1, g + trunk + 2, z + 1);
        Wood(x, g + trunk + 1, z - 1);
        // Six branches, each its own way round, twisting up and out, with a twig at the end.
        uint h = DeterministicRandom.Hash(x, z, seed + 33);
        int skipA = (int)(h % 8), skipB = (skipA + 3 + (int)(h >> 3 & 1)) % 8;
        int k = 0;
        for (int d = 0; d < 8; d++)
        {
            if (d == skipA || d == skipB) continue;
            var (dx, dz) = Compass[d];
            int bx = x, by = g + 5 + k % 3 + (k >= 3 ? 1 : 0), bz = z;
            int len = dx != 0 && dz != 0 ? 3 : 4;
            uint bh = DeterministicRandom.Hash(x + d, z, seed + 34);
            for (int step = 0; step < len; step++)
            {
                bx += dx;
                bz += dz;
                if ((bh >> step & 1) == 1 || step == 0) by++;
                Wood(bx, by, bz);
            }
            // The twig: up one or two, and one off to the side.
            Wood(bx, by + 1, bz);
            if ((bh >> 5 & 1) == 1) Wood(bx, by + 2, bz);
            var (sx, sz) = Compass[(d + 2) % 8];
            Wood(bx + sx, by + 1, bz + sz);
            k++;
        }
    }

    private sealed partial class Builder
    {
        private readonly ulong _seed;
        private readonly VoxelWorld _w;
        private readonly RegionMap _map;
        private readonly int _sx, _sy, _sz, _water;
        /// <summary>Layers of deep rock under the land (see <see cref="DeepLayers"/>), and the layers the land is laid out in.</summary>
        private readonly int _deep, _top;
        private readonly int[] _height;
        private readonly BlockType[] _surface;
        private readonly DeterministicRandom _rng;

        // Feature placement.
        private int _duskX = -1, _duskZ = -1;
        private readonly int _hillX, _hillZ, _hillR, _swampX, _swampZ, _swampR;
        /// <summary>Every hill range and every swamp (centre and radius), the first of each being the main one above.</summary>
        private readonly List<(int X, int Z, int R)> _hills = new(), _swamps = new();
        /// <summary>Rows (z) at the middle of each ford, the shallow stretches where the river can be waded.</summary>
        private readonly int[] _fordZ;

        public Builder(ulong seed, VoxelWorld world)
        {
            _seed = seed;
            _w = world;
            _map = new RegionMap(seed, world);
            _sx = world.SizeX; _sy = world.SizeY; _sz = world.SizeZ;
            _deep = DeepLayers(_sy);
            _top = _sy - _deep;
            _water = WaterLevel(_sy);
            _height = new int[_sx * _sz];
            _surface = new BlockType[_sx * _sz];
            _rng = new DeterministicRandom(seed ^ 0x6E0A11CEUL);

            _hillX = _sx / 2 + Sx(20) * (_rng.Range(0, 3) - 1);
            _hillZ = _sz / 5 + Sz(_rng.Range(0, 24));
            _hillR = Math.Max(4, _sx / 5);
            _swampX = _sx / 2 + Sx(30) * (_rng.Range(0, 3) - 1);
            _swampZ = _sz * 4 / 5 - Sz(_rng.Range(0, 24));
            _swampR = Math.Max(3, _sx / 7);
            // Half the seeds put the hills in the south and the swamp in the north.
            if (_rng.Range(0, 2) == 1)
            {
                _hillZ = _sz - 1 - _hillZ;
                _swampZ = _sz - 1 - _swampZ;
            }
            _hills.Add((_hillX, _hillZ, _hillR));
            _swamps.Add((_swampX, _swampZ, _swampR));
            // More hills and swamps on a bigger map, from a random stream of their own, so the 512 map is as it
            // was. Each extra hill range is a smaller one in the swamp's half, off to one side of the river; each
            // extra swamp a smaller one in the hills' half, off to the other side, so neither start gets both.
            var more = new DeterministicRandom(seed ^ 0x3E1D5A11UL);
            int side = more.Range(0, 2);
            for (int k = 1; k < FeatureSets(_sx, _sz); k++, side ^= 1)
            {
                int hx = side == 0 ? _sx / 4 : _sx * 3 / 4, sx = side == 0 ? _sx * 4 / 5 : _sx / 5;
                _hills.Add((hx + Sx(more.Range(-16, 17)), _swampZ + Sz(more.Range(-20, 21)), Math.Max(4, _hillR * 3 / 4)));
                _swamps.Add((sx + Sx(more.Range(-12, 13)), _hillZ + Sz(more.Range(-20, 21)), Math.Max(3, _swampR * 3 / 4)));
            }
            // Three fords, roughly at a quarter, half and three quarters of the river's length. Their own
            // random stream, so adding them did not move anything else on existing seeds.
            var fordRng = new DeterministicRandom(seed ^ 0xF02D5UL);
            _fordZ = new int[3];
            for (int k = 0; k < 3; k++) _fordZ[k] = _sz * (k + 1) / 4 + Sz(fordRng.Range(-24, 25));
        }

        /// <summary>Rows each side of a ford's middle that are still shallow.</summary>
        private int FordHalfLength => Sz(5);

        private bool IsFord(int z)
        {
            foreach (int f in _fordZ)
                if (Math.Abs(z - f) <= FordHalfLength) return true;
            return false;
        }

        /// <summary>
        /// Scales a length or offset defined for a 512-wide map. Never shrinks to nothing: at least one block,
        /// in the offset's direction.
        /// </summary>
        private int Sx(int v) => v < 0 ? -Math.Max(1, -v * _sx / 512) : Math.Max(1, v * _sx / 512);
        private int Sz(int v) => v < 0 ? -Math.Max(1, -v * _sz / 512) : Math.Max(1, v * _sz / 512);
        /// <summary>Scales a height defined for a 64-layer map (the land's layers, not the deep rock under them).</summary>
        private int Sy(int v) => Math.Max(1, v * _top / 64);

        public RegionMap Build()
        {
            for (int z = 0; z < _sz; z++)
            for (int x = 0; x < _sx; x++)
                Column(x, z);
            PlaceStarts();
            Fill();
            CarveCaverns();
            CarveDeeps();
            PlaceLava();
            PlaceBogs();
            PlaceDuskTree();
            PlaceRuins();
            PlantTrees();
            PlaceStartResources();
            PlaceSinkholes();
            PlaceQuicksand();
            PlaceRockfalls();
            CarveDeepRamps();
            PlaceGold();
            PlaceWilds();
            return _map;
        }

        private int Idx(int x, int z) => z * _sx + x;

        private int CoastWest(int z) => Sx(36) + Noise.Value(0, z, Sz(64), _seed + 10) * Sx(24) / 256;
        private int CoastEast(int z) => Sx(36) + Noise.Value(0, z, Sz(64), _seed + 11) * Sx(24) / 256;
        private int RiverCentre(int z) => _sx / 2 + (Noise.Value(z, 7, Sz(96), _seed + 12) - 128) * Sx(40) / 128;
        private int RiverHalfWidth => Math.Max(1, Sx(4));

        /// <summary>Signed distance to the nearest coast; negative is ocean.</summary>
        private int ShoreDistance(int x, int z) => Math.Min(x - CoastWest(z), _sx - 1 - x - CoastEast(z));

        private void Column(int x, int z)
        {
            int i = Idx(x, z);
            int shore = ShoreDistance(x, z);
            int beach = Sx(24) + 2;
            int rolling = Noise.Value(x, z, Math.Max(8, Sx(48)), _seed + 1) * Sy(6) / 256
                        + Noise.Value(x, z, Math.Max(4, Sx(16)), _seed + 2) * Sy(3) / 256;
            int land = _water + 2 + rolling;
            Biome biome = Biome.Grassland;
            BlockType surface = BlockType.Grass;
            int h = land;

            if (shore < 0)
            {
                h = _water - 1 - Math.Min(-shore / 3, Sy(6));
                biome = Biome.Ocean;
                surface = BlockType.Sand;
            }
            else if (shore < beach)
            {
                // Wet sand at the water's edge, then dry sand that stays low and flat before it curves up to
                // meet the grass, so there is no step where the beach ends.
                h = shore < 2 ? _water - 1 : _water + (land - _water) * shore * shore / (beach * beach);
                biome = Biome.Beach;
                surface = BlockType.Sand;
            }
            else
            {
                // Hills: a dome of raised land with ridged stone crests.
                int wobble = (Noise.Value(x, z, Math.Max(4, Sx(40)), _seed + 7) - 128) * Sx(36) / 128;
                foreach (var (hx, hz, hr) in _hills)
                {
                    int hd = Noise.Sqrt((long)(x - hx) * (x - hx) + (long)(z - hz) * (z - hz)) + wobble;
                    if (hd < hr)
                    {
                        int ridge = Noise.Value(x, z, Math.Max(4, Sx(28)), _seed + 3);
                        int crest = 255 - Math.Abs(2 * ridge - 255); // 255 on the noise mid-line
                        int bump = (hr - hd) * Sy(24) / hr;
                        int lift = bump * (96 + crest * 160 / 255) / 256;
                        h += lift;
                        if (lift >= Sy(3))
                        {
                            biome = Biome.Hills;
                            if (h >= _water + Sy(12) || crest > 215) surface = BlockType.Stone;
                        }
                    }
                }

                // Swamp: low, flat, muddy ground with pools. The nearest swamp (for its own radius) shapes the column.
                int swampWobble = (Noise.Value(x, z, Math.Max(4, Sx(32)), _seed + 8) - 128) * Sx(24) / 128;
                int sd = int.MaxValue, swampR = 1;
                foreach (var (wx, wz, wr) in _swamps)
                {
                    int d = Noise.Sqrt((long)(x - wx) * (x - wx) + (long)(z - wz) * (z - wz)) + swampWobble;
                    if ((long)d * swampR < (long)sd * wr || sd == int.MaxValue) (sd, swampR) = (d, wr);
                }
                if (sd < swampR)
                {
                    int flat = _water + (Noise.Value(x, z, Math.Max(3, Sx(10)), _seed + 4) > 150 ? 1 : 0);
                    bool pool = Noise.Value(x, z, Math.Max(3, Sx(8)), _seed + 5) < 95;
                    if (pool) flat = _water - 1;
                    int edge = Math.Max(1, Sx(10));
                    h = sd > swampR - edge ? flat + (h - flat) * (sd - (swampR - edge)) / edge : flat;
                    if (sd <= swampR - edge / 2)
                    {
                        biome = Biome.Swamp;
                        surface = BlockType.Mud;
                    }
                }
                else if (biome == Biome.Grassland && Noise.Value(x, z, Math.Max(6, Sx(40)), _seed + 6) > 138)
                {
                    biome = Biome.Forest;
                }
            }

            // River: cuts through everything on land, with sandy banks. Too deep to wade except at the
            // fords, where it is one block deep and the banks come down in one-block steps.
            if (shore >= 0)
            {
                int dr = Math.Abs(x - RiverCentre(z));
                int hw = RiverHalfWidth;
                bool ford = IsFord(z);
                if (dr <= hw)
                {
                    h = _water - 1 - (dr < hw && !ford ? Math.Max(1, Sy(2)) - 1 : 0);
                    biome = Biome.River;
                    surface = BlockType.Sand;
                }
                else if (ford)
                {
                    int ramp = _water - 1 + (dr - hw);
                    if (ramp < h) { h = ramp; if (dr <= hw + 2) surface = BlockType.Sand; }
                }
                else if (dr <= hw + Sx(5) && h > _water + 1)
                {
                    int bank = _water + 1 + (dr - hw) * Sy(2);
                    if (bank < h) { h = bank; if (dr <= hw + 1) surface = BlockType.Sand; }
                }
            }

            h = Math.Clamp(h, 1, _sy - Math.Max(3, Sy(8)));
            _height[i] = h;
            _surface[i] = surface;
            _map.SetBiome(x, z, biome);
        }

        private void PlaceStarts()
        {
            int z = _sz / 2;
            int west = Math.Min(CoastWest(z) + Sx(10) + Sx(24), RiverCentre(z) - RiverHalfWidth - Sx(40));
            int east = Math.Max(_sx - 1 - CoastEast(z) - Sx(10) - Sx(24), RiverCentre(z) + RiverHalfWidth + Sx(40));
            for (int p = 0; p < 2; p++)
            {
                int x = Math.Clamp(p == 0 ? west : east, 0, _sx - 1);
                int y = Math.Max(_water + 2, _height[Idx(x, z)]);
                y = Math.Min(y, _water + Sy(6));
                int r = Math.Max(2, Sx(9));
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx < 0 || cz < 0 || cx >= _sx || cz >= _sz || dx * dx + dz * dz > r * r) continue;
                    int i = Idx(cx, cz);
                    _height[i] = y;
                    _surface[i] = BlockType.Grass;
                    _map.SetBiome(cx, cz, Biome.Grassland);
                }
                _map.Starts.Add(new StartArea(p, x, y, z));
            }
        }

        private void Fill()
        {
            for (int z = 0; z < _sz; z++)
            for (int x = 0; x < _sx; x++)
            {
                int i = Idx(x, z);
                int h = _height[i];
                var top = _surface[i];
                var sub = top switch
                {
                    BlockType.Sand => BlockType.Sand,
                    BlockType.Mud => BlockType.Mud,
                    BlockType.Stone => BlockType.Stone,
                    _ => BlockType.Soil,
                };
                for (int y = 0; y <= h; y++)
                {
                    int depth = h - y;
                    BlockType b = y == 0 ? BlockType.Bedrock
                        : depth == 0 ? top
                        : depth < 3 ? sub
                        : DeepBlock(x, y, z);
                    _w.Set(x, y, z, b);
                }
                for (int y = h + 1; y <= _water; y++) _w.Set(x, y, z, BlockType.Water);
            }
        }

        private BlockType DeepBlock(int x, int y, int z)
        {
            int r = Noise.Permille(x, y, z, _seed ^ 0xA5A5UL);
            if (r < 7) return BlockType.IronOre;
            if (r < 13) return BlockType.Coal;
            if (r < 14 && y < _deep + Sy(6)) return BlockType.Crystal;
            // Deeper down, a little more crystal still: up to twice as much at the bottom of the deep rock.
            if (y < _deep && r < 14 + 7 * (_deep - y) / _deep) return BlockType.Crystal;
            return BlockType.Stone;
        }

        // --- Lava -----------------------------------------------------------------------------------

        /// <summary>
        /// A few round pools of lava in the hills, well away from the starts: the ground in a pool is levelled
        /// to its lowest column, lava on a stone bed, ringed with stone. Their own random stream, so adding
        /// them did not move anything else on existing seeds.
        /// </summary>
        private void PlaceLava()
        {
            var rng = new DeterministicRandom(_seed ^ 0x1A7A9001UL);
            int pools = Math.Clamp(_sx / 160, 1, 4), placed = 0;
            int r = Math.Max(1, Sx(4));
            for (int tries = 0; tries < 60 && placed < pools; tries++)
            {
                int cx = _hillX + rng.Range(-_hillR * 2 / 3, _hillR * 2 / 3 + 1);
                int cz = _hillZ + rng.Range(-_hillR * 2 / 3, _hillR * 2 / 3 + 1);
                if (TryPool(cx, cz, r)) placed++;
            }
            // Two more in each further hill range (issue #67), from a stream of their own.
            var more = new DeterministicRandom(_seed ^ 0x1A7A9002UL);
            for (int k = 1; k < _hills.Count; k++)
            {
                var (hx, hz, hr) = _hills[k];
                for (int tries = 0, got = 0; tries < 60 && got < 2; tries++)
                    if (TryPool(hx + more.Range(-hr * 2 / 3, hr * 2 / 3 + 1), hz + more.Range(-hr * 2 / 3, hr * 2 / 3 + 1), r)) got++;
            }
        }

        private bool TryPool(int cx, int cz, int r)
        {
            int ring = r + 1;
            if (cx - ring < 1 || cz - ring < 1 || cx + ring >= _sx - 1 || cz + ring >= _sz - 1) return false;
            foreach (var s in _map.Starts)
            {
                long dx = s.X - cx, dz = s.Z - cz;
                long far = StartResourceRadius + ring + Sx(16);
                if (dx * dx + dz * dz < far * far) return false;
            }
            foreach (var cave in _map.Caverns)
                if (Math.Abs(cave.X - cx) <= ring + 2 && Math.Abs(cave.Z - cz) <= ring + 2) return false;
            int low = int.MaxValue;
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (dx * dx + dz * dz > ring * ring) continue;
                int x = cx + dx, z = cz + dz;
                if (_map.BiomeAt(x, z) != Biome.Hills || _w.Get(x, _height[Idx(x, z)], z) == BlockType.Lava) return false;
                if (dx * dx + dz * dz <= r * r) low = Math.Min(low, _height[Idx(x, z)]);
            }
            if (low <= _water + 1 || low < 3) return false;
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                int d2 = dx * dx + dz * dz;
                if (d2 > ring * ring) continue;
                int x = cx + dx, z = cz + dz, i = Idx(x, z);
                bool inside = d2 <= r * r;
                int top = inside ? low : Math.Min(_height[i], low + 1);
                for (int y = top + 1; y <= _height[i]; y++) _w.Set(x, y, z, BlockType.Air);
                for (int y = Math.Max(1, low - 1); y <= top; y++)
                    _w.Set(x, y, z, inside && y == low ? BlockType.Lava : BlockType.Stone);
                _height[i] = top;
            }
            return true;
        }

        // --- Poison bog ------------------------------------------------------------------------------

        /// <summary>
        /// Patches of poison bog (<see cref="BlockType.Bog"/>, see <see cref="HazardSystem"/>) on the swamp's dry
        /// mud, where a noise of its own runs high, and never near a start. Only the top block changes, so
        /// nothing else on existing seeds moves (bar the swamp trees that would have grown on it).
        /// </summary>
        private void PlaceBogs()
        {
            int scale = Math.Max(3, Sx(9)), keepOff = StartResourceRadius + Sx(16);
            for (int z = 0; z < _sz; z++)
            for (int x = 0; x < _sx; x++)
            {
                if (_map.BiomeAt(x, z) != Biome.Swamp) continue;
                int h = _height[Idx(x, z)];
                if (h < _water || _w.Get(x, h, z) != BlockType.Mud || _w.Get(x, h + 1, z) != BlockType.Air) continue;
                if (Noise.Value(x, z, scale, _seed + 40) <= BogNoise || NearStart(x, z, keepOff)) continue;
                _w.Set(x, h, z, BlockType.Bog);
            }
        }

        /// <summary>The bog noise (0 to 255) above which swamp mud is poison bog.</summary>
        private const int BogNoise = 160;

        // --- Sinkholes ------------------------------------------------------------------------------

        private const int SinkholeRadius = HazardSystem.SinkholeRadius;
        /// <summary>Least distance between two sinkholes' middles.</summary>
        private const int SinkholeGap = 24;

        /// <summary>
        /// Sinkholes (see <see cref="HazardSystem.MakeSinkhole"/>). They lie on flat, open grassland, forest and hill ground,
        /// well away from the starts and each other, as many as <see cref="SinkholesPer512"/> per 512 by 512 blocks.
        /// Placed last, from a random stream of their own, so nothing else on existing seeds moves.
        /// </summary>
        private void PlaceSinkholes()
        {
            var rng = new DeterministicRandom(_seed ^ 0x51AC40E5UL);
            int want = Math.Max(1, (int)((long)SinkholesPer512 * _sx * _sz / (512 * 512)));
            int keepOff = StartResourceRadius + Sx(24), r = SinkholeRadius;
            var placed = new List<(int X, int Z)>();
            for (int tries = 0; tries < want * 40 && placed.Count < want; tries++)
            {
                int cx = rng.Range(r + 1, _sx - r - 1), cz = rng.Range(r + 1, _sz - r - 1);
                if (_map.BiomeAt(cx, cz) is not (Biome.Grassland or Biome.Forest or Biome.Hills) || NearStart(cx, cz, keepOff)) continue;
                bool far = true;
                foreach (var p in placed)
                    if ((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz) < SinkholeGap * SinkholeGap) far = false;
                if (!far || !SinkholeFits(cx, cz)) continue;
                HazardSystem.MakeSinkhole(_w, cx, _height[Idx(cx, cz)], cz);
                placed.Add((cx, cz));
            }
        }

        /// <summary>
        /// True if a sinkhole fits around (cx, cz): every column of it (and the ring around it) is level, its top
        /// is natural ground with nothing on it, and there is solid ground under the hollow.
        /// </summary>
        private bool SinkholeFits(int cx, int cz)
        {
            int h = _height[Idx(cx, cz)], r = SinkholeRadius + 1;
            if (h <= _water + 1 || h < 4) return false;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= _sx || z >= _sz || _height[Idx(x, z)] != h) return false;
                if (_w.Get(x, h, z) is not (BlockType.Grass or BlockType.Soil or BlockType.Stone or BlockType.Sand)) return false;
                if (_w.Get(x, h + 1, z) != BlockType.Air || _w.Get(x, h + 2, z) != BlockType.Air) return false;
                if (!BlockInfo.IsSolid(_w.Get(x, h - 1, z)) || !BlockInfo.IsSolid(_w.Get(x, h - 2, z)) || !BlockInfo.IsSolid(_w.Get(x, h - 3, z)))
                    return false;
            }
            return true;
        }

        // --- Quicksand ------------------------------------------------------------------------------

        /// <summary>Least distance between two quicksand patches' middles.</summary>
        private const int QuicksandGap = 20;

        /// <summary>
        /// Patches of quicksand (issue #78, see <see cref="HazardSystem.MakeQuicksand"/>) on the dry sand at the water's
        /// edge, on the beaches and the river banks: well away from the starts and each other, as many as
        /// <see cref="QuicksandPer512"/> per 512 by 512 blocks. Placed after everything else, from a random stream
        /// of their own, so nothing else on existing seeds moves.
        /// </summary>
        private void PlaceQuicksand()
        {
            var rng = new DeterministicRandom(_seed ^ 0x9C1C5A2DUL);
            int want = Math.Max(1, (int)((long)QuicksandPer512 * _sx * _sz / (512 * 512)));
            int keepOff = StartResourceRadius + Sx(24), r = QuicksandRadius;
            var placed = new List<(int X, int Z)>();
            for (int tries = 0; tries < want * 200 && placed.Count < want; tries++)
            {
                int cx = rng.Range(r + 4, _sx - r - 4), cz = rng.Range(r + 4, _sz - r - 4);
                if (!QuicksandFits(cx, cz) || NearStart(cx, cz, keepOff)) continue;
                bool far = true;
                foreach (var p in placed)
                    if ((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz) < QuicksandGap * QuicksandGap) far = false;
                if (!far) continue;
                HazardSystem.MakeQuicksand(_w, cx, _height[Idx(cx, cz)], cz, r);
                placed.Add((cx, cz));
            }
        }

        /// <summary>True if (cx, cz) is dry, open sand just above the water, with the water within three columns of it, and not by a ford.</summary>
        private bool QuicksandFits(int cx, int cz)
        {
            int h = _height[Idx(cx, cz)];
            if (h < _water || h > _water + 1) return false;
            for (int dz = -QuicksandRadius - 3; dz <= QuicksandRadius + 3; dz++)
                if (IsFord(cz + dz)) return false; // never across the way over a ford
            if (_w.Get(cx, h, cz) != BlockType.Sand || _w.Get(cx, h + 1, cz) != BlockType.Air) return false;
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
                if (_height[Idx(cx + dx, cz + dz)] < _water && _w.Get(cx + dx, _water, cz + dz) == BlockType.Water) return true;
            return false;
        }

        // --- Rockfall -------------------------------------------------------------------------------

        /// <summary>Least distance between two rockfalls.</summary>
        private const int RockfallGap = 20;

        /// <summary>
        /// Rockfalls (issue #78, see <see cref="HazardSystem.MakeRockfall"/>) on the steep slopes of the hills: loose
        /// rock on the brink of a cliff at least three blocks high, over scree at its foot, well away from the starts,
        /// the dusk tree and each other, as many as <see cref="RockfallsPer512"/> per 512 by 512 blocks. Placed after
        /// everything else, from a random stream of their own, so nothing else on existing seeds moves.
        /// </summary>
        private void PlaceRockfalls()
        {
            var rng = new DeterministicRandom(_seed ^ 0x40CF4A11UL);
            int want = Math.Max(1, (int)((long)RockfallsPer512 * _sx * _sz / (512 * 512)));
            int keepOff = StartResourceRadius + Sx(24), edge = ScreeReach + 4;
            var placed = new List<(int X, int Z)>();
            for (int tries = 0; tries < want * 400 && placed.Count < want; tries++)
            {
                int cx = rng.Range(edge, _sx - edge), cz = rng.Range(edge, _sz - edge);
                if (_map.BiomeAt(cx, cz) != Biome.Hills || NearStart(cx, cz, keepOff)) continue;
                if (_duskX >= 0 && Math.Max(Math.Abs(cx - _duskX), Math.Abs(cz - _duskZ)) <= DuskClearing + 8) continue;
                bool far = true;
                foreach (var p in placed)
                    if ((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz) < RockfallGap * RockfallGap) far = false;
                if (!far || !RockfallFits(cx, cz, out int dx, out int dz)) continue;
                int top = _w.SurfaceHeight(cx, cz);
                if (HazardSystem.MakeRockfall(_w, cx, top, cz, dx, dz, ScreeReach) == 0) continue;
                placed.Add((cx, cz));
            }
        }

        /// <summary>
        /// True if column (cx, cz) is the brink of a cliff: open natural ground with nothing on it, no lava or water
        /// near, and in direction (dx, dz) the ground drops at least three levels within two columns.
        /// </summary>
        private bool RockfallFits(int cx, int cz, out int dx, out int dz)
        {
            dx = dz = 0;
            int h = _w.SurfaceHeight(cx, cz);
            if (h != _height[Idx(cx, cz)] || h <= _water + 3 || h + 3 >= _sy) return false;
            if (_w.Get(cx, h, cz) is not (BlockType.Grass or BlockType.Soil or BlockType.Stone)) return false;
            for (int y = h + 1; y <= h + 3; y++)
                if (_w.Get(cx, y, cz) != BlockType.Air) return false;
            for (int oz = -3; oz <= 3; oz++)
            for (int ox = -3; ox <= 3; ox++)
            {
                int top = _height[Idx(cx + ox, cz + oz)];
                if (_w.Get(cx + ox, top, cz + oz) is BlockType.Lava or BlockType.Water || _w.Get(cx + ox, top + 1, cz + oz) == BlockType.Water) return false;
            }
            for (int d = 0; d < 4; d++)
            {
                int ddx = WalkGraph.Dirs[d].X, ddz = WalkGraph.Dirs[d].Z;
                if (_height[Idx(cx + ddx, cz + ddz)] <= h - 3 || _height[Idx(cx + 2 * ddx, cz + 2 * ddz)] <= h - 3)
                {
                    dx = ddx; dz = ddz;
                    return true;
                }
            }
            return false;
        }

        // --- Ruins ----------------------------------------------------------------------------------

        /// <summary>Columns round a ruin's walls that no tree grows in, so the ruin stays in sight.</summary>
        private const int RuinClearing = 6;
        /// <summary>Least distance between two ruins' middles, in blocks on a 512 map (scaled with the map).</summary>
        private const int RuinGap = 96;

        /// <summary>
        /// Old stone ruins (issue #67), <see cref="RuinsPer512"/> for each 512 by 512 blocks: the broken walls of a
        /// square stone hall on open, level grassland (not forest, whose wood the players want), well away from the
        /// starts (further than their wood and stone), the dusk tree and each
        /// other. Each has a cracked stone brick floor, walls from one to three blocks high with a doorway and a
        /// breach or two, a corner still standing four high, and fallen stone (<see cref="BlockType.StoneRubble"/>,
        /// cleared for stone) inside and around it, in a clearing no tree grows in. From a random stream of their own.
        /// </summary>
        private void PlaceRuins()
        {
            var rng = new DeterministicRandom(_seed ^ 0x2B1A5UL);
            int want = Math.Max(1, (int)((long)RuinsPer512 * _sx * _sz / (512 * 512)));
            int keepOff = StartResourceRadius + Sx(64), gap = Sx(RuinGap), r = RuinHalf;
            for (int tries = 0; tries < want * 60 && _map.Ruins.Count < want; tries++)
            {
                int cx = rng.Range(r + 3, _sx - r - 3), cz = rng.Range(r + 3, _sz - r - 3);
                if (_map.BiomeAt(cx, cz) != Biome.Grassland || NearStart(cx, cz, keepOff)) continue;
                if (_duskX >= 0 && Math.Max(Math.Abs(cx - _duskX), Math.Abs(cz - _duskZ)) <= DuskClearing + r + 4) continue;
                bool far = true;
                foreach (var p in _map.Ruins)
                    if ((long)(p.X - cx) * (p.X - cx) + (long)(p.Z - cz) * (p.Z - cz) < (long)gap * gap) far = false;
                if (!far || !RuinFits(cx, cz, out int g)) continue;
                BuildRuin(cx, g, cz, rng);
                _map.Ruins.Add((cx, g, cz));
            }
        }

        /// <summary>True if a ruin fits around (cx, cz): dry, natural, open ground within a block of level, under open sky.</summary>
        private bool RuinFits(int cx, int cz, out int g)
        {
            g = _height[Idx(cx, cz)];
            if (g <= _water + 1 || g + 6 >= _sy) return false;
            int r = RuinHalf + 2;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz, h = _height[Idx(x, z)];
                if (Math.Abs(h - g) > 1 || _map.BiomeAt(x, z) != Biome.Grassland) return false;
                if (_w.Get(x, h, z) is not (BlockType.Grass or BlockType.Soil) || _w.Get(x, h + 1, z) != BlockType.Air) return false;
            }
            return true;
        }

        private void BuildRuin(int cx, int g, int cz, DeterministicRandom rng)
        {
            int r = RuinHalf;
            // Level the ground under it to g.
            for (int dz = -r - 1; dz <= r + 1; dz++)
            for (int dx = -r - 1; dx <= r + 1; dx++)
            {
                int x = cx + dx, z = cz + dz, i = Idx(x, z), h = _height[i];
                if (h > g) { for (int y = g + 1; y <= h; y++) _w.Set(x, y, z, BlockType.Air); _w.Set(x, g, z, BlockType.Grass); }
                else if (h < g) { for (int y = h; y < g; y++) _w.Set(x, y, z, BlockType.Soil); _w.Set(x, g, z, BlockType.Grass); }
                _height[i] = g;
            }
            int door = rng.Range(0, 4), corner = rng.Range(0, 4);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, z = cz + dz;
                uint h = DeterministicRandom.Hash(x, z, _seed + 50);
                bool edge = Math.Max(Math.Abs(dx), Math.Abs(dz)) == r;
                if (!edge)
                {
                    // A cracked floor, grass coming through, and fallen stone lying on it.
                    if (h % 10 < 6) _w.Set(x, g, z, BlockType.StoneBrick);
                    if (h / 10 % 10 == 0) _w.Set(x, g + 1, z, BlockType.StoneRubble);
                    continue;
                }
                // The wall: one to three high, a doorway in the middle of one side, now and then a breach.
                int side = dz == -r ? 0 : dx == r ? 1 : dz == r ? 2 : 3;
                bool doorway = side == door && Math.Abs(side % 2 == 0 ? dx : dz) <= 1 && Math.Max(Math.Abs(dx), Math.Abs(dz)) == r
                               && !(Math.Abs(dx) == r && Math.Abs(dz) == r);
                int high = doorway ? 0 : (int)(h % 7) switch { 0 => 0, 1 or 2 => 1, 3 or 4 => 2, _ => 3 };
                bool tall = Math.Abs(dx) == r && Math.Abs(dz) == r && corner == (dx > 0 ? 1 : 0) + (dz > 0 ? 2 : 0);
                if (tall) high = 4;
                for (int y = 1; y <= high; y++) _w.Set(x, g + y, z, BlockType.StoneBrick);
                // What fell off a broken stretch lies at its foot, outside.
                if (high <= 1 && !doorway && h / 100 % 3 == 0)
                {
                    int ox = x + Math.Sign(dx) * (Math.Abs(dx) == r ? 1 : 0), oz = z + Math.Sign(dz) * (Math.Abs(dz) == r ? 1 : 0);
                    if (_w.Get(ox, g + 1, oz) == BlockType.Air) _w.Set(ox, g + 1, oz, BlockType.StoneRubble);
                }
            }
        }

        // --- Cavern ---------------------------------------------------------------------------------

        /// <summary>
        /// A cavern under each hill range: the main one's from the map's main random stream (as it always was), the
        /// further ones' (issue #67) from a stream of their own. The first is <see cref="RegionMap.Cavern"/>.
        /// </summary>
        private void CarveCaverns()
        {
            CarveCavern(_hillX, _hillZ, _rng);
            var more = new DeterministicRandom(_seed ^ 0xCA7E2UL);
            for (int k = 1; k < _hills.Count; k++) CarveCavern(_hills[k].X, _hills[k].Z, more);
        }

        private void CarveCavern(int hillX, int hillZ, DeterministicRandom rng)
        {
            int floor = Math.Max(2, _deep + Sy(4));
            int cy = Math.Clamp(_water - Sy(3), floor + 2, _sy - 4);
            int x = Math.Clamp(hillX + Sx(rng.Range(-30, 31)), 2, _sx - 3);
            int z = Math.Clamp(hillZ + Sz(rng.Range(-20, 21)), 2, _sz - 3);
            int y = cy;
            _map.Caverns.Add((x, y, z));

            int minX = x, maxX = x, minY = y, maxY = y, minZ = z, maxZ = z;
            void Carve(int px, int py, int pz, int r, bool throughSurface)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy * 2 + dz * dz > r * r) continue; // a little flattened
                    int bx = px + dx, by = py + dy, bz = pz + dz;
                    if (!_w.InBounds(bx, by, bz) || by < floor) continue;
                    // Keep a roof under the surface except for the entrance tunnel.
                    if (!throughSurface && by > _height[Idx(bx, bz)] - 3) continue;
                    if (_w.Get(bx, by, bz) == BlockType.Water) continue;
                    _w.Set(bx, by, bz, BlockType.Air);
                    minX = Math.Min(minX, bx); maxX = Math.Max(maxX, bx);
                    minY = Math.Min(minY, by); maxY = Math.Max(maxY, by);
                    minZ = Math.Min(minZ, bz); maxZ = Math.Max(maxZ, bz);
                }
            }

            // A worm of overlapping chambers.
            int dirX = rng.Range(-4, 5), dirZ = rng.Range(-4, 5);
            if (dirX == 0 && dirZ == 0) dirX = 3;
            int steps = 14;
            for (int s = 0; s < steps; s++)
            {
                int r = Math.Max(2, Sy(rng.Range(4, 8)));
                Carve(x, y, z, r, false);
                dirX = Math.Clamp(dirX + rng.Range(-2, 3), -4, 4);
                dirZ = Math.Clamp(dirZ + rng.Range(-2, 3), -4, 4);
                int len = Math.Max(1, Noise.Sqrt(dirX * dirX + dirZ * dirZ));
                x = Math.Clamp(x + dirX * r / len, 3, _sx - 4);
                z = Math.Clamp(z + dirZ * r / len, 3, _sz - 4);
                y = Math.Clamp(y + rng.Range(-1, 2), floor + 2, Math.Max(floor + 2, _water - 1));
            }

            // Entrance: a sloping tunnel from the first chamber up to the surface.
            var (ex, ey, ez) = _map.Caverns[^1];
            int tx = ex, tz = ez, ty = ey;
            int sdx = ex < _sx / 2 ? 1 : -1;
            for (int s = 0; s < _sy * 3 && ty <= _height[Idx(tx, tz)] + 1; s++)
            {
                Carve(tx, ty, tz, Math.Max(1, Sy(2)), true);
                tx = Math.Clamp(tx + sdx, 1, _sx - 2);
                if (s % 2 == 1) ty++;
            }

            // Ore, crystal and a little gold on the cave walls.
            for (int by = minY - 1; by <= maxY + 1; by++)
            for (int bz = minZ - 1; bz <= maxZ + 1; bz++)
            for (int bx = minX - 1; bx <= maxX + 1; bx++)
            {
                if (!_w.InBounds(bx, by, bz)) continue;
                var b = _w.Get(bx, by, bz);
                if (b is not (BlockType.Stone or BlockType.Soil) || by > _height[Idx(bx, bz)] - 2) continue;
                if (!TouchesAir(bx, by, bz)) continue;
                int r = Noise.Permille(bx, by, bz, _seed ^ 0xC4FEUL);
                if (r < 110) _w.Set(bx, by, bz, BlockType.IronOre);
                else if (r < 170) _w.Set(bx, by, bz, BlockType.Crystal);
                else if (r < 230) _w.Set(bx, by, bz, BlockType.Coal);
                else if (r >= 1000 - GoldWallPermille) _w.Set(bx, by, bz, BlockType.GoldOre);
            }
        }

        private bool TouchesAir(int x, int y, int z) =>
            _w.Get(x + 1, y, z) == BlockType.Air && _w.InBounds(x + 1, y, z)
            || _w.Get(x - 1, y, z) == BlockType.Air && _w.InBounds(x - 1, y, z)
            || _w.Get(x, y + 1, z) == BlockType.Air && _w.InBounds(x, y + 1, z) && y + 1 < _height[Idx(x, z)]
            || _w.Get(x, y - 1, z) == BlockType.Air && _w.InBounds(x, y - 1, z)
            || _w.Get(x, y, z + 1) == BlockType.Air && _w.InBounds(x, y, z + 1)
            || _w.Get(x, y, z - 1) == BlockType.Air && _w.InBounds(x, y, z - 1);

        // --- Vegetation -----------------------------------------------------------------------------

        private void PlantTrees()
        {
            // Bigger trees (see VoxelWorld.TreeScale) stand further apart, on their own grid: a cell of 4 such
            // squares rather than 5 blocks, so a forest keeps most of its wood with fewer trees.
            int ts = _w.TreeScale, cell = ts > 1 ? 4 : 5;
            for (int gz = 0; gz * cell * ts < _sz; gz++)
            for (int gx = 0; gx * cell * ts < _sx; gx++)
            {
                uint h = DeterministicRandom.Hash(gx, gz, _seed + 30);
                int x = (gx * cell + (int)(h % (uint)cell)) * ts, z = (gz * cell + (int)(h / (uint)cell % (uint)cell)) * ts;
                // Random vegetation stays out of the start areas so both players get the same mirrored layout.
                if (x >= _sx || z >= _sz || NearStart(x, z, Sx(StartResourceRadius + 4))) continue;
                int chance = _map.BiomeAt(x, z) switch
                {
                    Biome.Forest => 75,
                    Biome.Grassland => 5,
                    Biome.Swamp => 12,
                    Biome.Hills => 8,
                    _ => 0,
                };
                int roll = (int)(h >> 16 & 1023) % 100;
                if (roll < chance)
                {
                    // Trees vary a little: a trunk of 4 or 5 (now and then 6) and one of a few crowns, and each
                    // biome grows its own mix of kinds.
                    var biome = _map.BiomeAt(x, z);
                    var kind = RegionGenerator.KindFor(biome, (int)(DeterministicRandom.Hash(gx, gz, _seed + 31) % 100));
                    int trunk = 4 + (int)(h >> 26 & 1) + ((h >> 29 & 7) == 0 ? 1 : 0);
                    TryTree(x, z, trunk, (int)(h >> 27 & 3), kind);
                }
                else if (roll < chance + 3 && _map.BiomeAt(x, z) is Biome.Grassland or Biome.Forest) TryBush(x, z);
            }
        }

        /// <summary>Columns around the dusk tree that no other tree grows in.</summary>
        private const int DuskClearing = 8;

        /// <summary>
        /// The map's one dusk tree: in the hills, as near as the ground allows to halfway between the west and
        /// east coasts at the hills' middle row, on grass with level ground around it and no lava near. A
        /// gnarled trunk ten blocks tall with roots spread at its foot (<see cref="BlockType.DuskWood"/>) and six
        /// bare branches twisting up and out from its upper half (<see cref="BlockType.DuskBranch"/>). Its leaves only grow once it is healed (see
        /// <see cref="Units.DuskTreeSystem"/>).
        /// </summary>
        private void PlaceDuskTree()
        {
            int cx = _sx / 2, cz = _hillZ;
            for (int r = 0; r <= _hillR; r++)
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                int x = cx + dx, z = cz + dz;
                if (!DuskSiteOk(x, z, out int g)) continue;
                GrowDuskTree(_w, x, g, z, _seed);
                (_duskX, _duskZ) = (x, z);
                return;
            }
        }

        private bool DuskSiteOk(int x, int z, out int g)
        {
            g = -1;
            if (x < 12 || z < 12 || x >= _sx - 12 || z >= _sz - 12) return false;
            if (_map.BiomeAt(x, z) != Biome.Hills || !GroundOk(x, z, out g) || g + 16 >= _sy) return false;
            for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
            {
                int h = _height[Idx(x + dx, z + dz)];
                if (_w.Get(x + dx, h, z + dz) is BlockType.Lava or BlockType.Water) return false;
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) <= 3 && Math.Abs(h - g) > 1) return false;
            }
            return true;
        }

        private bool GroundOk(int x, int z, out int ground)
        {
            ground = _height[Idx(x, z)];
            var top = _w.Get(x, ground, z);
            return ground >= _water && top is BlockType.Grass or BlockType.Soil or BlockType.Mud
                   && _w.Get(x, ground + 1, z) == BlockType.Air;
        }

        /// <summary>
        /// A tree of the kind on the ground at (x, z), if the ground there takes one (see <see cref="Trees.Grow"/>).
        /// </summary>
        private void TryTree(int x, int z, int trunk, int crown = 0, TreeKind kind = TreeKind.Broadleaf)
        {
            // The dusk tree stands alone in its clearing, and no tree hides a ruin.
            if (_duskX >= 0 && Math.Max(Math.Abs(x - _duskX), Math.Abs(z - _duskZ)) <= DuskClearing) return;
            foreach (var (rx, _, rz) in _map.Ruins)
                if (Math.Max(Math.Abs(x - rx), Math.Abs(z - rz)) <= RuinHalf + RuinClearing) return;
            if (!GroundOk(x, z, out int g)) return;
            // A bigger tree's trunk (see VoxelWorld.TreeScale) only stands on ground that is level under it.
            var (x0, z0, s) = Trees.Footprint(_w, x, z);
            for (int k = 0; k < s && s > 1; k++)
            for (int i = 0; i < s; i++)
            {
                int cx = x0 + i, cz = z0 + k;
                if (cx >= _sx || cz >= _sz || !GroundOk(cx, cz, out int cg) || Math.Abs(cg - g) > 1) return;
            }
            Trees.Grow(_w, x, g, z, trunk, crown, kind, _seed);
        }

        /// <summary>True if the square a bigger tree at (x, z) would stand on already holds a trunk.</summary>
        private bool TrunkIn(int x, int z)
        {
            var (x0, z0, s) = Trees.Footprint(_w, x, z);
            for (int k = 0; k < s; k++)
            for (int i = 0; i < s; i++)
            for (int y = 0; y < _sy; y++)
                if (BlockInfo.IsTrunk(_w.Get(x0 + i, y, z0 + k))) return true;
            return false;
        }

        private void TryBush(int x, int z)
        {
            if (GroundOk(x, z, out int g)) _w.Set(x, g + 1, z, BlockType.BerryBush);
        }

        private bool NearStart(int x, int z, int r)
        {
            foreach (var s in _map.Starts)
                if ((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z) <= r * r) return true;
            return false;
        }

        // --- Start resources ------------------------------------------------------------------------

        /// <summary>Mirrored resource layout around each start, so both players get the same basics.</summary>
        private void PlaceStartResources()
        {
            foreach (var s in _map.Starts)
            {
                int inland = s.X < _sx / 2 ? 1 : -1;
                int Ox(int dx) => Math.Clamp(s.X + inland * Sx(dx), 1, _sx - 2);
                int Oz(int dz) => Math.Clamp(s.Z + Sz(dz), 1, _sz - 2);

                // Food: a patch of berry bushes.
                for (int k = 0; k < 6; k++) Ground(Ox(10 + k % 3), Oz(12 + k / 3 * 2), BlockType.BerryBush, 1);
                // Wood: a small grove, mostly broad-leaved trees with two birches and a pine.
                for (int k = 0; k < 8; k++)
                {
                    int tx = Ox(10 + k % 4 * 6), tz = Oz(-12 - k / 4 * 6 - k % 2 * 2);
                    if (_w.TreeScale > 1 && TrunkIn(tx, tz)) continue; // bigger trees: this one would stand in the last
                    Ground(tx, tz, BlockType.Air, 0);
                    TryTree(tx, tz, 4 + k % 2, 0, k % 4 == 1 ? TreeKind.Birch : k == 7 ? TreeKind.Pine : TreeKind.Broadleaf);
                }
                // Stone: an outcrop.
                Mound(Ox(22), Oz(4), BlockType.Stone, BlockType.Stone);
                // Iron: an outcrop with ore showing at the surface.
                Mound(Ox(24), Oz(-4), BlockType.Stone, BlockType.IronOre);
            }
        }

        /// <summary>Makes sure (x,z) is dry grass at the start's level, then optionally places a block on it.</summary>
        private void Ground(int x, int z, BlockType place, int height)
        {
            int g = _height[Idx(x, z)];
            if (g < _water + 1 || _w.Get(x, g, z) is not (BlockType.Grass or BlockType.Soil or BlockType.Mud))
            {
                g = Math.Max(g, _water + 1);
                for (int y = 1; y <= g; y++) if (_w.Get(x, y, z) is BlockType.Water or BlockType.Air) _w.Set(x, y, z, BlockType.Soil);
                _w.Set(x, g, z, BlockType.Grass);
                _height[Idx(x, z)] = g;
            }
            for (int y = g + 1; y < Math.Min(_sy, g + 8); y++) _w.Set(x, y, z, BlockType.Air);
            if (height > 0 && place != BlockType.Air) _w.Set(x, g + 1, z, place);
        }

        private void Mound(int x, int z, BlockType body, BlockType core)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int bx = Math.Clamp(x + dx, 0, _sx - 1), bz = Math.Clamp(z + dz, 0, _sz - 1);
                Ground(bx, bz, BlockType.Air, 0);
                int g = _height[Idx(bx, bz)];
                bool centre = dx == 0 && dz == 0;
                _w.Set(bx, g + 1, bz, centre || (dx + dz) % 2 == 0 ? core : body);
                if (centre && g + 2 < _sy) _w.Set(bx, g + 2, bz, core);
            }
        }
    }
}

/// <summary>The kinds of tree the map grows (see <see cref="RegionGenerator.KindFor"/>).</summary>
public enum TreeKind : byte
{
    /// <summary>The broad-leaved tree: a brown trunk and a round green crown.</summary>
    Broadleaf,
    /// <summary>A tall pine with tiers of dark needles.</summary>
    Pine,
    /// <summary>A slender white birch with a small, light crown.</summary>
    Birch,
    /// <summary>A swamp willow with curtains of leaves hanging from its rim.</summary>
    Willow,
    /// <summary>A giant mushroom in the swamp: a pale stalk and a red cap.</summary>
    Mushroom,
    /// <summary>A gloom pine in the shadowed wilds: a pine taller and gaunter than most, with blue-black needles.</summary>
    Gloom,
}
