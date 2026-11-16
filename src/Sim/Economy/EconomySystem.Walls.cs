using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Walls and gates (<see cref="WallCommand"/>): a line of one-column sites, built by workers a column at a
/// time. Each column is its own building made of ordinary building blocks, so blows, sappers and tunnels
/// knock pieces out of a wall and what loses its support falls (see <see cref="CombatSystem"/>). Walls and
/// gates stand right next to each other, with no gap; a line steps diagonally where it must, which still
/// shuts the way, since nobody cuts a corner between two blocked columns.
///
/// Walls come in tiers (<see cref="BuildingDef.Upgrade"/>): the one that is laid, and those it is upgraded to
/// where it stands (<see cref="UpgradeCommand"/>). An upgrade is paid for when it is ordered and is builders'
/// work like a site; the wall stands as it was until the work is done, and then it is the new building, whole.
///
/// Ranged units are posted on top of a wall or gate that has a walkway (<see cref="PostCommand"/>, one to a
/// column): they walk to its foot and climb up. A unit standing on a wall holds its ground, and comes down
/// when an order takes it somewhere else (see <see cref="UnitMovement"/>).
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Most columns one wall command lays.</summary>
    public const int MaxWall = 64;
    /// <summary>How far (columns) a worker who finished a stretch of wall looks for the next one to build.</summary>
    public const int WallReach = 8;

    /// <summary>
    /// The columns of a straight line from (x1, z1) to (x2, z2), at most <see cref="MaxWall"/> of them: the
    /// line a wall is laid along (the game previews the same one).
    /// </summary>
    public static List<(int X, int Z)> WallLine(int x1, int z1, int x2, int z2)
    {
        var list = new List<(int X, int Z)>();
        int x = x1, z = z1;
        int dx = Math.Abs(x2 - x1), dz = -Math.Abs(z2 - z1);
        int sx = x1 < x2 ? 1 : -1, sz = z1 < z2 ? 1 : -1;
        int err = dx + dz;
        while (list.Count < MaxWall)
        {
            list.Add((x, z));
            if (x == x2 && z == z2) break;
            int e2 = 2 * err;
            if (e2 >= dz) { err += dz; x += sx; }
            if (e2 <= dx) { err += dx; z += sz; }
        }
        return list;
    }

    internal void Apply(WallCommand c)
    {
        if (!_sim.Content.TryGetBuilding(c.Building, out var def) || !def.IsWall) return;
        if (def.SizeX != 1 || def.SizeZ != 1) return; // a line is laid a column at a time
        if (def.UpgradeOf != null) return; // a higher tier is not laid: the wall it comes from is, and then upgraded
        var sites = new List<Building>();
        foreach (var (x, z) in WallLine(c.X1, c.Z1, c.X2, c.Z2))
        {
            if (Buildings.AtColumn(x, z) != null) continue; // a gate (or wall) already there stays
            var result = CanPlace(c.PlayerId, def, x, z, out int baseY);
            if (result == PlaceResult.CannotAfford) break;
            if (result != PlaceResult.Ok || !TrySpend(c.PlayerId, def.Cost, x, z)) continue;
            sites.Add(Prepare(c.PlayerId, def, x, z, baseY));
        }
        if (sites.Count == 0) return;
        // Spread the workers along the line: each starts on its share of it, and moves on from there.
        var workers = Workers(c);
        for (int k = 0; k < workers.Count; k++)
            StartJob(workers[k], UnitJob.Build, sites[k * sites.Count / workers.Count].Id);
    }

    /// <summary>
    /// The player's wall or gate site (or one being upgraded) nearest to column (x, z), within
    /// <see cref="WallReach"/> of it, with the fewest workers on it (ties to the nearest, then the lowest id);
    /// null if there is none.
    /// </summary>
    private Building? NextWallSite(int player, int x, int z)
    {
        Building? best = null;
        int bestWorkers = 0, bestD = 0;
        foreach (var b in Buildings.All)
        {
            if (b.Owner != player || !b.WantsBuilders || !b.Def.IsWall) continue;
            int d = b.ColumnDistance(x, z);
            if (d > WallReach) continue;
            int workers = WorkersAt(b, UnitJob.Build);
            if (best != null && (workers > bestWorkers || (workers == bestWorkers && d >= bestD))) continue;
            (best, bestWorkers, bestD) = (b, workers, d);
        }
        return best;
    }

    // --- Upgrades ----------------------------------------------------------------------------------

    /// <summary>Most walls and gates one upgrade command starts on.</summary>
    public const int MaxUpgrade = 256;

    /// <summary>
    /// Why the building cannot be upgraded now: <see cref="PlaceResult.Ok"/> if it can. Blocked: it has no
    /// upgrade, is not finished, is being upgraded already, or something is in the way above it. Locked: its
    /// owner lacks a tech the upgrade needs.
    /// </summary>
    public PlaceResult CanUpgrade(Building b)
    {
        var to = b.Def.Upgrade;
        if (to == null || !b.Active || b.Upgrade != null) return PlaceResult.Blocked;
        if (!Unlocked(b.Owner, to)) return PlaceResult.Locked;
        if (!RoomFor(b, to)) return PlaceResult.Blocked;
        return CanAfford(b.Owner, to.Cost) ? PlaceResult.Ok : PlaceResult.CannotAfford;
    }

    /// <summary>True if the blocks of <paramref name="to"/> above what the building is now have room: air or leaves (units step up).</summary>
    private bool RoomFor(Building b, BuildingDef to)
    {
        int top = b.Y + Math.Max(b.Def.Height, 1);
        if (b.Y + to.Height + Walk.Clearance >= World.SizeY) return false;
        foreach (var t in to.Blocks)
        {
            int y = b.Y + t.Y;
            if (y < top) continue;
            var there = World.Get(b.X + t.X, y, b.Z + t.Z);
            if (there != BlockType.Air && !BlockInfo.IsLeaves(there)) return false;
        }
        return true;
    }

    internal void Apply(UpgradeCommand c)
    {
        var first = Buildings.Get(c.Building);
        if (first == null || first.Owner != c.PlayerId || !first.Def.IsWall || first.Planned) return;
        var started = new List<Building>();
        foreach (var b in c.Stretch ? Stretch(first) : new List<Building> { first })
        {
            var result = CanUpgrade(b);
            if (result == PlaceResult.CannotAfford) break;
            if (result != PlaceResult.Ok || !TrySpend(c.PlayerId, b.Def.Upgrade!.Cost, b.X, b.Z)) continue;
            b.Upgrade = b.Def.Upgrade;
            b.UpgradeWork = 0;
            started.Add(b);
        }
        if (started.Count == 0) return;
        var workers = Workers(c);
        for (int k = 0; k < workers.Count; k++)
            StartJob(workers[k], UnitJob.Build, started[k * started.Count / workers.Count].Id);
    }

    /// <summary>
    /// The owner's walls and gates joined to the building (side by side or corner to corner, by way of walls
    /// and gates of any tier) that are of its tier, itself first and then nearest first along the wall, at most
    /// <see cref="MaxUpgrade"/> of them.
    /// </summary>
    public List<Building> Stretch(Building first)
    {
        var result = new List<Building>();
        if (!first.Def.IsWall) return result;
        // Looked up by column only, never walked through, so the order of the dictionary does not matter.
        var at = new Dictionary<long, Building>();
        foreach (var b in Buildings.All)
            if (b.Owner == first.Owner && b.Def.IsWall && !b.Planned) at[Column(b.X, b.Z)] = b;
        var seen = new HashSet<int> { first.Id };
        var queue = new Queue<Building>();
        queue.Enqueue(first);
        while (queue.Count > 0 && result.Count < MaxUpgrade)
        {
            var b = queue.Dequeue();
            if (b.Def.Tier == first.Def.Tier && b.Def.Kind is BuildingKind.Wall or BuildingKind.Gate) result.Add(b);
            foreach (var (dx, dz) in WalkGraph.Dirs)
                if (at.TryGetValue(Column(b.X + dx, b.Z + dz), out var next) && seen.Add(next.Id)) queue.Enqueue(next);
        }
        return result;
    }

    private static long Column(int x, int z) => ((long)z << 32) | (uint)x;

    /// <summary>A building whose upgrade has had all its work becomes the new building (see <see cref="FinishUpgrade"/>).</summary>
    private void StepUpgrade(Building b)
    {
        if (b.UpgradeWork >= b.Upgrade!.BuildTicks) FinishUpgrade(b);
    }

    /// <summary>
    /// The upgrade is done: the building's blocks are the new building's, all of them (so it is whole again,
    /// whatever had been knocked out of it), and it is that building from now on. It waits while something
    /// solid is in the way above it. A unit standing where a block goes steps up onto it (see <see cref="UnitMovement"/>).
    /// </summary>
    private void FinishUpgrade(Building b)
    {
        var to = b.Upgrade!;
        if (!RoomFor(b, to)) return;
        foreach (var t in to.Blocks) World.Set(b.X + t.X, b.Y + t.Y, b.Z + t.Z, t.Block);
        b.Def = to;
        b.Placed = to.Blocks.Count;
        b.Work = to.BuildTicks;
        b.Lost = b.Damage = 0;
        b.Gone = null;
        b.Upgrade = null;
        b.UpgradeWork = 0;
        _sim.Events.BuildingFinished(b);
    }

    // --- Posts on the walls ------------------------------------------------------------------------

    /// <summary>How far (columns) from the wall named in a post order its units look for a free column of walkway.</summary>
    public const int PostReach = 12;
    /// <summary>Ticks a unit takes to climb each level of a wall.</summary>
    public const int ClimbTicks = Tps / 2;

    /// <summary>
    /// The wall or gate unit i stands on top of, or null: the building whose block is under its feet. A unit on
    /// a wall holds its ground (see <see cref="CombatSystem.Holding"/>).
    /// </summary>
    public Building? WallUnder(int i)
    {
        int x = U.X[i].FloorToInt(), y = U.Y[i] - 1, z = U.Z[i].FloorToInt();
        // The block first: almost every unit stands on plain ground, and that is settled without a look at the buildings.
        if (World.Get(x, y, z) is not (BlockType.Planks or BlockType.StoneBrick or BlockType.Palisade or BlockType.Gate)) return null;
        var b = Buildings.At(x, y, z);
        return b != null && b.Def.IsWall ? b : null;
    }

    /// <summary>True if a unit can be posted on the wall now: finished, with a walkway, its top whole and nobody on it.</summary>
    public bool PostFree(Building b)
    {
        if (!b.Def.Walkway || !b.Active) return false;
        int top = b.Y + b.Def.Height;
        return World.Get(b.X, top - 1, b.Z) == b.Def.Blocks[^1].Block && Walk.CanStand(World, b.X, top, b.Z) && !Occupied(b.X, top, b.Z);
    }

    internal void Apply(PostCommand c)
    {
        var wall = Buildings.Get(c.Building);
        if (wall == null || wall.Owner != c.PlayerId || !wall.Def.Walkway || wall.Planned) return;
        var units = OwnedUnits(c);
        units.RemoveAll(i => !CanPost(i));
        if (units.Count == 0) return;
        // The columns of walkway around the one named, nearest to it first.
        var places = new List<Building>();
        foreach (var b in Buildings.All)
            if (b.Owner == c.PlayerId && b.Def.Walkway && b.Active && b.ColumnDistance(wall.X, wall.Z) <= PostReach) places.Add(b);
        places.Sort((a, b) => a.CentreDistance2(wall.X, wall.Z) != b.CentreDistance2(wall.X, wall.Z)
            ? a.CentreDistance2(wall.X, wall.Z).CompareTo(b.CentreDistance2(wall.X, wall.Z)) : a.Id.CompareTo(b.Id));
        // Columns somebody else is on their way to, and those the command's own units already stand on (they stay).
        var taken = new HashSet<int>();
        for (int j = 0; j < U.Count; j++)
            if (U.Job[j] == UnitJob.Post && !units.Contains(j)) taken.Add(U.JobTarget[j]);
        var waiting = new List<int>();
        foreach (int i in units)
        {
            if (U.Job[i] == UnitJob.None && WallUnder(i) is { } on && places.Contains(on)) taken.Add(on.Id);
            else waiting.Add(i);
        }
        int next = 0;
        foreach (int i in waiting)
        {
            while (next < places.Count && (taken.Contains(places[next].Id) || !PostFree(places[next]))) next++;
            if (next == places.Count) break;
            StartJob(i, UnitJob.Post, places[next++].Id);
        }
    }

    /// <summary>True if unit i can be posted on a wall: a ranged unit that fights (see <see cref="UnitDef.IsRanged"/>).</summary>
    public bool CanPost(int i)
    {
        var def = Content.Units[U.Def[i]];
        return def.CanAttack && def.IsRanged;
    }

    private void StepPost(int i)
    {
        var u = U;
        var b = Buildings.Get(u.JobTarget[i]);
        if (b == null || b.Owner != u.Owner[i] || !PostFree(b))
        {
            EndJob(i); // the wall is gone or broken, or somebody else stands there now
            return;
        }
        bool there = AtBuilding(i, b);
        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    break;
                case Travel.NeedSpot:
                    // The foot of the wall on the unit's side of it.
                    if (SpotBeside(b, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), out int x, out int z, out int y) && b.ColumnDistance(x, z) == 1)
                        _sim.SendUnit(i, x, z, y);
                    else EndJob(i);
                    break;
                case Travel.Failed:
                    EndJob(i);
                    break;
            }
            return;
        }
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ClimbTicks * b.Def.Height) return;
        // Up: it stands in the middle of the column, on top.
        u.X[i] = Fix64.FromInt(b.X) + Fix64.Half;
        u.Z[i] = Fix64.FromInt(b.Z) + Fix64.Half;
        u.Y[i] = b.Y + b.Def.Height;
        EndJob(i);
    }
}
