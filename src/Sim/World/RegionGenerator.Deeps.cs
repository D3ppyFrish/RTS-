using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.World;

public static partial class RegionGenerator
{
    /// <summary>The layers the land is laid out in (water level, hills, the hill caverns): a 64-high map's.</summary>
    public const int SurfaceLayers = 64;

    /// <summary>The fewest layers of deep rock that get the deep caves (see <see cref="DeepLayers"/>).</summary>
    public const int MinDeepLayers = 32;

    /// <summary>About how far apart (columns) the deep caves' chambers are, each way, on each of their two levels.</summary>
    public const int DeepSpacing = 120;

    /// <summary>The least rock (layers) kept over a deep cave, under the ground above it.</summary>
    public const int DeepRoof = 6;

    /// <summary>Every this many of the deep caves' chambers (<see cref="RegionMap.Deeps"/>, from the first) is a monster's nest.</summary>
    public const int DeepNestEvery = 4;

    /// <summary>
    /// The layers of deep rock a map this high has under its land: everything over <see cref="SurfaceLayers"/>, when that is
    /// at least <see cref="MinDeepLayers"/> (the default map's 64); none on a lower map, which is laid out as it always was.
    /// The land, the water and the hill caverns sit on top of the deep rock, and the deep caves run through it.
    /// </summary>
    public static int DeepLayers(int sizeY) => sizeY >= SurfaceLayers + MinDeepLayers ? sizeY - SurfaceLayers : 0;

    private sealed partial class Builder
    {
        // --- Deep caves -----------------------------------------------------------------------------

        /// <summary>
        /// The deep caves, on a map with deep rock under its land: two levels of chambers on a grid across most of
        /// the map (about <see cref="DeepSpacing"/> apart), one about a quarter of the way down the deep rock and
        /// one about three quarters, each chamber joined by winding tunnels to its neighbours, the levels joined by
        /// sloping shafts, each hill cavern joined to the nearest chamber of the upper level, and a few long ramps up
        /// to open country. Then the chambers are furnished (see <see cref="Furnish"/>). Their own random stream,
        /// so the land above is as it was.
        /// </summary>
        private void CarveDeeps()
        {
            if (_deep < MinDeepLayers) return;
            var rng = new DeterministicRandom(_seed ^ 0xDEE9CA7EUL);
            int upperY = _deep - _deep / 4, lowerY = Math.Max(8, _deep / 4);
            int mx = Sx(56), mz = Sz(32);
            int cols = Math.Max(2, (_sx - 2 * mx) / DeepSpacing + 1), rows = Math.Max(2, (_sz - 2 * mz) / DeepSpacing + 1);
            int stepX = (_sx - 2 * mx) / (cols - 1), stepZ = (_sz - 2 * mz) / (rows - 1);
            var upper = new (int X, int Y, int Z)[cols * rows];
            var lower = new (int X, int Y, int Z)[cols * rows];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int jx = stepX / 4, jz = stepZ / 4;
                (int, int, int) Node(int y, int jy) => (
                    Math.Clamp(mx + c * stepX + rng.Range(-jx, jx + 1), 14, _sx - 15),
                    y + rng.Range(-jy, jy + 1),
                    Math.Clamp(mz + r * stepZ + rng.Range(-jz, jz + 1), 14, _sz - 15));
                upper[r * cols + c] = Node(upperY, 4);
                lower[r * cols + c] = Node(lowerY, 3);
            }

