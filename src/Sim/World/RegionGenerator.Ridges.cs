using System;
using System.Collections.Generic;

namespace Rts.Sim.World;

public static partial class RegionGenerator
{
    /// <summary>Blocks a ridge stands above the hill it runs across (for a 64-layer map; scaled), before its rough top.</summary>
    public const int RidgeLift = 9;
    /// <summary>Columns from a ridge's middle line to its face (for a 512 map; scaled): it is twice this and one thick.</summary>
    public const int RidgeHalf = 2;
    /// <summary>Columns from a pass's middle to each of its walls (for a 512 map; scaled).</summary>
    public const int PassHalf = 3;
    /// <summary>Columns out from a ridge's face over which the ground is eased to the pass's floor (for a 512 map; scaled).</summary>
    public const int PassRamp = 10;
    /// <summary>Columns along the ridge each side of a pass whose rock is seamed with iron and gold (for a 512 map; scaled).</summary>
    public const int VeinReach = 10;
    /// <summary>Of each thousand blocks of rock in a pass's walls, how many are iron ore, and how many after those gold ore.</summary>
    public const int VeinIronPermille = 150, VeinGoldPermille = 30;

    private sealed partial class Builder
    {
        /// <summary>
        /// One ridge: the hill range's (cx, cz) middle of its line, its direction (ux, uz) of length len / 16 (len is
        /// sixteen times the length, for precision), and the pass across it at tp columns along the line from the middle.
        /// </summary>
        private readonly List<(int Cx, int Cz, int Ux, int Uz, int Len, int Tp, int R)> _ridges = new();
        /// <summary>The blocks each column was raised by its ridge (0 off the ridges).</summary>
        private int[]? _ridgeLift;

        private int RidgeAt(int x, int z) => _ridgeLift is { } l ? l[Idx(x, z)] : 0;

        /// <summary>
        /// The design doc's mountain passes, guarded by stone giants: across each hill range a ridge of sheer stone
        /// runs roughly west to east, a third of the way from the range's middle towards the map's nearer north or
        /// south edge, <see cref="RidgeLift"/> blocks above the hill (too high to climb) and
        /// 2 * <see cref="RidgeHalf"/> + 1 columns thick, stopping where the hills do, or where the river cuts
        /// through. Halfway or nearer along it a pass 2 * <see cref="PassHalf"/> + 1 columns wide goes through on a
        /// level floor, the ground on each side eased to it over <see cref="PassRamp"/> columns so it can be walked
        /// into, so the pass is the short way over the hills. Each is kept in <see cref="RegionMap.Passes"/> (the
        /// stone giants stand in them, see <c>MonsterSystem.PlaceWild</c>). Their own random stream, so nothing
        /// else's random choices moved. Runs on the height map, before the blocks are filled.
        /// </summary>
        private void RaiseRidges()
        {
            var rng = new DeterministicRandom(_seed ^ 0x51D6E5A5UL);
            _ridgeLift = new int[_sx * _sz];
            int half = Sx(RidgeHalf), pass = Sx(PassHalf), ramp = Sx(PassRamp), lift = Sy(RidgeLift);
            foreach (var (hx, hz, hr) in _hills)
            {
                int ux = 8, uz = rng.Range(-3, 4), len = Noise.Sqrt((ux * ux + uz * uz) * 256);
                int cx = hx, cz = Math.Clamp(hz + (hz < _sz / 2 ? -hr / 3 : hr / 3), 1, _sz - 2);
                // The pass: on the hills, at least half the range's width from the river (or its banks) and above the
                // water, at most half the range's radius along the line from its middle.
                int tp = 0, px = 0, pz = 0;
                bool found = false;
                for (int k = 0; k < 32 && !found; k++)
                {
                    tp = rng.Range(-hr / 2, hr / 2 + 1);
                    px = cx + tp * ux * 16 / len; pz = cz + tp * uz * 16 / len;
                    if (px < ramp + 2 || pz < ramp + 2 || px >= _sx - ramp - 2 || pz >= _sz - ramp - 2) continue;
                    found = _map.BiomeAt(px, pz) == Biome.Hills && _height[Idx(px, pz)] > _water + 2
                        && Math.Abs(px - RiverCentre(pz)) > RiverHalfWidth + Sx(5) + ramp + pass;
                }
                if (!found) continue;
                tp = ((px - cx) * ux + (pz - cz) * uz) * 16 / len; // where the pass's column lies along the line
                int floor = _height[Idx(px, pz)];
                _ridges.Add((cx, cz, ux, uz, len, tp, hr));
                _map.Passes.Add((px, floor, pz));

                int reach = hr + Sx(40);
                for (int z = Math.Max(1, cz - reach); z < Math.Min(_sz - 1, cz + reach); z++)
                for (int x = Math.Max(1, cx - reach); x < Math.Min(_sx - 1, cx + reach); x++)
                {
                    int dx = x - cx, dz = z - cz;
                    int t = (dx * ux + dz * uz) * 16 / len, c = Math.Abs(dx * uz - dz * ux) * 16 / len;
                    if (c > half + ramp) continue;
                    int i = Idx(x, z);
                    if (_map.BiomeAt(x, z) != Biome.Hills || _height[i] <= _water + 1) continue;
                    if (Math.Abs(t - tp) <= pass)
                    {
                        // The pass: a level floor between the walls, eased to it outside them a block a column.
                        int off = Math.Max(0, c - half);
                        _height[i] = Math.Clamp(_height[i], floor - off, floor + off);
                        if (off == 0) _surface[i] = BlockType.Stone;
                    }
                    else if (c <= half && _ridgeLift[i] == 0)
                    {
                        int rough = Noise.Value(x, z, Math.Max(3, Sx(6)), _seed + 21) * Sy(3) / 256;
                        int up = Math.Min(lift + rough, _sy - Math.Max(3, Sy(8)) - _height[i]);
                        if (up <= 1) continue;
                        _height[i] += up;
                        _surface[i] = BlockType.Stone;
                        _ridgeLift[i] = up;
                    }
                }
            }
        }

