using System;
using System.Collections.Generic;

namespace Rts.Sim.World;

public static partial class RegionGenerator
{
    /// <summary>The smallest map (blocks across, both ways) that has shadowed wilds (test and scripted maps have none).</summary>
    public const int WildsMapSize = 384;

    /// <summary>How far the shadowed wilds reach from the middle of the map (columns, on a 512 map), before their ragged edge.</summary>
    public const int WildsRadius = 48;

    /// <summary>The least distance (columns, on a 512 map) from a start to the shadowed wilds.</summary>
    public const int WildsKeepOff = StartResourceRadius + 80;

    /// <summary>Shadow crystal outcrops in the shadowed wilds, on a 512 map (more on a bigger one).</summary>
    public const int WildsCrystals = 4;

    /// <summary>The least distance (columns, on a 512 map) between two shadow crystal outcrops.</summary>
    public const int CrystalGap = 24;

    /// <summary>In a hundred, the chance a cell of the gloom pines' grid grows one (a dense, dark forest).</summary>
    public const int GloomTreeChance = 45;

    private sealed partial class Builder
    {
        /// <summary>
        /// The shadowed wilds (the design doc's far interior): round the middle of the map, over the Heartscar, every
        /// grassland and forest column within <see cref="WildsRadius"/> (with a ragged edge) and well away from the
        /// starts becomes <see cref="Biome.Wilds"/>. Its grass turns to gloom moss (<see cref="BlockType.Gloom"/>), the
        /// crowns of the broad-leaved trees and birches in it turn blue-black (<see cref="BlockType.GloomLeaves"/>) and gloom pines grow
        /// close together between them, and a few outcrops of shadow crystal (<see cref="BlockType.Crystal"/>) stand out of
        /// its ground. Hills, swamp, river and beach inside it stay as they are. Laid out last, from random streams and
        /// hashes of its own, so nothing else on existing seeds moved; a map smaller than <see cref="WildsMapSize"/> has none.
        /// </summary>
        private void PlaceWilds()
        {
            if (_sx < WildsMapSize || _sz < WildsMapSize) return;
            int cx = _sx / 2, cz = _sz / 2, r = Sx(WildsRadius), keepOff = Sx(WildsKeepOff);
            var cols = new List<int>();
            for (int z = Math.Max(0, cz - r * 2); z < Math.Min(_sz, cz + r * 2); z++)
            for (int x = Math.Max(0, cx - r * 2); x < Math.Min(_sx, cx + r * 2); x++)
            {
                int wobble = (Noise.Value(x, z, Math.Max(4, Sx(24)), _seed + 40) - 128) * Sx(12) / 128;
                int d = Noise.Sqrt((long)(x - cx) * (x - cx) + (long)(z - cz) * (z - cz)) + wobble;
                if (d >= r || _map.BiomeAt(x, z) is not (Biome.Grassland or Biome.Forest) || NearStart(x, z, keepOff)) continue;
                _map.SetBiome(x, z, Biome.Wilds);
                cols.Add(Idx(x, z));
            }
            if (cols.Count == 0) return;
            PlantGloomTrees(cx, cz, r);
            foreach (int i in cols)
            {
                int x = i % _sx, z = i / _sx, g = _height[i];
                // The broad-leaved trees and birches standing in it keep their trunks, but their crowns turn blue-black.
                for (int y = g + 1, top = Math.Min(_sy, g + 32); y < top; y++)
                    if (_w.Get(x, y, z) is BlockType.Leaves or BlockType.BirchLeaves) _w.Set(x, y, z, BlockType.GloomLeaves);
                if (_w.Get(x, g, z) == BlockType.Grass) _w.Set(x, g, z, BlockType.Gloom);
            }
            PlaceShadowCrystal(cols);
        }