            foreach (var level in new[] { upper, lower })
            {
                foreach (var n in level) Chamber(n, rng);
                for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (c + 1 < cols) Link(Floor(level[r * cols + c]), Floor(level[r * cols + c + 1]), rng);
                    if (r + 1 < rows) Link(Floor(level[r * cols + c]), Floor(level[(r + 1) * cols + c]), rng);
                }
            }
            // Shafts between the levels: a third of the chambers, and always the first and last.
            for (int k = 0; k < upper.Length; k++)
                if (k == 0 || k == upper.Length - 1 || rng.Range(0, 3) == 0) Link(Floor(upper[k]), Floor(lower[k]), rng);
            // Each hill cavern's first chamber (where its way in comes down) down to the nearest upper chamber.
            foreach (var cave in _map.Caverns)
            {
                int best = 0;
                long bestD = long.MaxValue;
                for (int k = 0; k < upper.Length; k++)
                {
                    long dx = upper[k].X - cave.X, dz = upper[k].Z - cave.Z;
                    if (dx * dx + dz * dz < bestD) (best, bestD) = (k, dx * dx + dz * dz);
                }
                int stand = Walk.NearestStandLevel(_w, cave.X, cave.Z, cave.Y, 8);
                Link((cave.X, stand >= 0 ? stand : cave.Y - 2, cave.Z), Floor(upper[best]), rng, padA: 4);
            }
            _upperDeeps = upper.Length;
            _map.Deeps.AddRange(upper);
            _map.Deeps.AddRange(lower);
            for (int k = 0; k < _map.Deeps.Count; k++)
                Furnish(_map.Deeps[k], k < upper.Length, k % DeepNestEvery == 0, rng);
        }

        /// <summary>The furthest a chamber reaches from its node (columns).</summary>
        private const int ChamberReach = 18;

        /// <summary>How many of <see cref="RegionMap.Deeps"/> are the upper level's.</summary>
        private int _upperDeeps;

        /// <summary>
        /// A few ramps from the deep caves' upper level up to the open country, well away from the starts: the last
        /// thing made, so nothing (a tree, a ruin, a sinkhole) is put in their way.
        /// </summary>
        private void CarveDeepRamps()
        {
            if (_upperDeeps == 0) return;
            var rng = new DeterministicRandom(_seed ^ 0x2A3B5UL);
            int ramps = 0, most = Math.Max(2, _upperDeeps / 6);
            for (int k = 0; k < _upperDeeps && ramps < most; k++)
                if (rng.Range(0, 3) == 0 && Ramp(_map.Deeps[k], rng)) ramps++;
        }

        /// <summary>A node's floor: the layer a unit stands at in its chamber (the node is two above, in the open).</summary>
        private static (int X, int Y, int Z) Floor((int X, int Y, int Z) n) => (n.X, n.Y - 2, n.Z);

        /// <summary>
        /// A chamber: a big domed hall and a few smaller ones overlapping it, all on the node's floor (here and there
        /// a layer higher, never more, so it is walked anywhere).
        /// </summary>
        private void Chamber((int X, int Y, int Z) n, DeterministicRandom rng)
        {
            int floor = n.Y - 2;
            ulong bumps = rng.NextULong();
            void Hall(int cx, int cz, int r, int high)
            {
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int d2 = dx * dx + dz * dz;
                    if (d2 > r * r) continue;
                    int x = cx + dx, z = cz + dz;
                    if (x < 3 || z < 3 || x >= _sx - 3 || z >= _sz - 3) continue;
                    int f = floor + (Noise.Value(x, z, 5, bumps) > 165 ? 1 : 0);
                    Open(x, z, f, f + 2 + (high - 3) * (r * r - d2) / (r * r), false);
                }
            }
            Hall(n.X, n.Z, rng.Range(9, 13), rng.Range(8, 13));
            int blobs = rng.Range(3, 6);
            for (int k = 0; k < blobs; k++) Hall(n.X + rng.Range(-9, 10), n.Z + rng.Range(-9, 10), rng.Range(5, 9), rng.Range(5, 9));
        }

        /// <summary>
        /// A tunnel from floor a to floor b that never climbs more than a layer for each two columns: when b is too far
        /// above or below for that, the tunnel first runs out to one side, climbing or dropping all the way, then on to b.
        /// It keeps level through the chambers at its ends (<paramref name="padA"/> columns at a's, <see cref="ChamberReach"/>
        /// at b's), so it meets their floors.
        /// </summary>
        private void Link((int X, int Y, int Z) a, (int X, int Y, int Z) b, DeterministicRandom rng, int padA = ChamberReach)
        {
            int need = 2 * Math.Abs(b.Y - a.Y) + 4;
            long hx = b.X - a.X, hz = b.Z - a.Z;
            if (Noise.Sqrt(hx * hx + hz * hz) >= need + padA + ChamberReach) { Tunnel(a, b, rng, padA, ChamberReach); return; }
            need += padA;
            int first = rng.Range(0, 8);
            for (int k = 0; k < 8; k++)
            {
                var (dx, dz) = WalkGraphDirs[(first + k) % 8];
                int step = dx != 0 && dz != 0 ? need * 3 / 4 + 1 : need;
                int wx = a.X + dx * step, wz = a.Z + dz * step;
                if (wx < 12 || wz < 12 || wx >= _sx - 12 || wz >= _sz - 12) continue;
                Tunnel(a, (wx, b.Y, wz), rng, padA, 0);
                Tunnel((wx, b.Y, wz), b, rng, 0, ChamberReach);
                return;
            }
        }

        private static readonly (int X, int Z)[] WalkGraphDirs =
            { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

        /// <summary>
        /// A winding tunnel from floor a to floor b, five to seven columns wide and four layers high (three at its
        /// sides), bending off to one side on the way. Its floor changes a layer at most for each two columns along it
        /// (as <see cref="Link"/> makes sure), so it is walked end to end.
        /// </summary>
        private void Tunnel((int X, int Y, int Z) a, (int X, int Y, int Z) b, DeterministicRandom rng, int padA, int padB)
        {
            long hx = b.X - a.X, hz = b.Z - a.Z;
            int len = Math.Max(1, Noise.Sqrt(hx * hx + hz * hz));
            int bend = Math.Min(len / 5, 14) * (rng.Range(0, 2) * 2 - 1);
            ulong ns = rng.NextULong();
            for (int s = 0; s <= len; s++)
            {
                long t = s * 1000L / len;
                int off = (int)(bend * 4 * t * (1000 - t) / 1_000_000) + (Noise.Value(s * 2, 3, 16, ns) - 128) * 3 / 128;
                int x = a.X + (int)(hx * s / len - hz * off / len);
                int z = a.Z + (int)(hz * s / len + hx * off / len);
                int slope = Math.Max(1, len - padA - padB);
                int floor = a.Y + (b.Y - a.Y) * Math.Clamp(s - padA, 0, slope) / slope;
                Corridor(x, z, floor, 2 + Noise.Value(s * 3, 9, 12, ns) / 128, 4, false);
            }
        }

        /// <summary>Opens a round patch of columns, r across, from the floor up: <paramref name="high"/> layers, one less at the edge.</summary>
        private void Corridor(int cx, int cz, int floor, int r, int high, bool throughSurface)
        {
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int d2 = dx * dx + dz * dz;
                if (d2 > r * r + 1) continue;
                int x = cx + dx, z = cz + dz;
                if (x < 3 || z < 3 || x >= _sx - 3 || z >= _sz - 3) continue;
                Open(x, z, floor, floor + high - (d2 >= r * r ? 2 : 1), throughSurface);
            }
        }

        /// <summary>
        /// A ramp from a chamber up to the surface, climbing a layer every two columns in one of the four straight
        /// directions, coming out in open grassland, forest or hills, well away from the starts and the water.
        /// False (and nothing carved) when no direction does.
        /// </summary>
        private bool Ramp((int X, int Y, int Z) n, DeterministicRandom rng)
        {
            int first = rng.Range(0, 4);
            var f = Floor(n);
            for (int k = 0; k < 4; k++)
            {
                var (dx, dz) = WalkGraphDirs[(first + k) % 4 * 2];
                if (!RampFits(f, dx, dz, out int steps)) continue;
                for (int s = 0; s <= steps; s++)
                    Corridor(f.X + dx * s, f.Z + dz * s, f.Y + Math.Max(0, s - ChamberReach) / 2, 1, 4, throughSurface: true);
                return true;
            }
            return false;
        }

        private bool RampFits((int X, int Y, int Z) f, int dx, int dz, out int steps)
        {
            int keepOff = StartResourceRadius + Sx(80);
            for (steps = 0; ; steps++)
            {
                int x = f.X + dx * steps, z = f.Z + dz * steps, y = f.Y + Math.Max(0, steps - ChamberReach) / 2;
                if (x < 8 || z < 8 || x >= _sx - 8 || z >= _sz - 8) return false;
                int h = _height[Idx(x, z)];
                if (y > h + 1) return steps > 0;
                if (y < h - DeepRoof - 4) continue;
                // Near the surface: open, dry country only, the ramp's sides too.
                for (int side = -2; side <= 2; side++)
                {
                    int sx = x + dz * side, sz = z + dx * side;
                    int sh = _height[Idx(sx, sz)];
                    if (_map.BiomeAt(sx, sz) is not (Biome.Grassland or Biome.Forest or Biome.Hills) || sh <= _water + 1
                        || _w.Get(sx, sh, sz) is not (BlockType.Grass or BlockType.Soil or BlockType.Stone)
                        || _w.Get(sx, sh + 1, sz) != BlockType.Air || RidgeAt(sx, sz) > 0) return false;
                }
                if (NearStart(x, z, keepOff) || NearPass(x, z, PassClear)) return false;
                foreach (var cave in _map.Caverns)
                    if (Math.Abs(cave.X - x) + Math.Abs(cave.Z - z) < Sx(24)) return false;
            }
        }

        /// <summary>
        /// Opens column (x, z) from the floor to the top layer into the deep caves, keeping <see cref="DeepRoof"/>
        /// layers under the ground (unless <paramref name="throughSurface"/>), the bottom two layers, and any water,
        /// lava and bedrock; and seams the stone round it with ore (see <see cref="Vein"/>).
        /// </summary>
        private void Open(int x, int z, int floor, int top, bool throughSurface)
        {
            int ground = _height[Idx(x, z)];
            floor = Math.Max(2, floor);
            if (!throughSurface) top = Math.Min(top, ground - DeepRoof);
            top = Math.Min(top, _sy - 2);
            for (int y = floor; y <= top; y++)
            {
                var b = _w.Get(x, y, z);
                if (b is BlockType.Water or BlockType.Lava or BlockType.Bedrock or BlockType.Air) continue;
                _w.Set(x, y, z, BlockType.Air);
            }
            if (floor > top) return;
            Seam(x, floor - 1, z, ground);
            Seam(x, top + 1, z, ground);
            for (int y = floor; y <= top; y++)
            {
                Seam(x + 1, y, z, ground); Seam(x - 1, y, z, ground);
                Seam(x, y, z + 1, ground); Seam(x, y, z - 1, ground);
            }
        }

        private void Seam(int x, int y, int z, int ground)
        {
            if (y <= ground - 3 && _w.Get(x, y, z) == BlockType.Stone) Vein(x, y, z);
        }

        /// <summary>
        /// Ore in the deep caves' walls: iron and coal, a little gold (<see cref="GoldWallPermille"/>), and crystal, which grows richer the deeper it is (half as much
        /// again at the top of the deep rock as in a hill cavern's walls, three times as much at the bottom).
        /// </summary>
        private void Vein(int x, int y, int z)
        {
            int r = Noise.Permille(x, y, z, _seed ^ 0xD33F5UL);
            int crystal = 25 + 50 * (_deep - Math.Min(y, _deep)) / _deep;
            if (r < 70) _w.Set(x, y, z, BlockType.IronOre);
            else if (r < 110) _w.Set(x, y, z, BlockType.Coal);
            else if (r < 110 + crystal) _w.Set(x, y, z, BlockType.Crystal);
            else if (r >= 1000 - GoldWallPermille) _w.Set(x, y, z, BlockType.GoldOre);
        }

        /// <summary>
        /// What is in a deep chamber: crystal and stone spikes rising from the floor and hanging from the roof; in the
        /// upper level, giant mushrooms standing floor to roof and now and then a pool of water; in the lower, pools of
        /// lava or water. A nest chamber (see <see cref="DeepNestEvery"/>) gets no pool, so its monsters stand clear.
        /// </summary>
        private void Furnish((int X, int Y, int Z) n, bool upperLevel, bool nest, DeterministicRandom rng)
        {
            const int reach = 11;
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (dx * dx + dz * dz > reach * reach) continue;
                int x = n.X + dx, z = n.Z + dz;
                if (!OpenColumn(x, n.Y, z, out int f, out int c)) continue;
                int room = c - f + 1;
                int p = Noise.Permille(x, f, z, _seed ^ 0xF0221UL);
                bool crystal = !upperLevel || p % 3 == 0;
                if (p < 22 && room >= 5)
                {
                    int tall = Math.Min(1 + p % 3, room - 3);
                    for (int y = f; y < f + tall; y++) _w.Set(x, y, z, crystal ? BlockType.Crystal : BlockType.Stone);
                }
                else if (p < 48 && room >= 6)
                {
                    int hang = 1 + p % 2;
                    for (int y = c; y > c - hang; y--) _w.Set(x, y, z, crystal ? BlockType.Crystal : BlockType.Stone);
                }
            }

            if (upperLevel)
            {
                int want = rng.Range(2, 5);
                for (int tries = 0, got = 0; tries < 24 && got < want; tries++)
                    if (GiantMushroom(n.X + rng.Range(-8, 9), n.Y, n.Z + rng.Range(-8, 9))) got++;
            }
            if (nest) return;
            int pool = rng.Range(0, 12);
            if (upperLevel ? pool < 4 : pool < 8) Basin(n, !upperLevel && pool < 5 ? BlockType.Lava : BlockType.Water);
        }

        /// <summary>
        /// The open run of air through layer y (or just under it) in column (x, z): its lowest air block, on solid
        /// ground, and its highest, under the roof. False if the column is rock there, or floorless.
        /// </summary>
        private bool OpenColumn(int x, int y, int z, out int floor, out int ceiling)
        {
            floor = ceiling = -1;
            if (!_w.InBounds(x, y, z)) return false;
            int f = y;
            if (_w.Get(x, f, z) != BlockType.Air) return false;
            while (f > 2 && f > y - 12 && _w.Get(x, f - 1, z) == BlockType.Air) f--;
            if (!BlockInfo.CanStandOn(_w.Get(x, f - 1, z))) return false;
            int c = y;
            while (c < y + 12 && c + 1 < _sy && _w.Get(x, c + 1, z) == BlockType.Air) c++;
            if (_w.Get(x, c + 1, z) == BlockType.Air) return false;
            floor = f;
            ceiling = c;
            return true;
        }

        /// <summary>
        /// A giant mushroom standing from the floor to the roof: a pale stalk with its cap round it right under the
        /// rock, so nothing stands on the cap and units walk beneath it. Needs six layers of room and an open floor.
        /// </summary>
        private bool GiantMushroom(int x, int y, int z)
        {
            if (!OpenColumn(x, y, z, out int f, out int c) || c - f + 1 < 6) return false;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (_w.Get(x + dx, f + 2, z + dz) != BlockType.Air || _w.Get(x + dx, c - 1, z + dz) != BlockType.Air) return false;
            for (int by = f; by < c; by++) _w.Set(x, by, z, BlockType.MushroomStem);
            // The cap follows the roof: each block of it right under the rock over its own column.
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (dx * dx + dz * dz > 5) continue;
                int cx = x + dx, cz = z + dz, cy = c;
                while (cy > f + 3 && _w.Get(cx, cy, cz) != BlockType.Air) cy--;
                while (cy < c + 3 && _w.Get(cx, cy + 1, cz) == BlockType.Air) cy++;
                if (_w.Get(cx, cy, cz) == BlockType.Air && _w.Get(cx, cy + 1, cz) != BlockType.Air) _w.Set(cx, cy, cz, BlockType.MushroomCap);
            }
            return true;
        }

        /// <summary>
        /// A pool sunk into a chamber's floor, at the floor's level in the chamber's middle: one layer deep, two where
        /// the pool is all round, and only where rock holds it in on every side, so nothing spills.
        /// </summary>
        private void Basin((int X, int Y, int Z) n, BlockType liquid)
        {
            if (!OpenColumn(n.X, n.Y, n.Z, out int f0, out _)) return;
            const int r = 6, side = 2 * r + 1;
            int top = f0 - 1;
            var inPool = new bool[side * side];
            bool Solid(int x, int y, int z) => _w.Get(x, y, z) is not (BlockType.Air or BlockType.Water or BlockType.Lava);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = n.X + dx, z = n.Z + dz;
                inPool[(dz + r) * side + dx + r] = dx * dx + dz * dz <= r * r && _w.InBounds(x, top - 2, z)
                    && _w.Get(x, f0, z) == BlockType.Air && Solid(x, top, z) && Solid(x, top - 1, z)
                    && _w.Get(x, top, z) != BlockType.MushroomStem;
            }
            bool In(int dx, int dz) => Math.Abs(dx) <= r && Math.Abs(dz) <= r && inPool[(dz + r) * side + dx + r];
            // Keep only what is held in: every side either more pool or rock at the pool's level.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (!In(dx, dz)) continue;
                    foreach (var (sx, sz) in WalkGraph4)
                        if (!In(dx + sx, dz + sz) && !Solid(n.X + dx + sx, top, n.Z + dz + sz))
                        {
                            inPool[(dz + r) * side + dx + r] = false;
                            changed = true;
                            break;
                        }
                }
            }
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (!In(dx, dz)) continue;
                int x = n.X + dx, z = n.Z + dz;
                _w.Set(x, top, z, liquid);
                bool allRound = In(dx + 1, dz) && In(dx - 1, dz) && In(dx, dz + 1) && In(dx, dz - 1);
                if (allRound && Solid(x, top - 2, z)) _w.Set(x, top - 1, z, liquid);
            }
        }

        private static readonly (int X, int Z)[] WalkGraph4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    }
}