        /// <summary>
        /// The iron vein the stone giant guards: the rock a ridge was raised by, within <see cref="VeinReach"/> columns
        /// along it each side of its pass, is seamed with iron ore and a little gold, so the pass's walls are a mine
        /// once the giant is beaten. Runs after the blocks are filled.
        /// </summary>
        private void SeamPasses()
        {
            if (_ridgeLift is null) return;
            int half = Sx(RidgeHalf), reach = Sx(PassHalf) + Sx(VeinReach);
            for (int k = 0; k < _ridges.Count; k++)
            {
                var (cx, cz, ux, uz, len, tp, _) = _ridges[k];
                var (px, _, pz) = _map.Passes[k];
                for (int z = Math.Max(1, pz - reach - half); z <= Math.Min(_sz - 2, pz + reach + half); z++)
                for (int x = Math.Max(1, px - reach - half); x <= Math.Min(_sx - 2, px + reach + half); x++)
                {
                    int i = Idx(x, z), up = _ridgeLift[i];
                    if (up == 0) continue;
                    int dx = x - cx, dz = z - cz;
                    if (Math.Abs((dx * ux + dz * uz) * 16 / len - tp) > reach) continue;
                    for (int y = _height[i] - up + 1; y <= _height[i]; y++)
                    {
                        if (_w.Get(x, y, z) != BlockType.Stone) continue;
                        int r = Noise.Permille(x, y, z, _seed ^ 0x6E1AUL);
                        if (r < VeinIronPermille) _w.Set(x, y, z, BlockType.IronOre);
                        else if (r < VeinIronPermille + VeinGoldPermille) _w.Set(x, y, z, BlockType.GoldOre);
                    }
                }
            }
        }

        /// <summary>
        /// Brings the ridges down round where a cavern's way in comes up (within <see cref="PassHalf"/> columns plus
        /// one), so no cavern opens onto a ridge's top, out of reach.
        /// </summary>
        private void OpenMouths()
        {
            if (_ridgeLift is null) return;
            int r = Sx(PassHalf) + 1;
            foreach (var (mx, mz) in _caveMouths)
                for (int z = Math.Max(1, mz - r); z <= Math.Min(_sz - 2, mz + r); z++)
                for (int x = Math.Max(1, mx - r); x <= Math.Min(_sx - 2, mx + r); x++)
                    Lower(x, z);
        }

        /// <summary>Takes a ridge's rock off column (x, z), back down to the hill it stood on.</summary>
        private void Lower(int x, int z)
        {
            int i = Idx(x, z), up = _ridgeLift![i];
            if (up == 0) return;
            for (int y = _height[i] - up + 1; y <= _height[i]; y++) _w.Set(x, y, z, BlockType.Air);
            _height[i] -= up;
            _ridgeLift[i] = 0;
        }

        /// <summary>True if (x, z) is within r columns of a pass, or on a ridge: kept clear of hazards, pools and trees.</summary>
        private bool NearPass(int x, int z, int r)
        {
            if (RidgeAt(x, z) > 0) return true;
            foreach (var p in _map.Passes)
                if ((long)(p.X - x) * (p.X - x) + (long)(p.Z - z) * (p.Z - z) <= (long)r * r) return true;
            return false;
        }

        /// <summary>Columns round a pass's middle kept clear of hazards, lava, lodes and trees.</summary>
        private int PassClear => Sx(PassHalf) + Sx(RidgeHalf) + Sx(PassRamp);
    }
}
