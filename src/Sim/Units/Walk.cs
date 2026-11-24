namespace Rts.Sim.Units;

/// <summary>
/// How units stand on and step across voxel terrain. Units are two blocks tall (1 m), can step up
/// one block, can drop up to three, and can wade through water one block deep. A gate's leaves
/// (<see cref="BlockType.Gate"/>) are open here: whose units may go through a gate is up to the gate's owner,
/// which path finding and movement check (see <see cref="Economy.BuildingStore.GateOwner"/>). A siege ladder's rungs
/// (<see cref="BlockType.Ladder"/>) are open too, and a unit at a ladder's foot climbs it in one step to the top of the
/// wall or cliff it leans on, as one up there climbs down it (see <see cref="Economy.Ladder"/>).
/// </summary>
public static class Walk
{
    public const int Clearance = 2;
    public const int MaxClimb = 1;
    public const int MaxDrop = 3;

    /// <summary>A block a unit's body can occupy.</summary>
    public static bool Passable(BlockType b) => b is BlockType.Air or BlockType.Water or BlockType.Gate or BlockType.Ladder or BlockType.Orchard;

    /// <summary>A block a unit's head can be in: air, a gate's leaf, a ladder's rung, or an orchard's branches.</summary>
    public static bool Headroom(BlockType b) => b is BlockType.Air or BlockType.Gate or BlockType.Ladder or BlockType.Orchard;

    /// <summary>Most rungs a ladder has: the most levels it climbs from its foot to the top of what it leans on.</summary>
    public const int MaxLadder = 10;

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
        // Up a ladder: from its foot, straight on to the top of what it leans on.
        if (ClimbsLadder(w, fromX, fromZ, fromY, toX, toZ, out toY)) return true;
        // Drop: the unit walks off at its own height, so the target column must be open from there down.
        if (!Passable(w.Get(toX, fromY, toZ)) || !Headroom(w.Get(toX, fromY + 1, toZ))) return false;
        for (int d = 1; d <= MaxDrop; d++)
        {
            int y = fromY - d;
            if (CanStand(w, toX, y, toZ)) { toY = y; return true; }
            if (!Passable(w.Get(toX, y, toZ))) break;
        }
        // Down a ladder whose top rung is just under its feet, to the ladder's foot.
        return DescendsLadder(w, fromX, fromZ, fromY, toX, toZ, out toY);
    }

    /// <summary>
    /// True if a unit standing at level <paramref name="fromY"/> in (fromX, fromZ), with its feet in a ladder's
    /// bottom rung, climbs it straight on into (toX, toZ): the rungs go up unbroken from its feet, it has headroom
    /// over the top one, and it can stand on the column beyond at the level over the top rung. Never on a diagonal.
    /// </summary>
    public static bool ClimbsLadder(VoxelWorld w, int fromX, int fromZ, int fromY, int toX, int toZ, out int toY)
    {
        toY = fromY;
        if ((fromX != toX && fromZ != toZ) || w.Get(fromX, fromY, fromZ) != BlockType.Ladder) return false;
        int top = fromY;
        while (top - fromY < MaxLadder && w.Get(fromX, top + 1, fromZ) == BlockType.Ladder) top++;
        int y = top + 1;
        if (y - fromY <= MaxClimb || !Headroom(w.Get(fromX, y, fromZ)) || !Headroom(w.Get(fromX, y + 1, fromZ))
            || !CanStand(w, toX, y, toZ)) return false;
        toY = y;
        return true;
    }

    /// <summary>
    /// True if a unit standing at level <paramref name="fromY"/> in (fromX, fromZ) climbs down a ladder in (toX, toZ)
    /// whose top rung is the block under its feet there: straight on, never on a diagonal, to where the ladder stands.
    /// </summary>
    public static bool DescendsLadder(VoxelWorld w, int fromX, int fromZ, int fromY, int toX, int toZ, out int toY)
    {
        toY = fromY;
        if ((fromX != toX && fromZ != toZ) || w.Get(toX, fromY - 1, toZ) != BlockType.Ladder
            || !Headroom(w.Get(toX, fromY, toZ)) || !Headroom(w.Get(toX, fromY + 1, toZ))) return false;
        for (int y = fromY - 1; y >= 1 && fromY - y <= MaxLadder; y--)
        {
            if (w.Get(toX, y, toZ) != BlockType.Ladder) return false;
            if (CanStand(w, toX, y, toZ))
            {
                toY = y;
                return true;
            }
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
    /// Highest standable level in the column that is not on top of tree leaves or the dusk tree, or -1.
    /// Spawning uses this so units appear on the ground under a forest rather than on the canopy.
    /// </summary>
    public static int GroundLevel(VoxelWorld w, int x, int z)
    {
        for (int y = w.SizeY - 2; y >= 1; y--)
            if (CanStand(w, x, y, z) && w.Get(x, y - 1, z) is var under && !BlockInfo.IsLeaves(under) && !BlockInfo.IsDuskTree(under)) return y;
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
