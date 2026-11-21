using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Great works (see <see cref="AscendancySystem"/>): the smart levels (Easy and up) answer a rival's finished great
/// work, which wins that rival the match if it stands long enough. Every soldier it can spare beyond the guard (and,
/// once nothing threatens home, the guard's share above <see cref="LeastGuard"/> too) goes to bring down the one
/// finished first, at once and whatever it sees on the way, and those that strayed are sent at it again until it is
/// down. Everyone is told where a finished great work stands, so it needs no sight of it. Computer players do not
/// build great works of their own yet. The classic level leaves them be.
/// </summary>
public sealed partial class AiPlayer
{
    // The soldiers sent against a rival's great work, and the building they are after (0 while none is).
    private readonly List<int> _strike = new();
    private int _strikeAt;

    /// <summary>The rival's great work (a building id) its strike force is after, or 0.</summary>
    public int StrikeTarget => _strikeAt;

    /// <summary>Soldiers in the strike force against a rival's great work, by id.</summary>
    public IReadOnlyList<int> StrikeForce => _strike;

    /// <summary>The strike against a rival's great work: see the class notes. Soldiers in it are taken out of <paramref name="home"/>.</summary>
    private void GreatWorkStep(Simulation sim, Building hall, List<int> home, int guardPercent, Action<Command> issue)
    {
        var u = sim.Units;
        _strike.RemoveAll(id => !u.Exists(id));
        AscendancySystem.Work? target = null;
        foreach (var w in sim.Ascendancy.Works)
            if (w.Owner != Player && w.FinishedAt >= 0 && (target == null || w.FinishedAt < target.FinishedAt)) target = w;
        if (target == null || _strikeAt != 0 && _strikeAt != target.Building)
        {
            if (_strike.Count > 0)
            {
                var (rx, rz) = RallyAt(sim, hall, null);
                issue(new MoveCommand(Player, _strike.ToArray(), rx, rz));
                Note(sim, "its strike force comes home: no rival's great work stands");
            }
            _strike.Clear();
            _strikeAt = 0;
            if (target == null) return;
        }
        home.RemoveAll(_strike.Contains);

        var (hx, hz) = Middle(hall);
        bool attacked = NearestEnemy(sim, hx, hz, 2 * DefendRadius, soldiersOnly: true) >= 0;
        var ready = new List<int>(home);
        ready.RemoveAll(_mending.Contains);
        ready.RemoveAll(id => id == _scout || id == _explorer);
        // The match is at stake: with nothing at the door, only the least guard stays home.
        int guard = attacked ? Math.Max(LeastGuard, home.Count * guardPercent / 100) : LeastGuard;
        var sent = new List<int>();
        if (ready.Count > guard)
        {
            SortNearest(sim, ready, target.X, target.Z);
            sent.AddRange(ready.GetRange(0, ready.Count - guard));
            _strike.AddRange(sent);
            home.RemoveAll(sent.Contains);
        }
        if (_strikeAt == 0)
        {
            _strikeAt = target.Building;
            Note(sim, $"sends {_strike.Count} to bring down player {target.Owner}'s great work at {target.X},{target.Z}");
        }
        // Those just sent, and every few seconds any that wandered off, go at the great work.
        bool resend = sim.Tick % (5 * ArmyTicks) < ArmyTicks;
        var go = resend ? _strike : sent;
        if (go.Count > 0) issue(new AttackBuildingCommand(Player, go.ToArray(), target.Building));
    }
}
