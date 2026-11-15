using System;
using Rts.Sim.Units;

namespace Rts.Sim.Economy;

/// <summary>
/// Cleaning blood (<see cref="CleanCommand"/>): workers go to the nearest stain around the spot they were sent
/// to that no other cleaner has, scrub it for a while (longer for more blood) and move on, until none is left
/// within <see cref="CleanRadius"/> columns. Blood brings wolves (they follow fresh trails) and, later, darker
/// things, so a tidy town is a safer one.
/// </summary>
public sealed partial class EconomySystem
{
    /// <summary>How far (columns) from the spot they were sent to cleaners look for blood.</summary>
    public const int CleanRadius = 8;
    /// <summary>Ticks of scrubbing a stain takes at least, and ticks more for each blood in it.</summary>
    public const int CleanTicks = Simulation.TicksPerSecond;
    public const int CleanTicksPerBlood = 1;

    internal void Apply(CleanCommand c)
    {
        if (!World.InBounds(c.X, 0, c.Z)) return;
        foreach (int i in Workers(c))
        {
            StartJob(i, UnitJob.Clean, c.X * World.SizeZ + c.Z);
            if (!NextStain(i)) EndJob(i);
        }
    }

    private void StepClean(int i)
    {
        var u = U;
        int x = u.JobX[i], z = u.JobZ[i];
        if (_sim.Blood.At(x, z) == 0)
        {
            // Gone (someone else scrubbed it, or it faded or washed away): on to the next one.
            if (NextStain(i)) SetPhase(i, JobPhase.ToWork);
            else EndJob(i);
            return;
        }
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        bool there = Math.Abs(ux - x) <= 1 && Math.Abs(uz - z) <= 1 && Math.Abs(u.Y[i] - (u.JobY[i] + 1)) <= 1;
        if (u.Phase[i] != JobPhase.Working)
        {
            switch (Approach(i, there))
            {
                case Travel.Arrived:
                    SetPhase(i, JobPhase.Working);
                    return;
                case Travel.NeedSpot:
                    _sim.SendUnit(i, x, z, u.JobY[i] + 1);
                    return;
                case Travel.Walking:
                    return;
            }
            // Cannot get to it: try another one, or stop.
            if (++u.JobMisses[i] < MaxMisses && NextStain(i, skipCurrent: true)) SetPhase(i, JobPhase.ToWork);
            else EndJob(i);
            return;
        }
        if (!there)
        {
            SetPhase(i, JobPhase.ToWork);
            return;
        }
        if (++u.JobTimer[i] < ByWork(i, CleanTicks + _sim.Blood.At(x, z) * CleanTicksPerBlood)) return;
        _sim.Blood.Clean(x, z);
        u.JobMisses[i] = 0;
        if (NextStain(i)) SetPhase(i, JobPhase.ToWork);
        else EndJob(i);
    }

    /// <summary>Sends cleaner i on to the nearest stain around its spot that no other cleaner is on. False if none.</summary>
    private bool NextStain(int i, bool skipCurrent = false)
    {
        var u = U;
        int centre = u.JobTarget[i];
        int cx = centre / World.SizeZ, cz = centre % World.SizeZ;
        int was = skipCurrent ? u.JobX[i] * World.SizeZ + u.JobZ[i] : -1;
        bool Taken(int x, int z)
        {
            if (x * World.SizeZ + z == was) return true;
            for (int j = 0; j < u.Count; j++)
                if (j != i && u.Job[j] == UnitJob.Clean && u.JobX[j] == x && u.JobZ[j] == z && u.Owner[j] == u.Owner[i]) return true;
            return false;
        }
        if (!_sim.Blood.Nearest(cx, cz, CleanRadius, u.X[i].FloorToInt(), u.Z[i].FloorToInt(), Taken, out int sx, out int sy, out int sz))
            return false;
        u.JobX[i] = sx;
        u.JobY[i] = sy;
        u.JobZ[i] = sz;
        u.JobTimer[i] = 0;
        return true;
    }
}
