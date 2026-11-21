using System;

namespace Rts.Sim.World;

public static partial class RegionGenerator
{
    /// <summary>Gold lodes (see <see cref="BlockType.GoldOre"/>) in each hill range.</summary>
    public const int GoldLodesPerHill = 4;

    /// <summary>The least distance (columns, on a 512 map) from a start to a hill range's gold.</summary>
    public const int GoldKeepOff = StartResourceRadius + 40;

    /// <summary>Permille of a cavern's or deep cave's bare stone wall that is gold ore.</summary>
    public const int GoldWallPermille = 15;

    private sealed partial class Builder
    {
        /// <summary>
        /// Gold: a few winding lodes in each hill range, just under the rock and showing on it where the lode runs
        /// close, well away from the starts. Only stone turns to gold, from a random stream of its own, after everything
        /// else is laid out, so nothing else on existing seeds moved. (The caves' walls get their specks of gold as
        /// their ore is laid, see <see cref="GoldWallPermille"/>.)
        /// </summary>
        private void PlaceGold()
        {
            var rng = new DeterministicRandom(_seed ^ 0x601D0DE5UL);
            int keepOff = Sx(GoldKeepOff), step = Math.Max(1, Sx(2)), r = Math.Max(1, Sx(2));
            foreach (var (hx, hz, hr) in _hills)
            {
                for (int tries = 0, got = 0; tries < 80 && got < GoldLodesPerHill; tries++)
                {
                    int x = hx + rng.Range(-hr * 3 / 4, hr * 3 / 4 + 1), z = hz + rng.Range(-hr * 3 / 4, hr * 3 / 4 + 1);
                    int dx = rng.Range(-1, 2), dz = rng.Range(-1, 2), length = 5 + rng.Range(0, 4);
                    if (dx == 0 && dz == 0) dx = 1;
                    if (!LodeFits(x, z, keepOff)) continue;
                    for (int s = 0; s < length && LodeFits(x, z, keepOff); s++)
                    {
                        Nugget(x, _height[Idx(x, z)] - r, z, r);
                        x += dx * step;
                        z += dz * step;
                        // The lode wanders a little.
                        if (rng.Range(0, 3) == 0) (dx, dz) = (Math.Clamp(dx + rng.Range(-1, 2), -1, 1), Math.Clamp(dz + rng.Range(-1, 2), -1, 1));
                        if (dx == 0 && dz == 0) dz = 1;
                    }
                    got++;
                }
            }
        }

        /// <summary>True if a lode may run through column (x, z): dry hills rock, away from the starts.</summary>
        private bool LodeFits(int x, int z, int keepOff)
        {
            if (x < 2 || z < 2 || x >= _sx - 2 || z >= _sz - 2) return false;
            if (_map.BiomeAt(x, z) != Biome.Hills || NearStart(x, z, keepOff)) return false;
            int h = _height[Idx(x, z)];
            return h > _water + 2 && _w.Get(x, h, z) == BlockType.Stone;
        }

        /// <summary>Turns the stone within r blocks of (x, y, z) into gold ore.</summary>
        private void Nugget(int x, int y, int z, int r)
        {
            for (int by = y - r; by <= y + r; by++)
            for (int bz = z - r; bz <= z + r; bz++)
            for (int bx = x - r; bx <= x + r; bx++)
            {
                int d2 = (bx - x) * (bx - x) + (by - y) * (by - y) + (bz - z) * (bz - z);
                if (d2 > r * r + r || !_w.InBounds(bx, by, bz) || by < 1) continue;
                if (_w.Get(bx, by, bz) == BlockType.Stone) _w.Set(bx, by, bz, BlockType.GoldOre);
            }
        }
    }
}
