using System;
using System.Collections.Generic;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Siege ladders (<see cref="LadderCommand"/>, <see cref="Ladder"/>): workers and sappers raise a ladder of rungs
/// against a wall, a tower or a cliff 2 to <see cref="Walk.MaxLadder"/> levels high, a rung at a time from the
/// bottom, each rung <see cref="WoodPerRung"/> wood and <see cref="LadderRungTicks"/> of work. Once it is up, units
/// at its foot climb it straight onto the top and anyone up there climbs down it (see <see cref="Walk.ClimbsLadder"/>),
/// so a ladder is a quick, cheap way over a wall that does not break it, where a ramp is slow and costs stone. It is
/// fragile: an enemy standing on the top beside it, with none of the ladder's side up there with it, throws it down
/// after <see cref="LadderPushTicks"/>; it falls when the top of what it leans on, or the ground under its foot, is
/// gone; and a ladder nobody is raising any more is taken down.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Wood each rung of a ladder costs, taken as it goes in.</summary>
    public const int WoodPerRung = 2;
    /// <summary>Ticks of work (at a worker's plain work speed) for each rung of a ladder.</summary>
    public const int LadderRungTicks = Tps;
    /// <summary>Ticks an enemy holds the top beside a ladder, unopposed, before throwing it down.</summary>
    public const int LadderPushTicks = 2 * Tps;

    private readonly List<Ladder> _ladders = new();
    private int _nextLadder = 1;

    /// <summary>The ladders being raised, and those up, by id.</summary>
    public IReadOnlyList<Ladder> Ladders => _ladders;

    public Ladder? LadderById(int id)
    {
        foreach (var l in _ladders)
            if (l.Id == id) return l;
        return null;
    }

    /// <summary>The column a ladder from foot (x1, z1) dragged toward (x2, z2) leans on: the next one along whichever of x and z the drag goes further in.</summary>
    public static (int X, int Z) LadderFace(int x1, int z1, int x2, int z2)
    {
        if (x1 == x2 && z1 == z2) return (x1, z1);
        return Math.Abs(x2 - x1) >= Math.Abs(z2 - z1) ? (x1 + Math.Sign(x2 - x1), z1) : (x1, z1 + Math.Sign(z2 - z1));
    }

    /// <summary>
    /// Plans a ladder with its foot in column (x1, z1), leaning on the column beside it toward (x2, z2) (see
    /// <see cref="LadderFace"/>). The face's top (the level units stand at on it) must be 2 to
    /// <see cref="Walk.MaxLadder"/> levels over the ground at the foot, and the foot column must be open from the
    /// ground to two levels over the top (for the rungs, and for a climber's head at the top): no building, ground or
    /// trunk there, nor a city-state's town. Returns the top level, or -1; <paramref name="foot"/> is the ground level
    /// at the foot either way (-1 if none), for the game's preview.
    /// </summary>
    public int PlanLadder(int x1, int z1, int x2, int z2, out int foot)
    {
        var w = World;
        foot = -1;
        var (fx, fz) = LadderFace(x1, z1, x2, z2);
        if ((fx == x1 && fz == z1) || !w.InBounds(x1, 1, z1) || !w.InBounds(fx, 1, fz)) return -1;
        foot = Walk.GroundLevel(w, x1, z1);
        int top = Walk.GroundLevel(w, fx, fz);
        if (foot < 0 || top < 0 || top - foot < 2 || top - foot > Walk.MaxLadder || top + Walk.Clearance > w.SizeY - 1) return -1;
        if (x1 < 1 || z1 < 1 || x1 > w.SizeX - 2 || z1 > w.SizeZ - 2) return -1;
        if (Buildings.AtColumn(x1, z1) != null || _sim.Cities.Crowds(x1, z1, x1, z1)) return -1;
        for (int y = foot; y < top + Walk.Clearance; y++)
            if (w.Get(x1, y, z1) is var b && b != BlockType.Air && !BlockInfo.IsLeaves(b)) return -1;
        return top;
    }

    internal void Apply(LadderCommand c)
    {
        var units = OwnedUnits(c);
        units.RemoveAll(i => !CanDig(Content.Units[U.Def[i]]));
        if (units.Count == 0) return;
        int top = PlanLadder(c.X1, c.Z1, c.X2, c.Z2, out int foot);
        if (top < 0) return;
        var (fx, fz) = LadderFace(c.X1, c.Z1, c.X2, c.Z2);
        // Leaves in the way are pushed aside.
        for (int y = foot; y < top + Walk.Clearance; y++)
            if (BlockInfo.IsLeaves(World.Get(c.X1, y, c.Z1))) World.Set(c.X1, y, c.Z1, BlockType.Air);
        var ladder = new Ladder(_nextLadder++, c.PlayerId, c.X1, c.Z1, fx, fz, foot, top);
        _ladders.Add(ladder);
        foreach (int i in units)
        {
            StartJob(i, UnitJob.Ladder, ladder.Id);
            SetPhase(i, JobPhase.ToWork);
        }
    }

    /// <summary>True if a builder standing at level uy in column (ux, uz) can raise ladder l: at its foot, or beside it about level with it.</summary>
    public static bool LadderReach(Ladder l, int ux, int uy, int uz) =>
        Math.Abs(ux - l.X) <= 1 && Math.Abs(uz - l.Z) <= 1 && Math.Abs(uy - l.Foot) <= Walk.MaxClimb;

    /// <summary>The spot nearest a builder at (fx, fz) it can raise ladder l from: its foot, else a column beside it; false if none.</summary>
    private bool LadderSpot(Ladder l, int fx, int fz, out int sx, out int sy, out int sz)
    {
        var w = World;
        sx = sy = sz = -1;
        long best = long.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int cx = l.X + dx, cz = l.Z + dz;
            for (int cy = l.Foot - Walk.MaxClimb; cy <= l.Foot + Walk.MaxClimb; cy++)
            {
                if (!Walk.CanStand(w, cx, cy, cz)) continue;
                long ex = cx - fx, ez = cz - fz;
                // The foot itself is best, then level with it, then nearest the builder.
                long score = (dx == 0 && dz == 0 ? 0 : 1000) + 100L * Math.Abs(cy - l.Foot) + ex * ex + ez * ez;
                if (score >= best) continue;
                best = score;
                (sx, sy, sz) = (cx, cy, cz);
            }
        }
        return best != long.MaxValue;
    }

    private void StepLadder(int i)
    {
        var u = U;
        var ladder = LadderById(u.JobTarget[i]);
        if (ladder == null || ladder.Done)
        {
            EndJob(i);
            return;
        }
        if (u.Phase[i] == JobPhase.Wait)
        {
            if (--u.JobTimer[i] > 0) return;
            SetPhase(i, JobPhase.ToWork);
        }
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        bool there = LadderReach(ladder, ux, u.Y[i], uz);
        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    u.JobMisses[i] = 0;
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    if (LadderSpot(ladder, ux, uz, out int sx, out int sy, out int sz))
                    {
                        _sim.SendUnit(i, sx, sz, sy);
                        return;
                    }
                    break;
                case Travel.Walking:
                    return;
            }
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
        if (++u.JobTimer[i] < ByWork(i, LadderRungTicks)) return;
        u.JobTimer[i] = 0;
        int y = ladder.Foot + ladder.Built;
        if (!Ramp.Open(World, ladder.X, y, ladder.Z) || World.Get(ladder.X, y, ladder.Z) == BlockType.Water)
        {
            TakeDown(ladder); // something is in the way of the next rung
            EndJob(i);
            return;
        }
        if (Take(u.Owner[i], Good.Wood, WoodPerRung, (ladder.X, ladder.Z)) < WoodPerRung)
        {
            EndJob(i); // no wood left: the work waits for an order with wood to spare
            return;
        }
        Player(u.Owner[i]).Spent[(int)Good.Wood] += WoodPerRung;
        World.Set(ladder.X, y, ladder.Z, BlockType.Ladder);
        ladder.Built++;
        if (ladder.Done) EndJob(i);
    }

    /// <summary>
    /// Once a tick: each ladder falls if the ground under its foot, any of its rungs or (once it is up) the top of what it
    /// leans on is gone; and an enemy standing on the top beside it (level with its top, in a column next to its foot)
    /// with none of the ladder's side up there throws it down once it has held it <see cref="LadderPushTicks"/>.
    /// Once a second, a ladder nobody is raising any more, not yet up, is taken down.
    /// </summary>
    private void StepLadders()
    {
        if (_ladders.Count == 0) return;
        var w = World;
        bool prune = _sim.Tick % Tps == 0;
        for (int k = _ladders.Count - 1; k >= 0; k--)
        {
            var l = _ladders[k];
            bool stands = BlockInfo.CanStandOn(w.Get(l.X, l.Foot - 1, l.Z)) && (!l.Done || Walk.CanStand(w, l.FaceX, l.Top, l.FaceZ));
            for (int y = l.Foot; stands && y < l.Foot + l.Built; y++)
                stands = w.Get(l.X, y, l.Z) == BlockType.Ladder;
            if (!stands || (prune && !l.Done && !Raising(l)))
            {
                TakeDown(l);
                continue;
            }
            if (l.Built == 0) continue;
            if (Contested(l, out bool held) && !held)
            {
                if (++l.Push >= LadderPushTicks) TakeDown(l);
            }
            else l.Push = 0;
        }
    }

    /// <summary>True if any unit is at work raising ladder l.</summary>
    private bool Raising(Ladder l)
    {
        for (int i = 0; i < U.Count; i++)
            if (U.Job[i] == UnitJob.Ladder && U.JobTarget[i] == l.Id) return true;
        return false;
    }

    /// <summary>
    /// True if an enemy of ladder l's owner stands on the top beside it: at its top level, in a column next to its
    /// foot. <paramref name="held"/> says if one of the owner's units (not a machine) stands up there too.
    /// </summary>
    private bool Contested(Ladder l, out bool held)
    {
        bool enemy = false;
        held = false;
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Y[i] != l.Top) continue;
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            if (Math.Abs(x - l.X) > 1 || Math.Abs(z - l.Z) > 1 || (x == l.X && z == l.Z)) continue;
            if (u.Hp[i] <= 0 || Content.Units[u.Def[i]].Machine) continue;
            if (u.Owner[i] == l.Owner) held = true;
            else if (_sim.Truces.Hostile(u.Owner[i], l.Owner)) enemy = true;
        }
        return enemy;
    }

    /// <summary>Takes ladder l down (or it falls, or is thrown down): its rungs are gone.</summary>
    private void TakeDown(Ladder l)
    {
        for (int y = l.Foot; y < l.Foot + l.Built; y++)
            if (World.Get(l.X, y, l.Z) == BlockType.Ladder) World.Set(l.X, y, l.Z, BlockType.Air);
        _ladders.Remove(l);
    }

    private void MixLadders(ref ulong h)
    {
        foreach (var l in _ladders) l.Mix(ref h);
    }
}
