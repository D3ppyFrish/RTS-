using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Ramps (<see cref="RampCommand"/>): workers and sappers heap a slope of rammed earth from a foot up to a cliff,
/// a hill or the top of a wall, a level a column, so units walk up where they could not climb (see
/// <see cref="Ramp"/>). Each block takes <see cref="StonePerRamp"/> stone from the storage nearest it as it goes
/// in, and <see cref="RampBlockTicks"/> of work. A finished ramp is plain ground: anyone walks it, and anyone digs
/// it away.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Most columns a ramp runs between its foot and what it leans on.</summary>
    public const int MaxRamp = 16;
    /// <summary>Most levels a ramp climbs from its foot to its top.</summary>
    public const int MaxRampRise = 10;
    /// <summary>Most blocks of earth one column of a ramp takes (a ramp over a deep trench or water).</summary>
    public const int MaxRampFill = 12;
    /// <summary>Stone each block of a ramp costs, taken as it goes in.</summary>
    public const int StonePerRamp = 1;
    /// <summary>Ticks of work (at a worker's plain work speed) for each block of a ramp.</summary>
    public const int RampBlockTicks = 2 * Tps;
    /// <summary>How many levels below its feet a builder still heaps earth in beside it (tipping it down).</summary>
    public const int RampReachDown = MaxRampFill;

    private readonly List<Ramp> _ramps = new();
    private readonly List<RampSpan> _rampPlan = new();
    private int _nextRamp = 1;

    /// <summary>The ramps being built, by id.</summary>
    public IReadOnlyList<Ramp> Ramps => _ramps;

    public Ramp? RampById(int id)
    {
        foreach (var r in _ramps)
            if (r.Id == id) return r;
        return null;
    }

    /// <summary>
    /// The line a ramp from (x1, z1) toward (x2, z2) runs along: straight along whichever of x and z it goes
    /// further in, so each column steps straight up from the one before.
    /// </summary>
    public static (int X2, int Z2) RampEnd(int x1, int z1, int x2, int z2) =>
        Math.Abs(x2 - x1) >= Math.Abs(z2 - z1) ? (x2, z1) : (x1, z2);

    /// <summary>
    /// Plans a ramp from its foot, column (x1, z1), to the column it leans on at the far end of the line (see
    /// <see cref="RampEnd"/>), into <paramref name="spans"/>: one span for each column in between, from the foot
    /// on, at most <see cref="MaxRamp"/>. Each rises a level above the one before, up to a level under the top
    /// (the standing level on the far column), and then runs level to it; ground already that high is left. The
    /// top must be 2 to <see cref="MaxRampRise"/> levels above the foot. Returns the top level if the ramp gets
    /// there (every column has room and nothing on the way rises more than a climb), else -1; the spans are
    /// filled either way, for the game's preview.
    /// </summary>
    public int PlanRamp(int x1, int z1, int x2, int z2, List<RampSpan> spans)
    {
        spans.Clear();
        var w = World;
        (x2, z2) = RampEnd(x1, z1, x2, z2);
        if (!w.InBounds(x1, 1, z1) || !w.InBounds(x2, 1, z2)) return -1;
        int foot = Walk.GroundLevel(w, x1, z1), top = Walk.GroundLevel(w, x2, z2);
        if (foot < 0 || top < 0) return -1;
        var line = WallLine(x1, z1, x2, z2);
        bool reaches = top - foot >= 2 && top - foot <= MaxRampRise && line.Count >= 3 && line.Count <= MaxRamp + 2
                       && top + Walk.Clearance <= w.SizeY - 1;
        int s = foot;
        for (int k = 1; k < line.Count - 1 && spans.Count < MaxRamp; k++)
        {
            var (x, z) = line[k];
            int g = Walk.GroundLevel(w, x, z);
            if (g < 0)
            {
                reaches = false;
                break;
            }
            int t = Math.Max(g, Math.Min(s + 1, top - 1));
            bool room = t - g <= MaxRampFill && t - s <= Walk.MaxClimb && RampRoom(x, z, g, t);
            spans.Add(new RampSpan(x, z, g, t, room));
            reaches &= room;
            s = t;
        }
        reaches &= s >= top - Walk.MaxClimb && spans.Exists(c => c.Blocks > 0);
        return reaches ? top : -1;
    }

    /// <summary>True if column (x, z) can be filled from level g to t with headroom over it.</summary>
    private bool RampRoom(int x, int z, int g, int t)
    {
        var w = World;
        if (x < 1 || z < 1 || x > w.SizeX - 2 || z > w.SizeZ - 2) return false;
        // Not over a building or a city-state's town, even where nothing is filled (it would climb its roof).
        if (Buildings.AtColumn(x, z) != null || _sim.Cities.Crowds(x, z, x, z)) return false;
        if (g == t) return true; // nothing to fill: the ground is high enough as it is
        for (int y = g; y < t; y++)
            if (!Ramp.Open(w, x, y, z)) return false;
        for (int y = t; y < t + Walk.Clearance; y++)
            if (w.Get(x, y, z) is var b && b != BlockType.Air && !BlockInfo.IsLeaves(b)) return false;
        return true;
    }

    internal void Apply(RampCommand c)
    {
        var units = OwnedUnits(c);
        units.RemoveAll(i => !CanDig(Content.Units[U.Def[i]]));
        if (units.Count == 0) return;
        if (PlanRamp(c.X1, c.Z1, c.X2, c.Z2, _rampPlan) < 0) return;
        var columns = new List<RampSpan>();
        foreach (var span in _rampPlan)
        {
            if (span.Blocks == 0) continue;
            // Leaves in the way of the earth, or over it, are pushed aside.
            for (int y = span.Ground; y < span.Top + Walk.Clearance; y++)
                if (BlockInfo.IsLeaves(World.Get(span.X, y, span.Z))) World.Set(span.X, y, span.Z, BlockType.Air);
            columns.Add(span);
        }
        var (x2, z2) = RampEnd(c.X1, c.Z1, c.X2, c.Z2);
        var ramp = new Ramp(_nextRamp++, c.PlayerId, columns, Walk.GroundLevel(World, x2, z2));
        _ramps.Add(ramp);
        foreach (int i in units)
        {
            StartJob(i, UnitJob.Ramp, ramp.Id);
            SetPhase(i, JobPhase.ToWork);
        }
    }

    /// <summary>
    /// True if a builder standing at level uy in column (ux, uz) can put earth into block (x, y, z): it stands in
    /// a column next to it (diagonals too, never in it), and the block is no higher than its waist, or below it
    /// down to <see cref="RampReachDown"/> levels (earth is tipped down into a trench).
    /// </summary>
    public static bool RampReach(int ux, int uy, int uz, int x, int y, int z) =>
        Math.Abs(ux - x) <= 1 && Math.Abs(uz - z) <= 1 && (ux != x || uz != z)
        && y <= uy + Walk.Clearance - 1 && y >= uy - RampReachDown;

    /// <summary>The nearest spot beside block (x, y, z), to a builder at (fx, fy, fz), it can heap earth in from; false if none.</summary>
    private bool RampSpot(int x, int y, int z, int fx, int fy, int fz, out int sx, out int sy, out int sz)
    {
        var w = World;
        sx = sy = sz = -1;
        long best = long.MaxValue;
        foreach (var (dx, dz) in Around)
        {
            int cx = x + dx, cz = z + dz;
            for (int cy = y - Walk.Clearance + 1; cy <= y + RampReachDown; cy++)
            {
                if (!RampReach(cx, cy, cz, x, y, z) || !Walk.CanStand(w, cx, cy, cz)) continue;
                long ex = cx - fx, ez = cz - fz, ey = cy - fy;
                // Standing level with the block's top is best: on the finished ramp, not down in the trench.
                long score = ex * ex + ez * ez + 2 * ey * ey + 4L * Math.Abs(cy - (y + 1));
                if (score >= best) continue;
                best = score;
                (sx, sy, sz) = (cx, cy, cz);
            }
        }
        return best != long.MaxValue;
    }

    private void StepRamp(int i)
    {
        var u = U;
        var ramp = RampById(u.JobTarget[i]);
        if (ramp == null)
        {
            EndJob(i);
            return;
        }
        if (u.Phase[i] == JobPhase.Wait)
        {
            if (--u.JobTimer[i] > 0) return;
            SetPhase(i, JobPhase.ToWork);
        }
        if (!ramp.Next(World, out int x, out int y, out int z))
        {
            _ramps.Remove(ramp); // done
            EndJob(i);
            return;
        }
        Heap(i, x, y, z, BlockType.Soil);
    }

    /// <summary>
    /// Unit i's step at heaping block (x, y, z) of a ramp or a dam: it walks to a spot beside it (see
    /// <see cref="RampReach"/>), works <see cref="RampBlockTicks"/> there and puts <paramref name="block"/> in,
    /// paying <see cref="StonePerRamp"/> from the storage nearest it. With no stone left it stops.
    /// </summary>
    private void Heap(int i, int x, int y, int z, BlockType block)
    {
        var u = U;
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        bool there = RampReach(ux, u.Y[i], uz, x, y, z);

        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    u.JobMisses[i] = 0;
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    if (RampSpot(x, y, z, ux, u.Y[i], uz, out int sx, out int sy, out int sz))
                    {
                        _sim.SendUnit(i, sx, sz, sy);
                        return;
                    }
                    break; // nowhere to stand: as if the walk failed
                case Travel.Walking:
                    return;
            }
            // Cannot get to it: wait and try again (someone may be in the way), giving up after MaxMisses tries.
            if (++u.JobMisses[i] < MaxMisses) Wait(i);
            else EndJob(i);
            return;
        }

        // Working.
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ByWork(i, RampBlockTicks)) return;
        u.JobTimer[i] = 0;
        if (Take(u.Owner[i], Good.Stone, StonePerRamp, (x, z)) < StonePerRamp)
        {
            EndJob(i); // no stone left: the work waits for an order with stone to spare
            return;
        }
        Player(u.Owner[i]).Spent[(int)Good.Stone] += StonePerRamp;
        World.Set(x, y, z, block);
        // Anyone standing where the earth went in is lifted onto it.
        for (int j = 0; j < u.Count; j++)
            if (u.Y[j] == y && u.X[j].FloorToInt() == x && u.Z[j].FloorToInt() == z && Walk.CanStand(World, x, y + 1, z))
                u.Y[j] = y + 1;
    }

    /// <summary>Drops the ramps nobody builds any more (every unit on one was given other orders, or died). Once a second.</summary>
    private void PruneRamps()
    {
        if (_ramps.Count == 0 || _sim.Tick % Tps != 0) return;
        _ramps.RemoveAll(r =>
        {
            for (int i = 0; i < U.Count; i++)
                if (U.Job[i] == UnitJob.Ramp && U.JobTarget[i] == r.Id) return false;
            return true;
        });
    }

    private void MixRamps(ref ulong h)
    {
        foreach (var r in _ramps) r.Mix(ref h);
    }
}
