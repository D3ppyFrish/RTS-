using System;

namespace Rts.Sim.Units;

/// <summary>
/// Fear: the forests' giant hornets sting (<see cref="UnitDef.Fear"/>), and so does the bearer of a queen's sting
/// (<see cref="Content.RelicDef.Fear"/>). Each blow of theirs that lands (not one a shield stops, nor a blast's splash)
/// takes that much morale from the unit struck, landing with the tick's hits, so a swarm sends a squad running before
/// it has killed many: a unit whose morale falls to <see cref="BreakMorale"/> routs as it would from its friends dying.
/// A hero near the stung takes the same share of the sting out of it as of a death's (see <see cref="Rally"/>).
/// Monsters, heroes, ships and machines are never frightened.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Stings that took morale since the match began.</summary>
    public long Frights { get; private set; }

    // The morale each unit is to lose to this tick's stings; it lands with the tick's hits.
    private int[] _fear = Array.Empty<int>();

    /// <summary>Morale unit i's blows take from whom they strike: its own sting and its relic's.</summary>
    public int FearOf(int i, UnitDef def) => def.Fear + (_sim.RelicOf(i)?.Fear ?? 0);

    /// <summary>True if unit t's morale can be shaken by a sting.</summary>
    private bool Frightened(int t)
    {
        var target = Def(t);
        return target.Role != UnitRole.Monster && !target.Hero && !target.Machine && !target.IsBoat;
    }

    /// <summary>Unit i's blow has landed on unit t: t is to lose morale, if i's blows frighten and t can be frightened.</summary>
    private void Frighten(int i, UnitDef def, int t)
    {
        int fear = FearOf(i, def);
        if (fear <= 0 || !Frightened(t)) return;
        if (_fear.Length < U.Capacity) Array.Resize(ref _fear, U.Capacity);
        _fear[t] += fear;
        Frights++;
    }

    /// <summary>The tick's stings take their morale, before the tick's deaths are mourned.</summary>
    private void LandFear()
    {
        var u = U;
        int n = Math.Min(u.Count, _fear.Length);
        for (int j = 0; j < n; j++)
        {
            int fear = _fear[j];
            if (fear == 0) continue;
            _fear[j] = 0;
            if (u.Hp[j] <= 0) continue;
            if (_anyRally) fear = fear * (100 - _steady[j]) / 100;
            u.Morale[j] = Math.Max(0, u.Morale[j] - fear);
        }
    }
}
