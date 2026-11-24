using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Molting (issue #153), at every level. A player whose units can molt (the Deepfolk's Molting) takes each badly hurt
// one out of its army (see Soldiers and the wave): with a rival soldier it can see within MoltSafe columns it walks
// home to the town hall, and once none is that close it molts there. It rejoins the army once it has molted and its
// new shell has hardened (no longer soft).
public sealed partial class AiPlayer
{
    /// <summary>A unit pulled back to molt does so only with no rival soldier it can see this close (columns).</summary>
    public const int MoltSafe = 10;

    // The units pulled back to molt, molting or soft after it, by id in the order they were taken out.
    private readonly List<int> _molters = new();

    /// <summary>The units it has pulled back to molt, molting, or soft after it (left out of the army).</summary>
    public IReadOnlyList<int> Molters => _molters;

    /// <summary>
    /// Takes newly badly hurt units that can molt out of the army; walks those with an enemy close home, and has the
    /// others molt; lets those done (molted and hard again) back into the army.
    /// </summary>
    private void Molt(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        _molters.RemoveAll(id =>
        {
            int i = u.IndexOf(id);
            return i < 0 || (u.Molting[i] == 0 && u.Soft[i] == 0 && !sim.Combat.ReadyToMolt(i));
        });
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || !sim.Combat.ReadyToMolt(i) || _molters.Contains(u.Id[i])) continue;
            _molters.Add(u.Id[i]);
            _wave.Remove(u.Id[i]);
        }
        if (_molters.Count == 0) return;
        List<int>? molt = null, back = null;
        foreach (int id in _molters)
        {
            int i = u.IndexOf(id);
            if (!sim.Combat.ReadyToMolt(i)) continue; // molting, or soft after it
            if (EnemyClose(sim, i, MoltSafe)) (back ??= new()).Add(id);
            else (molt ??= new()).Add(id);
        }
        if (molt != null) issue(new MoltCommand(Player, molt.ToArray()));
        if (back != null) issue(new MoveCommand(Player, back.ToArray(), hall.X - 2, hall.Z - 2));
    }

    /// <summary>True if a rival soldier it can see stands within <paramref name="reach"/> columns of unit i.</summary>
    private bool EnemyClose(Simulation sim, int i, int reach)
    {
        var u = sim.Units;
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        long r2 = (long)reach * reach;
        for (int e = 0; e < u.Count; e++)
        {
            if (!Rival(u.Owner[e]) || u.Subdued[e]) continue;
            var def = sim.Content.Units[u.Def[e]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (Dist2(ux, uz, u.X[e].FloorToInt(), u.Z[e].FloorToInt()) > r2 || !sim.Vision.SeesUnit(Player, e)) continue;
            return true;
        }
        return false;
    }
}
