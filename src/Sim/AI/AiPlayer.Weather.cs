using System;

namespace Rts.Sim.AI;

// Wet ground, from Normal up: a wave does not set out across mud in the rain. While it rains or storms on muddy ground
// (Conditions.MuddyFrom) the army waits at home for the ground to dry, unless it is far stronger than the enemy it
// knows of, and for no longer than MudHoldTicks at a time; a wave already out keeps going. The classic player marches
// whatever the weather.
public sealed partial class AiPlayer
{
    /// <summary>Longest a wave waits at home for the mud to dry: three hours of the day.</summary>
    public const int MudHoldTicks = 3 * Conditions.HourTicks;

    private long _mudSince = -1;

    /// <summary>True if the ground is muddy and it is still raining or storming: no time to march.</summary>
    public static bool Mud(Simulation sim) =>
        sim.Conditions.Wetness >= Conditions.MuddyFrom && sim.Conditions.Weather is Weather.Rain or Weather.Storm;

    /// <summary>
    /// True if a wave worth <paramref name="mine"/> should wait at home for the mud to dry before it sets out
    /// against <paramref name="theirs"/>: only in the mud, not when it is twice as strong, and not for more than
    /// <see cref="MudHoldTicks"/> in a row.
    /// </summary>
    private bool MudHolds(Simulation sim, long mine, long theirs)
    {
        if (!Mud(sim) || mine > 2 * theirs)
        {
            _mudSince = -1;
            return false;
        }
        if (_mudSince < 0)
        {
            _mudSince = sim.Tick;
            if (Level >= AiLevel.Hard) Note(sim, "holds its wave at home while the rain turns the ground to mud");
        }
        return sim.Tick - _mudSince < MudHoldTicks;
    }
}
