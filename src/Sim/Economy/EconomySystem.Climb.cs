using System;
using Rts.Sim.Content;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Wall climbing, Clan Kage's (see "Clan Kage" in docs/rules.md): a unit whose player has the tech for it
/// (<see cref="TechEffectType.WallClimbing"/>, their kunoichi, shinobi and shuriken throwers) climbs the face of a wall,
/// gate, tower or cliff with no ladder (<see cref="ClimbCommand"/>). It walks to the foot of the face, the ground beside
/// it, and climbs <see cref="ClimbRules.TicksPerLevel"/> a level onto the top; or it climbs down from a top to the ground
/// beside it the same way. A climb goes from 2 (one level is a step) to <see cref="ClimbRules.Height"/> levels. A climber
/// that is hit lets go and fights (see <see cref="CombatSystem"/>). Up on a wall, it holds its ground there as anyone
/// posted on one does, and comes down off it when it is sent on (see <see cref="UnitMovement"/>'s climbing down).
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Climbs made since the match began.</summary>
    public long Climbs { get; private set; }

    private ClimbRules ClimbRules => Content.Combat.Climb;

    /// <summary>
    /// A climb: from the foot, column (FootX, FootZ) standing at level FootY, up (or down) the face onto the
    /// neighbouring column (ToX, ToZ) at level ToY.
    /// </summary>
    public readonly record struct ClimbRoute(int FootX, int FootZ, int FootY, int ToX, int ToZ, int ToY)
    {
        /// <summary>Levels the climb goes up (or down).</summary>
        public int Levels => Math.Abs(ToY - FootY);
    }

    /// <summary>True if unit i, of its type and its player's techs, can climb walls at all (free to or not).</summary>
    public bool CanClimb(int i) =>
        (uint)i < (uint)U.Count && U.Town[i] == 0 && U.Owner[i] < Simulation.WildPlayer && CanClimb(U.Owner[i], Content.Units[U.Def[i]]);

    /// <summary>True if unit i can set out to climb now: it can at all, and it is free to (not beaten, running, aboard or penned).</summary>
    public bool ReadyToClimb(int i) =>
        CanClimb(i) && U.Hp[i] > 0 && !U.Subdued[i] && U.Aboard[i] == 0 && U.Penned[i] == 0 && U.Rout[i] == 0 && U.RunStage[i] == 0;

    /// <summary>
    /// True if a climber standing at level fy in column (fx, fz) can climb the face into the neighbouring column (tx, tz)
    /// (never on a diagonal) and stand there at level ty: 2 to <see cref="ClimbRules.Height"/> levels up or down, and the
    /// lower of the two columns, where it hangs on the face, open from its feet to a head over the top.
    /// </summary>
    public bool Climbable(int fx, int fz, int fy, int tx, int tz, int ty)
    {
        var w = World;
        if (Math.Abs(fx - tx) + Math.Abs(fz - tz) != 1) return false;
        int levels = Math.Abs(ty - fy);
        if (levels < 2 || levels > ClimbRules.Height) return false;
        int low = Math.Min(fy, ty), high = Math.Max(fy, ty);
        if (high + Walk.Clearance > w.SizeY - 1 || !Walk.CanStand(w, fx, fy, fz) || !Walk.CanStand(w, tx, ty, tz)) return false;
        int lx = ty > fy ? fx : tx, lz = ty > fy ? fz : tz;
        for (int y = low + 1; y <= high + 1; y++)
            if (!Walk.Headroom(w.Get(lx, y, lz))) return false;
        return true;
    }

    /// <summary>
    /// The climb a climber standing at (nearX, nearY, nearZ) makes when sent to climb at column (x, z): onto a column
    /// within <see cref="ClimbRules.Reach"/> of it at about the level of the ground there (its highest, not on leaves),
    /// from the ground beside it, the foot nearest the climber first. A climb that would leave the climber at about the
    /// level it stands at is none (it walks there, or clicks the top). False if there is none.
    /// </summary>
    public bool PlanClimb(int x, int z, int nearX, int nearY, int nearZ, out ClimbRoute route)
    {
        var w = World;
        route = default;
        if (!w.InBounds(x, 1, z)) return false;
        int clicked = Walk.GroundLevel(w, x, z);
        if (clicked < 0) return false;
        int reach = ClimbRules.Reach;
        long bestFoot = long.MaxValue, bestTo = long.MaxValue;
        for (int tz = z - reach; tz <= z + reach; tz++)
        for (int tx = x - reach; tx <= x + reach; tx++)
        {
            if (!w.InBounds(tx, 1, tz)) continue;
            int ty = Walk.GroundLevel(w, tx, tz);
            if (ty < 0 || Math.Abs(ty - clicked) > 1 || Math.Abs(ty - nearY) <= Walk.MaxClimb) continue;
            long to = (long)(tx - x) * (tx - x) + (long)(tz - z) * (tz - z);
            foreach (var (dx, dz) in ClimbSides)
            {
                int fx = tx + dx, fz = tz + dz;
                if (!w.InBounds(fx, 1, fz)) continue;
                long foot = (long)(fx - nearX) * (fx - nearX) + (long)(fz - nearZ) * (fz - nearZ);
                if (foot > bestFoot || foot == bestFoot && to >= bestTo) continue;
                int fy = Walk.GroundLevel(w, fx, fz);
                if (fy < 0 || !Climbable(fx, fz, fy, tx, tz, ty)) continue;
                (bestFoot, bestTo) = (foot, to);
                route = new ClimbRoute(fx, fz, fy, tx, tz, ty);
            }
        }
        return bestFoot != long.MaxValue;
    }

    private static readonly (int X, int Z)[] ClimbSides = { (0, -1), (-1, 0), (1, 0), (0, 1) };

    /// <summary>The climb unit i is on (see <see cref="UnitJob.Climb"/>), or false if it is on none or the face is gone.</summary>
    public bool ClimbOf(int i, out ClimbRoute route)
    {
        route = default;
        if ((uint)i >= (uint)U.Count || U.Job[i] != UnitJob.Climb) return false;
        int fx = U.JobTarget[i] / World.SizeZ, fz = U.JobTarget[i] % World.SizeZ;
        int fy = Walk.GroundLevel(World, fx, fz);
        route = new ClimbRoute(fx, fz, fy, U.JobX[i], U.JobZ[i], U.JobY[i]);
        return fy >= 0 && Climbable(fx, fz, fy, route.ToX, route.ToZ, route.ToY);
    }

    internal void Apply(ClimbCommand c)
    {
        foreach (int i in OwnedUnits(c))
        {
            if (!ReadyToClimb(i)) continue;
            if (!PlanClimb(c.X, c.Z, U.X[i].FloorToInt(), U.Y[i], U.Z[i].FloorToInt(), out var r)) continue;
            StartJob(i, UnitJob.Climb, r.FootX * World.SizeZ + r.FootZ);
            U.JobX[i] = r.ToX;
            U.JobY[i] = r.ToY;
            U.JobZ[i] = r.ToZ;
        }
    }

    private void StepClimb(int i)
    {
        var u = U;
        if (!ReadyToClimb(i) || !ClimbOf(i, out var r))
        {
            EndJob(i); // beaten or running, or the face is gone or something is in the way
            return;
        }
        bool there = u.X[i].FloorToInt() == r.FootX && u.Z[i].FloorToInt() == r.FootZ && Math.Abs(u.Y[i] - r.FootY) <= Walk.MaxClimb;
        if (u.Phase[i] == JobPhase.ToWork)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    break;
                case Travel.NeedSpot:
                    _sim.SendUnit(i, r.FootX, r.FootZ, r.FootY);
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
        if (++u.JobTimer[i] < ClimbRules.TicksPerLevel * r.Levels) return;
        // Over the top (or down at the bottom): it stands in the middle of the column.
        u.X[i] = Fix64.FromInt(r.ToX) + Fix64.Half;
        u.Z[i] = Fix64.FromInt(r.ToZ) + Fix64.Half;
        u.Y[i] = r.ToY;
        EndJob(i);
        Climbs++;
    }
}
