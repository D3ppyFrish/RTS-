using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Smoke bombs (Clan Kage's, see "Clan Kage" in docs/rules.md), at every level. A player whose units can throw them pulls
// each badly hurt one (down to the retreat share of its hit points) with a rival soldier it can see within SmokeClose
// columns out of the fight: it throws a smoke bomb at its feet and walks home to the town hall in the cloud. It is left
// out of the army for SlipSeconds, then rejoins it.
public sealed partial class AiPlayer
{
    /// <summary>A hurt unit throws its smoke bomb with a rival soldier it can see this close (columns).</summary>
    public const int SmokeClose = 4;

    /// <summary>Seconds a unit that slipped away in the smoke is left out of the army.</summary>
    public const int SlipSeconds = 20;

    // The units slipping away home in their smoke, by id, with the tick each rejoins the army.
    private readonly List<(int Id, long Until)> _slipping = new();

    /// <summary>The units that threw a smoke bomb and are slipping away home (left out of the army a while).</summary>
    public IEnumerable<int> Slipping
    {
        get { foreach (var s in _slipping) yield return s.Id; }
    }

    /// <summary>
    /// Has its badly hurt units that can throw a smoke bomb, with a rival close, throw it and walk home; lets those that
    /// slipped away long enough ago back into the army.
    /// </summary>
    private void Smoke(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        long tick = sim.Tick;
        if (_slipping.Count > 0) _slipping.RemoveAll(s => s.Until <= tick || !u.Exists(s.Id));
        List<int>? slip = null;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || !sim.Combat.ReadyToSmoke(i) || !sim.Combat.Hurt(i)) continue;
            if (!EnemyClose(sim, i, SmokeClose)) continue;
            (slip ??= new()).Add(u.Id[i]);
        }
        if (slip == null) return;
        foreach (int id in slip)
        {
            _wave.Remove(id);
            _slipping.Add((id, tick + SlipSeconds * Simulation.TicksPerSecond));
        }
        var ids = slip.ToArray();
        issue(new SmokeCommand(Player, ids));
        issue(new MoveCommand(Player, ids, hall.X - 2, hall.Z - 2));
    }
}