        /// <summary>Gloom pines on a grid of their own over the wilds, where no other tree stands near.</summary>
        private void PlantGloomTrees(int cx, int cz, int r)
        {
            int ts = _w.TreeScale, cell = ts > 1 ? 4 : 5, step = cell * ts;
            for (int gz = Math.Max(0, (cz - r * 2) / step); gz * step < Math.Min(_sz, cz + r * 2); gz++)
            for (int gx = Math.Max(0, (cx - r * 2) / step); gx * step < Math.Min(_sx, cx + r * 2); gx++)
            {
                uint h = DeterministicRandom.Hash(gx, gz, _seed + 41);
                int x = (gx * cell + (int)(h % (uint)cell)) * ts, z = (gz * cell + (int)(h / (uint)cell % (uint)cell)) * ts;
                if (x >= _sx || z >= _sz || _map.BiomeAt(x, z) != Biome.Wilds) continue;
                if ((int)(h >> 16 & 1023) % 100 >= GloomTreeChance || TrunkNear(x, z, 2 * ts + 1)) continue;
                int trunk = 4 + (int)(h >> 26 & 1) + (int)(h >> 29 & 1);
                TryTree(x, z, trunk, (int)(h >> 27 & 3), TreeKind.Gloom);
            }
        }

        /// <summary>True if a tree's trunk stands within <paramref name="reach"/> columns of (x, z), near the ground.</summary>
        private bool TrunkNear(int x, int z, int reach)
        {
            for (int bz = Math.Max(0, z - reach); bz <= Math.Min(_sz - 1, z + reach); bz++)
            for (int bx = Math.Max(0, x - reach); bx <= Math.Min(_sx - 1, x + reach); bx++)
            {
                int g = _height[Idx(bx, bz)];
                for (int y = g + 1; y <= Math.Min(_sy - 1, g + 2); y++)
                    if (BlockInfo.IsTrunk(_w.Get(bx, y, bz)) || BlockInfo.IsDuskTree(_w.Get(bx, y, bz))) return true;
            }
            return false;
        }

        /// <summary>
        /// Shadow crystal: <see cref="WildsCrystals"/> outcrops (more on a bigger map) on level, open gloom moss in the
        /// wilds, at least <see cref="CrystalGap"/> apart and off the river's banks. Each is a mound three columns across
        /// with a spire four blocks tall in its middle, two under each side and one under each corner, all crystal, with
        /// crystal two blocks down into the ground under it.
        /// </summary>
        private void PlaceShadowCrystal(List<int> cols)
        {
            var rng = new DeterministicRandom(_seed ^ 0x5AAD0C2EUL);
            int want = WildsCrystals * FeatureSets(_sx, _sz), gap = Sx(CrystalGap), placed = 0;
            var spots = new List<(int X, int Z)>();
            for (int tries = 0; tries < 400 && placed < want; tries++)
            {
                int i = cols[rng.Range(0, cols.Count)];
                int x = i % _sx, z = i / _sx;
                if (!CrystalFits(x, z, gap, spots)) continue;
                int g = _height[i];
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int bx = x + dx, bz = z + dz, tall = dx == 0 && dz == 0 ? 4 : dx == 0 || dz == 0 ? 2 : 1;
                    for (int y = g - 1; y <= g + tall; y++) _w.Set(bx, y, bz, BlockType.Crystal);
                }
                spots.Add((x, z));
                placed++;
            }
        }

        private bool CrystalFits(int x, int z, int gap, List<(int X, int Z)> spots)
        {
            if (x < 4 || z < 4 || x >= _sx - 4 || z >= _sz - 4) return false;
            if (Math.Abs(x - RiverCentre(z)) <= RiverHalfWidth + Sx(10)) return false;
            foreach (var (sx, sz) in spots)
                if ((sx - x) * (sx - x) + (sz - z) * (sz - z) < gap * gap) return false;
            foreach (var (rx, _, rz) in _map.Ruins)
                if (Math.Max(Math.Abs(x - rx), Math.Abs(z - rz)) <= RuinHalf + RuinClearing) return false;
            int g = _height[Idx(x, z)];
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int bx = x + dx, bz = z + dz;
                if (_map.BiomeAt(bx, bz) != Biome.Wilds || _height[Idx(bx, bz)] != g) return false;
                if (_w.Get(bx, g, bz) is not (BlockType.Grass or BlockType.Gloom)) return false;
                for (int y = g + 1; y <= Math.Min(_sy - 1, g + 6); y++)
                    if (_w.Get(bx, y, bz) != BlockType.Air) return false;
            }
            return true;
        }
    }
}
