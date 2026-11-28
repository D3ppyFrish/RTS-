using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Glamour (the Fey's, see "The Fey" in docs/rules.md), at every level. A player whose casters can weave illusions has
// each ready one with a rival soldier it can see within GlamourClose columns weave them, so the rival's blows and shots
// go on the decoys. Its illusions that stand with nothing to fight are sent on at the nearest rival soldier it sees within
// DecoyReach columns of them (an attack-move), to draw the fight; they are never part of the army (see Soldiers).
public sealed partial class AiPlayer
{
    /// <summary>A ready caster weaves its illusions with a rival soldier it can see this close (columns).</summary>
    public const int GlamourClose = 10;

    /// <summary>Its idle illusions go at a rival soldier it sees this close to them (columns).</summary>
    public const int DecoyReach = 16;

    /// <summary>Has its ready casters with a rival close weave illusions, and sends its idle illusions at the nearest rival.</summary>
    private void Glamour(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        var combat = sim.Combat;
        List<int>? weave = null;
        bool illusions = false;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player) continue;
            if (u.Illusion[i] > 0) illusions = true;
            else if (combat.ReadyToGlamour(i) && EnemyClose(sim, i, GlamourClose)) (weave ??= new()).Add(u.Id[i]);
        }
        if (weave != null) issue(new GlamourCommand(Player, weave.ToArray()));
        if (!illusions) return;
        // Idle illusions, by the rival soldier they go at (by index), so that those going at the same one go together.
        var orders = new SortedDictionary<int, List<int>>();
        long r2 = (long)DecoyReach * DecoyReach;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || u.Illusion[i] == 0 || u.Rout[i] > 0) continue;
            if (u.Target[i] != 0 || u.Order[i] != UnitOrder.Idle || u.Combat[i] != CombatOrder.Auto) continue;
            int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
            int best = -1;
            long bestD = 0;
            for (int e = 0; e < u.Count; e++)
            {
                if (!Rival(u.Owner[e]) || u.Subdued[e]) continue;
                var def = sim.Content.Units[u.Def[e]];
                if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
                long d = Dist2(ux, uz, u.X[e].FloorToInt(), u.Z[e].FloorToInt());
                if (d > r2 || (best >= 0 && d >= bestD) || !sim.Vision.SeesUnit(Player, e)) continue;
                (best, bestD) = (e, d);
            }
            if (best < 0) continue;
            if (!orders.TryGetValue(best, out var list)) orders[best] = list = new();
            list.Add(u.Id[i]);
        }
        foreach (var (e, ids) in orders)
            issue(new AttackMoveCommand(Player, ids.ToArray(), u.X[e].FloorToInt(), u.Z[e].FloorToInt()));
    }
}
