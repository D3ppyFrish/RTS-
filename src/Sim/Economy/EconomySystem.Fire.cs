using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Setting trees alight (<see cref="KindleCommand"/>): a worker or soldier walks to the tree, works at it
/// <see cref="KindleTicks"/>, and its trunk catches (see <see cref="World.FireSystem"/>); then it walks
/// <see cref="KindleRetreat"/> columns away from the tree, clear of the flames. Not in rain or a storm: the
/// order is refused then, and a unit that gets there as the rain comes gives up.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>Ticks of work to set a tree alight.</summary>
    public const int KindleTicks = 3 * Simulation.TicksPerSecond;

    /// <summary>True if unit i can set a tree alight: a worker or soldier, not a machine, boat, cart, caravan or monster.</summary>
    public bool CanKindle(int i)
    {
        var def = Content.Units[U.Def[i]];
        return !def.Machine && def.Role is not (UnitRole.Monster or UnitRole.Trader or UnitRole.Hauler or UnitRole.Boat);
    }

    internal void Apply(KindleCommand c)
    {
        if (!_sim.Fire.CanKindle) return;
        if (!FindTree(c.X, c.Z, out int tx, out int baseY, out int tz)) return;
        foreach (int i in OwnedUnits(c))
        {
            if (!CanKindle(i)) continue;
            StartJob(i, UnitJob.Kindle, 0);
            U.JobX[i] = tx;
            U.JobY[i] = baseY;
            U.JobZ[i] = tz;
        }
    }

    private void StepKindle(int i)
    {
        var u = U;
        int x = u.JobX[i], y = u.JobY[i], z = u.JobZ[i];
        if (!BlockInfo.IsTrunk(World.Get(x, y, z)) || _sim.Fire.Burning(x, y, z) || _sim.Fire.Burning(x, y + 1, z))
        {
            EndJob(i); // burning already, felled or gone
            return;
        }
        var (baseY, topY) = Harvest.Span(World, x, y, z);
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        bool there = Harvest.InReach(ux, u.Y[i], uz, x, baseY, topY, z);
        if (u.Phase[i] != JobPhase.Working)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    if (Harvest.WorkSpot(World, x, baseY, topY, z, ux, u.Y[i], uz, out int sx, out int sy, out int sz, allowOnTop: false))
                    {
                        _sim.SendUnit(i, sx, sz, sy);
                        return;
                    }
                    break;
                case Travel.Walking:
                    return;
            }
            EndJob(i); // no way to it
            return;
        }
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (!_sim.Fire.CanKindle)
        {
            EndJob(i); // the rain came
            return;
        }
        if (++u.JobTimer[i] < KindleTicks) return;
        // The trunk a block up from its foot, where a torch is held to it (the foot, on a stump of one).
        int at = topY > baseY ? baseY + 1 : baseY;
        _sim.Fire.Ignite(x, at, z, u.Owner[i]);
        EndJob(i);
        // Then it gets clear of the fire: away from the tree, out from under the crowns round it.
        int dx = System.Math.Sign(ux - x), dz = System.Math.Sign(uz - z);
        if (dx == 0 && dz == 0) dx = 1;
        int rx = System.Math.Clamp(x + dx * KindleRetreat, 0, World.SizeX - 1), rz = System.Math.Clamp(z + dz * KindleRetreat, 0, World.SizeZ - 1);
        int ry = Walk.GroundLevel(World, rx, rz);
        if (ry >= 1) _sim.SendUnit(i, rx, rz, ry);
    }

    /// <summary>Columns from the tree a unit that set it alight walks off to, clear of the fire.</summary>
    public const int KindleRetreat = 6;
}
