using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// War drummers (issue #151), at every level. A player whose barracks can train a war drummer keeps one (see Train and
// Weight). Each wave on its way to a building takes a drummer along if it has none and one stands free at home, and
// once the wave is within SiegeReach of its target, every drummer in it that is ready beats the charge.
public sealed partial class AiPlayer
{
    /// <summary>War drummers it keeps, with those queued at its barracks: one goes with each wave.</summary>
    public const int DrummersWanted = 1;

    /// <summary>Its war drummers, with those queued at its barracks.</summary>
    private int Drummers(Simulation sim)
    {
        int n = 0;
        var u = sim.Units;
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == Player && sim.Content.Units[u.Def[i]].Drums != null) n++;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.Barracks) continue;
            foreach (int q in b.Queue) if (sim.Content.Units[q].Drums != null) n++;
        }
        return n;
    }

    /// <summary>
    /// The war drummer barracks b can train for the classic level, if it has fewer than <see cref="DrummersWanted"/>; else null.
    /// </summary>
    private UnitDef? Drummer(Simulation sim, Building b)
    {
        foreach (string id in b.Def.Trains)
            if (sim.Content.TryGetUnit(id, out var d) && d.Drums != null && sim.Economy.Unlocked(Player, d))
                return Drummers(sim) < DrummersWanted ? d : null;
        return null;
    }

    /// <summary>
    /// The wave on a building, (far) columns from its target at (tx, tz): on the way, it takes a free drummer from home
    /// along if it has none; at the target, its ready drummers beat the charge.
    /// </summary>
    private void Drum(Simulation sim, int far, int tx, int tz, Action<Command> issue)
    {
        var u = sim.Units;
        List<int>? ready = null;
        bool any = false;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || sim.Content.Units[u.Def[i]].Drums == null) continue;
            any = true;
            if (far <= SiegeReach && sim.Combat.CanCharge(i)) (ready ??= new()).Add(id);
        }
        if (ready != null)
        {
            issue(new ChargeCommand(Player, ready.ToArray()));
            Note(sim, $"beats the charge with {ready.Count} drummer{(ready.Count == 1 ? "" : "s")} {far} columns from its target");
            return;
        }
        if (any || far <= SiegeReach) return;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || sim.Content.Units[u.Def[i]].Drums == null) continue;
            if (u.Rout[i] > 0 || u.Subdued[i] || u.Aboard[i] != 0 || u.Town[i] != 0 || u.Job[i] != UnitJob.None) continue;
            int id = u.Id[i];
            if (_joining.Contains(id) || _engines.Contains(id) || _shield.Contains(id) || Crewed(id)) continue;
            _wave.Add(id);
            issue(new AttackMoveCommand(Player, new[] { id }, tx, tz));
            Note(sim, "sends a war drummer after its wave");
            return;
        }
    }
}
