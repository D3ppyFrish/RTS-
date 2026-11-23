using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>
/// Craters: where a shot from a ranged unit with <see cref="UnitDef.Crater"/> lands (a catapult's stone, a
/// bombard ship's shell), it blows a bowl in the natural ground round it, two blocks deep in the middle and one
/// at the edge, and throws the earth out onto the rim as rubble. Only open ground under the sky is dug: no
/// building's blocks, nothing under water, a roof or a tree, and nothing underground.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Craters blown since the match began.</summary>
    public long Craters { get; private set; }

    /// <summary>How far (levels) below where a shot lands the ground of a crater's column may lie (and no more than one above).</summary>
    public const int CraterReach = 3;

    private readonly List<(int X, int Z)> _rim = new();

    /// <summary>
    /// Blows a crater reaching <paramref name="radius"/> columns round where a shot lands at (x, y, z) (y: the
    /// level of the feet of whoever stood there). Nothing happens if the ground there is not open to the sky.
    /// </summary>
    public void Crater(Fix64 x, int y, Fix64 z, int radius)
    {
        if (radius <= 0) return;
        int cx = x.FloorToInt(), cz = z.FloorToInt();
        if (!World.InBounds(cx, 0, cz)) return;
        int ground = World.SurfaceHeight(cx, cz);
        // Landing on a building (the foot of a wall): the ground round it is what is dug.
        if (ground >= 0 && Buildings.At(cx, ground, cz) != null) ground = y - 1;
        // Underground, or the shot struck a wall or roof above: no crater in the open ground.
        if (ground < y - 1 - CraterReach || ground > y) return;
        int r2 = radius * radius + radius, removed = 0;
        _rim.Clear();
        for (int dz = -radius - 1; dz <= radius + 1; dz++)
        for (int dx = -radius - 1; dx <= radius + 1; dx++)
        {
            int px = cx + dx, pz = cz + dz, d2 = dx * dx + dz * dz;
            if (!World.InBounds(px, 0, pz)) continue;
            if (d2 > r2)
            {
                if (d2 <= (radius + 1) * (radius + 1) + radius + 1) _rim.Add((px, pz));
                continue;
            }
            int depth = d2 * 4 <= radius * radius ? 2 : 1;
            for (int k = 0; k < depth; k++)
            {
                int top = Diggable(px, pz, ground);
                if (top < 0) break;
                World.Set(px, top, pz, BlockType.Air);
                removed++;
            }
        }
        if (removed == 0) return;
        Craters++;
        // The earth comes down on the rim, a block to a column while it lasts.
        foreach (var (px, pz) in _rim)
        {
            if (removed == 0) break;
            int top = World.SurfaceHeight(px, pz);
            if (top < 0 || top + 1 >= World.SizeY || Math.Abs(top - ground) > CraterReach) continue;
            var b = World.Get(px, top, pz);
            if (!BlockInfo.IsGround(b) && !BlockInfo.IsRubble(b) || World.Get(px, top + 1, pz) != BlockType.Air) continue;
            if (Buildings.AtColumn(px, pz) != null) continue;
            World.Set(px, top + 1, pz, BlockType.Rubble);
            removed--;
        }
    }

    /// <summary>
    /// A cratering shot at building b lands at its foot on the side unit i shoots from: the crater is blown in the
    /// ground there (the building's own blocks are spared, see <see cref="Diggable"/>).
    /// </summary>
    private void CraterBeside(int i, UnitDef def, Economy.Building b)
    {
        var u = U;
        // Stones do not all land on the same spot: each falls up to two columns either way of the aim, by the tick
        // and the shooter (the same in every run).
        int spread = (int)(DeterministicRandom.Hash((int)(_sim.Tick & 0x7FFFFFFF), u.Id[i] * 65599 ^ b.Id, _sim.Seed ^ 0xC7A7UL) % 25);
        Fix64 x = Fix64.Clamp(u.X[i] + Fix64.FromInt(spread % 5 - 2), Fix64.FromInt(b.X - 1), Fix64.FromInt(b.MaxX + 1) + Fix64.Half);
        Fix64 z = Fix64.Clamp(u.Z[i] + Fix64.FromInt(spread / 5 - 2), Fix64.FromInt(b.Z - 1), Fix64.FromInt(b.MaxZ + 1) + Fix64.Half);
        // The ground there may lie above or below the building's floor (it stands on a levelled site, or on a plinth).
        int top = World.SurfaceHeight(x.FloorToInt(), z.FloorToInt());
        int y = Buildings.AtColumn(x.FloorToInt(), z.FloorToInt()) == null && top >= 0 ? top + 1 : b.Y;
        Crater(x, y, z, def.Crater);
    }

    /// <summary>
    /// The level of column (x, z)'s top block if a crater can blow it out: natural ground open to the sky, not under a
    /// building's footprint (it would undermine it), at most a level above <paramref name="ground"/> and <see cref="CraterReach"/> below it, or -1.
    /// </summary>
    private int Diggable(int x, int z, int ground)
    {
        int top = World.SurfaceHeight(x, z);
        if (top < 1 || top > ground + 1 || top < ground - CraterReach) return -1;
        var b = World.Get(x, top, z);
        if (!BlockInfo.IsGround(b) && !BlockInfo.IsRubble(b)) return -1;
        if (top + 1 < World.SizeY && World.Get(x, top + 1, z) != BlockType.Air) return -1;
        if (Buildings.AtColumn(x, z) != null || World.Get(x, top - 1, z) == BlockType.Bedrock) return -1;
        return top;
    }
}
