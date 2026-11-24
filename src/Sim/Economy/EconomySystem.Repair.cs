using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Repair (Craft guilds, issue #150): a player with the <see cref="Content.TechEffectType.Repair"/> tech sends workers
/// to their own damaged building, wall, gate or tower (see <see cref="RepairCommand"/>), and they mend it a step at a
/// time from beside it: each knocked-out block put back where it stood (bottom up, once nothing is in its way), then
/// the damage dealt toward the next block. A step takes as long as building that much of it did, four times as long
/// while it is under fire (hit by another side in the last few seconds), and costs its share of half the building's
/// cost over all of its hit points, taken from storage. Rebuilding a ruin from scratch is a separate thing (see
/// <see cref="Automation.AutomationSystem"/>), open to everyone.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>How long (ticks) after it was last hit by another side a building counts as under fire.</summary>
    public const int UnderFireTicks = 5 * Tps;
    /// <summary>How many times longer each step of a repair takes while the building is under fire.</summary>
    public const int UnderFireSlowdown = 4;
    /// <summary>The least time (ticks) one step of a repair takes.</summary>
    public const int MinMendTicks = Tps / 4;
    /// <summary>
    /// How near (columns from its footprint) an enemy keeps automatic workers (and with them computer players' workers)
    /// from going to repair a building.
    /// </summary>
    public const int RepairSafeRadius = 16;

    /// <summary>
    /// True if the building is damaged and its owner's workers can repair it: the owner has the tech for it, and it is
    /// a building of theirs (not a city-state's totem).
    /// </summary>
    public bool Repairable(Building b) =>
        b.Damaged && b.Def.Kind != BuildingKind.Totem && b.Owner >= 0 && b.Owner < Simulation.WildPlayer && CanRepair(b.Owner);

    /// <summary>True if the building was hit by another side less than <see cref="UnderFireTicks"/> ago: repairs to it go slowly.</summary>
    public bool UnderFire(Building b) => b.LastHit >= 0 && _sim.Tick - b.LastHit < UnderFireTicks;

    /// <summary>
    /// True if automatic workers should go and repair the building: it is <see cref="Repairable"/>, something on it can be
    /// mended now and paid for, and no enemy is within <see cref="RepairSafeRadius"/> columns of it.
    /// </summary>
    public bool WantsRepair(Building b)
    {
        if (!Repairable(b) || !NextMend(b, out _, out int h)) return false;
        return CanAfford(b.Owner, MendCost(b, h)) && !EnemyNear(b, RepairSafeRadius);
    }

    /// <summary>True if a unit of a side hostile to the building's owner (wild game aside) is within <paramref name="radius"/> columns of it.</summary>
    public bool EnemyNear(Building b, int radius)
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Aboard[i] != 0 || !_sim.Truces.Hostile(b.Owner, u.Owner[i]) || Content.Units[u.Def[i]].IsGame) continue;
            if (b.ColumnDistance(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) <= radius) return true;
        }
        return false;
    }

    /// <summary>
    /// The next step of a repair to the building: block <paramref name="k"/> of its template put back (the first gone in
    /// build order, so bottom up, whose place is clear and has something under it), or with k = -1 the damage toward its
    /// next block taken away. <paramref name="h"/> is the hit points that gives back. False if nothing can be mended now.
    /// </summary>
    public bool NextMend(Building b, out int k, out int h)
    {
        k = -1;
        h = 0;
        if (!b.Damaged) return false;
        var shape = b.Shape;
        var blocks = shape.Blocks;
        if (b.Lost > 0)
            for (int j = 0; j < b.Placed && j < blocks.Count; j++)
            {
                if (!b.IsGone(j) || !ClearForMend(b, j)) continue;
                k = j;
                h = shape.BlockToughness(j);
                return true;
            }
        if (b.Damage <= 0) return false;
        h = b.Damage;
        return true;
    }

    /// <summary>True if knocked-out block k of the building can go back now: its place is clear and something is under it.</summary>
    private bool ClearForMend(Building b, int k)
    {
        var t = b.Shape.Blocks[k];
        int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
        if (!World.InBounds(x, y, z)) return false;
        var current = World.Get(x, y, z);
        // A field's block was turned to soil; anything else goes back into air, water, leaves or rubble.
        if (t.Y < 0) return current == BlockType.Soil || current == BlockType.Air;
        if (!(current is BlockType.Air or BlockType.Water || BlockInfo.IsLeaves(current) || BlockInfo.IsRubble(current))) return false;
        if (t.Y > 0 && y > 0 && !BlockInfo.IsSolid(World.Get(x, y - 1, z))) return false;
        return !Occupied(x, y, z);
    }

    /// <summary>
    /// What mending <paramref name="h"/> more hit points of the building costs: of each good of its cost, the share that
    /// brings what has been paid since it was last whole to half the cost times the hit points mended over its most.
    /// </summary>
    public GoodAmount[] MendCost(Building b, int h)
    {
        long most = 2L * Math.Max(1, b.MaxHealth);
        var cost = b.Def.Cost;
        var owed = new List<GoodAmount>(cost.Length);
        foreach (var c in cost)
        {
            long before = c.Amount * (long)b.Mended / most, after = c.Amount * (long)(b.Mended + h) / most;
            if (after > before) owed.Add(new GoodAmount(c.Good, (int)Math.Min(c.Amount, after - before)));
        }
        return owed.ToArray();
    }

    /// <summary>Ticks of work (at the usual work speed) mending <paramref name="h"/> hit points of the building takes: as long as building that much of it did.</summary>
    public int MendTicks(Building b, int h) =>
        Math.Max(MinMendTicks, (int)((long)b.Def.BuildTicks * h / Math.Max(1, b.Built)));

    /// <summary>Sends the command's workers to repair the building, if its owner can repair it.</summary>
    internal void Apply(RepairCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (b == null || b.Owner != c.PlayerId || !Repairable(b)) return;
        foreach (int i in Workers(c))
        {
            if (U.Job[i] == UnitJob.Repair && U.JobTarget[i] == b.Id) continue;
            StartJob(i, UnitJob.Repair, b.Id);
        }
    }

    private void StepRepair(int i)
    {
        var u = U;
        var b = Buildings.Get(u.JobTarget[i]);
        if (b != null && b.Owner == u.Owner[i] && (b.Def.IsWall || b.Def.IsTower) && !(Repairable(b) && NextMend(b, out _, out _))
            && NextRepair(u.Owner[i], b) is { } next)
        {
            // That stretch of wall is whole (or has nothing to mend now): on to the next damaged one along.
            StartJob(i, UnitJob.Repair, next.Id);
            b = next;
        }
        if (b == null || b.Owner != u.Owner[i] || !Repairable(b))
        {
            EndJob(i);
            return;
        }
        if (WalkToBuilding(i, b, out bool there)) return;
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (!NextMend(b, out int k, out int h))
        {
            EndJob(i); // nothing it can mend now (rubble or a unit in the way of what is gone)
            return;
        }
        u.JobTimer[i] += UnderFire(b) ? Math.Max(1, WorkOf(i) / UnderFireSlowdown) : WorkOf(i);
        if ((_sim.Tick + u.Id[i]) % Tps == 0) _sim.Evolution.Built(i);
        if (u.JobTimer[i] < MendTicks(b, h) * 100) return;
        u.JobTimer[i] = 0;
        var cost = MendCost(b, h);
        if (!TrySpend(b.Owner, cost, b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2))
        {
            EndJob(i); // nothing to pay for it with
            return;
        }
        Mend(b, k, h);
    }

    /// <summary>
    /// The player's damaged wall, gate or tower within <see cref="WallReach"/> columns of <paramref name="from"/> that has
    /// something to mend now, with the fewest workers repairing it and then the nearest; null if there is none.
    /// </summary>
    private Building? NextRepair(int player, Building from)
    {
        Building? best = null;
        int bestWorkers = 0, bestD = 0;
        foreach (var b in Buildings.All)
        {
            if (b == from || b.Owner != player || !(b.Def.IsWall || b.Def.IsTower) || !Repairable(b) || !NextMend(b, out _, out _)) continue;
            int d = Math.Max(from.ColumnDistance(b.X, b.Z), b.ColumnDistance(from.X, from.Z));
            if (d > WallReach) continue;
            int workers = WorkersAt(b, UnitJob.Repair);
            if (best != null && (workers > bestWorkers || (workers == bestWorkers && d >= bestD))) continue;
            (best, bestWorkers, bestD) = (b, workers, d);
        }
        return best;
    }

    /// <summary>Puts back block k of the building (or with k = -1 takes away its damage), <paramref name="h"/> hit points.</summary>
    private void Mend(Building b, int k, int h)
    {
        if (k >= 0)
        {
            var t = b.Shape.Blocks[k];
            World.Set(b.X + t.X, b.Y + t.Y, b.Z + t.Z, t.Block);
            b.Gone![k] = false;
            b.Lost = Math.Max(0, b.Lost - h);
        }
        else b.Damage = 0;
        b.Mended += h;
        if (b.Lost == 0 && b.Damage == 0)
        {
            b.Mended = 0;
            b.Gone = null;
        }
    }
}
