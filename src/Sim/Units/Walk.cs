namespace Rts.Sim.Units;

/// <summary>
/// How units stand on and step across voxel terrain. Units are two blocks tall (1 m), can step up
/// one block, can drop up to three, and can wade through water one block deep. A gate's leaves
/// (<see cref="BlockType.Gate"/>) are open here: whose units may go through a gate is up to the gate's owner,
/// which path finding and movement check (see <see cref="Economy.BuildingStore.GateOwner"/>).
/// </summary>
public static class Walk
{
    public const int Clearance = 2;
    public const int MaxClimb = 1;
    public const int MaxDrop = 3;

    /// <summary>A block a unit's body can occupy.</summary>
    public static bool Passable(BlockType b) => b == BlockType.Air || b == BlockType.Water || b == BlockType.Gate;

    /// <summary>A block a unit's head can be in: air, or a gate's leaf.</summary>
    public static bool Headroom(BlockType b) => b == BlockType.Air || b == BlockType.Gate;

    /// <summary>
    /// True if a unit can stand with its feet at level y in column (x, z): solid ground below, its feet
    /// in air or shallow water, and air at head height.
    /// </summary>
    public static bool CanStand(VoxelWorld w, int x, int y, int z)
    {
        if (y < 1 || !w.InBounds(x, y, z)) return false;
        return BlockInfo.CanStandOn(w.Get(x, y - 1, z))
            && Passable(w.Get(x, y, z))
            && Headroom(w.Get(x, y + 1, z));
    }

    /// <summary>
    /// Finds where a unit standing at level <paramref name="fromY"/> in (fromX, fromZ) ends up if it steps into
    /// the neighbouring column (toX, toZ). Prefers level ground, then a one-block climb, then the shortest drop.
    /// </summary>
    public static bool TryStep(VoxelWorld w, int fromX, int fromZ, int fromY, int toX, int toZ, out int toY)
    {
        toY = fromY;
        if (CanStand(w, toX, fromY, toZ)) return true;
        // Climb: needs headroom above the unit before it rises.
        if (Headroom(w.Get(fromX, fromY + Clearance, fromZ)) && CanStand(w, toX, fromY + 1, toZ))
        {
            toY = fromY + 1;
            return true;
        }
        // Drop: the unit walks off at its own height, so the target column must be open from there down.
        if (!Passable(w.Get(toX, fromY, toZ)) || !Headroom(w.Get(toX, fromY + 1, toZ))) return false;
        for (int d = 1; d <= MaxDrop; d++)
        {
            int y = fromY - d;
            if (CanStand(w, toX, y, toZ)) { toY = y; return true; }
            if (!Passable(w.Get(toX, y, toZ))) return false;
        }
        return false;
    }

    /// <summary>Highest standable level in a column at or below <paramref name="maxY"/>, or -1.</summary>
    public static int StandLevelAtOrBelow(VoxelWorld w, int x, int z, int maxY)
    {
        for (int y = System.Math.Min(maxY, w.SizeY - 2); y >= 1; y--)
            if (CanStand(w, x, y, z)) return y;
        return -1;
    }

    /// <summary>
    /// Highest standable level in the column that is not on top of tree leaves, or -1. Spawning uses this
    /// so units appear on the ground under a forest rather than on the canopy.
    /// </summary>
    public static int GroundLevel(VoxelWorld w, int x, int z)
    {
        for (int y = w.SizeY - 2; y >= 1; y--)
            if (CanStand(w, x, y, z) && !BlockInfo.IsLeaves(w.Get(x, y - 1, z))) return y;
        return -1;
    }

    /// <summary>Standable level nearest to <paramref name="y"/>, searching up first then down, or -1.</summary>
    public static int NearestStandLevel(VoxelWorld w, int x, int z, int y, int range)
    {
        if (CanStand(w, x, y, z)) return y;
        for (int d = 1; d <= range; d++)
        {
            if (CanStand(w, x, y + d, z)) return y + d;
            if (CanStand(w, x, y - d, z)) return y - d;
        }
        return -1;
    }
}
