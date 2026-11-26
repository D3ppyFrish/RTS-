using System;
using System.Collections.Generic;

namespace Rts.Sim.World;

public static partial class RegionGenerator
{
    /// <summary>Hill caves (see <see cref="RegionMap.HillCaves"/>) carved into each hill range.</summary>
    public const int HillCavesPerRange = 2;

    /// <summary>Radius (columns) of a hill cave's chamber at its bottom.</summary>
    public const int HillCaveChamber = 6;

    /// <summary>Levels of air over a hill cave's tunnel floor.</summary>
    public const int HillCaveHeadroom = 4;

    /// <summary>The least solid ground (levels) over a hill cave's chamber.</summary>
    public const int HillCaveRoof = 3;

    /// <summary>The longest a hill cave's tunnel runs (columns) from the chamber's middle to its mouth.</summary>
    public const int HillCaveReach = 64;

    /// <summary>The least distance (columns) between two hill caves.</summary>
    public const int HillCaveGap = 40;

    private sealed partial class Builder
    {
        /// <summary>
        /// The hill caves, the giant spiders' nests: in each hill range <see cref="HillCavesPerRange"/> of them, each a
        /// round chamber deep under the hill's high ground (<see cref="HillCaveChamber"/> columns across from its middle,
        /// its floor a few levels over the water, its roof at least <see cref="HillCaveRoof"/> under the hill) and a
        /// tunnel three columns wide running straight out of it away from the range's middle, climbing a level every
        /// two columns until it comes out on the hillside. So the way in goes down to the chamber, the cave's bottom.
        /// Only through plain ground: not through a ridge, a pass, the cavern under the range, a tree, a ruin, water or
        /// loose rock, and not near a start. Carved last from a random stream of its own, so nothing else on existing
        /// seeds moved. Each is <see cref="RegionMap.HillCaves"/> and <see cref="RegionMap.HillCaveMouths"/>.
        /// </summary>
        private void CarveHillCaves()
        {
            var rng = new DeterministicRandom(_seed ^ 0x5D1DE2CAUL);
            foreach (var (hx, hz, hr) in _hills)
            for (int got = 0, tries = 0; got < HillCavesPerRange && tries < 300; tries++)
            {
                int reach = Math.Max(1, hr * 2 / 3);
                int cx = hx + rng.Range(-reach, reach + 1), cz = hz + rng.Range(-reach, reach + 1);
                if (TryHillCave(cx, cz, hx, hz)) got++;
            }
        }

        /// <summary>True if a hill cave fits with its chamber's middle at (cx, cz), and carves it.</summary>
        private bool TryHillCave(int cx, int cz, int hx, int hz)
        {
            const int r = HillCaveChamber;
            int margin = r + 4;
            if (cx < margin || cz < margin || cx >= _sx - margin || cz >= _sz - margin) return false;
            if (NearStart(cx, cz, Sx(StartResourceRadius + 40))) return false;
            foreach (var (ox, _, oz) in _map.HillCaves)
                if ((long)(ox - cx) * (ox - cx) + (long)(oz - cz) * (oz - cz) < (long)HillCaveGap * HillCaveGap) return false;
            foreach (var (mx, mz) in _caveMouths)
                if ((long)(mx - cx) * (mx - cx) + (long)(mz - cz) * (mz - cz) < 24L * 24) return false;
            foreach (var (rx, _, rz) in _map.Ruins)
                if ((long)(rx - cx) * (rx - cx) + (long)(rz - cz) * (rz - cz) < 24L * 24) return false;

            // Straight out away from the range's middle.
            int dx, dz;
            if (Math.Abs(cx - hx) >= Math.Abs(cz - hz)) (dx, dz) = (cx >= hx ? 1 : -1, 0);
            else (dx, dz) = (0, cz >= hz ? 1 : -1);

            int floor = _water + Math.Max(3, Sy(4));
            var cells = new List<(int X, int Y, int Z)>();
            var floors = new List<(int X, int Y, int Z)>();

            // The chamber: a flat floor under a low dome.
            for (int oz = -r; oz <= r; oz++)
            for (int ox = -r; ox <= r; ox++)
            {
                int d2 = ox * ox + oz * oz;
                if (d2 > r * r) continue;
                int x = cx + ox, z = cz + oz;
                int top = floor + HillCaveHeadroom + 2 * (r * r - d2) / (r * r);
                if (_height[Idx(x, z)] < top + HillCaveRoof) return false;
                for (int y = floor; y <= top; y++) cells.Add((x, y, z));
                floors.Add((x, floor - 1, z));
            }

            // The tunnel, climbing to the hillside.
            int mouthX = -1, mouthY = -1, mouthZ = -1;
            for (int s = r; s <= HillCaveReach; s++)
            {
                int f = floor + (s - r + 1) / 2;
                int px = cx + dx * s, pz = cz + dz * s;
                if (px < 2 || pz < 2 || px >= _sx - 2 || pz >= _sz - 2) return false;
                bool outside = true;
                for (int w = -1; w <= 1; w++)
                {
                    int x = px + w * dz, z = pz + w * dx;
                    if (_height[Idx(x, z)] >= f) outside = false;
                }
                if (outside)
                {
                    (mouthX, mouthY, mouthZ) = (px, f, pz);
                    break;
                }
                for (int w = -1; w <= 1; w++)
                {
                    int x = px + w * dz, z = pz + w * dx;
                    for (int y = f; y < f + HillCaveHeadroom; y++) cells.Add((x, y, z));
                    floors.Add((x, f - 1, z));
                }
            }
            if (mouthX < 0) return false;

            // Only plain ground in the way.
            foreach (var (x, y, z) in cells)
            {
                int i = Idx(x, z);
                if (RidgeAt(x, z) > 0 || _map.BiomeAt(x, z) is Biome.Wilds or Biome.River or Biome.Ocean or Biome.Swamp) return false;
                var b = _w.Get(x, y, z);
                if (b == BlockType.Air ? y <= _height[i] : !PlainGround(b)) return false;
            }
            foreach (var (x, y, z) in floors)
                if (!PlainGround(_w.Get(x, y, z))) return false;
            if (_map.Passes.Count > 0 && (NearPass(cx, cz, Sx(PassHalf) + Sx(RidgeHalf) + r + 8) || NearPass(mouthX, mouthZ, Sx(PassHalf) + 8)))
                return false;

            foreach (var (x, y, z) in cells) _w.Set(x, y, z, BlockType.Air);
            foreach (var (x, y, z) in floors)
                if (_w.Get(x, y, z) is BlockType.Grass or BlockType.Sand) _w.Set(x, y, z, BlockType.Soil);
            _map.HillCaves.Add((cx, floor, cz));
            _map.HillCaveMouths.Add((mouthX, mouthY, mouthZ));
            return true;
        }

        /// <summary>Ground a hill cave may be dug through: earth, stone and the ore in it.</summary>
        private static bool PlainGround(BlockType b) => b is BlockType.Stone or BlockType.Soil or BlockType.Grass or BlockType.Sand
            or BlockType.IronOre or BlockType.Coal or BlockType.Crystal or BlockType.GoldOre;
    }
}
