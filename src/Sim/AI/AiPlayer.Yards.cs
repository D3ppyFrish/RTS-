using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Yards at the smart levels (Easy and up). A siege workshop mends its owner's catapults and battering rams that wait
// beside it out of the fight (see EconomySystem.YardFor). A machine of its own that has come home battered (below
// MachineMendPercent of its hit points) and stands idle is taken out of the army like a wounded soldier (see Mend and
// _mending), but it waits beside the nearest of its finished workshops rather than at the town hall door, and it is let
// back only once it is whole again. With no workshop left standing it goes to the town hall door like anyone hurt and
// rejoins at HealedPercent. A wave's machines below WoundedPercent leave it as its soldiers do (see Withdraw), so they
// end up at the workshop too. Ships are not handled here: they already sail home to the dock (see AiPlayer.Ships.cs).
public sealed partial class AiPlayer
{
    /// <summary>A machine at home below this share (percent) of its hit points goes to its yard to be mended whole.</summary>
    public const int MachineMendPercent = 95;

    /// <summary>Machines it has sent to a yard to be mended (counted once each time one is sent).</summary>
    public int MachinesToYard { get; private set; }

    /// <summary>
    /// The finished building of its own nearest unit i that mends it (a siege workshop for a catapult or a ram), or
    /// null: i is a machine on land that is trained at a building that mends (<see cref="BuildingDef.Mends"/>).
    /// </summary>
    private Building? YardOf(Simulation sim, int i)
    {
        var u = sim.Units;
        var def = sim.Content.Units[u.Def[i]];
        if (!def.Machine || def.IsBoat || def.Sails || def.TrainedAt is not { Mends: > 0 } at) return null;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        Building? best = null;
        long bestD = long.MaxValue;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || !b.Active || b.Def != at) continue;
            long d = Dist2(b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2, x, z);
            if (d < bestD) (best, bestD) = (b, d);
        }
        return best;
    }

    /// <summary>True if unit i stands near enough to the yard to be mended there (a column inside its reach).</summary>
    private static bool AtYard(Simulation sim, int i, Building yard)
    {
        var u = sim.Units;
        return yard.ColumnDistance(u.X[i].FloorToInt(), u.Z[i].FloorToInt()) <= EconomySystem.YardReach - 1
            && Math.Abs(u.Y[i] - yard.Y) <= EconomySystem.YardReach;
    }

    /// <summary>
    /// True if unit i, sent home to heal, is done: a machine with a yard once it is whole, anyone else once back to
    /// <see cref="HealedPercent"/>.
    /// </summary>
    private bool Mended(Simulation sim, int i)
    {
        if (YardOf(sim, i) == null) return !Hurt(sim, i, HealedPercent);
        var u = sim.Units;
        return u.Hp[i] >= sim.Economy.MaxHp(Player, sim.Content.Units[u.Def[i]]);
    }

    /// <summary>
    /// Battered machines standing idle at home (not in the wave or on their way to it, below
    /// <see cref="MachineMendPercent"/>, with a yard to go to) are taken out of the army to be mended.
    /// </summary>
    private void BatteredMachines(Simulation sim)
    {
        var u = sim.Units;
        foreach (int id in Soldiers(sim))
        {
            if (_wave.Contains(id) || _mending.Contains(id) || _joining.Contains(id)) continue;
            int i = u.IndexOf(id);
            if (u.Aboard[i] != 0 || u.Order[i] != UnitOrder.Idle || u.Target[i] != 0 || u.Rout[i] > 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.Machine || u.Hp[i] * 100L >= (long)sim.Economy.MaxHp(Player, def) * MachineMendPercent) continue;
            if (YardOf(sim, i) is not { } yard) continue;
            _mending.Add(id);
            MachinesToYard++;
            Note(sim, $"sends its battered {UnitName(sim, i).ToLowerInvariant()} to the {yard.Def.Name.ToLowerInvariant()} to be mended");
        }
    }

    /// <summary>Sends the machines among <paramref name="ids"/> that have a yard and are not beside it there, one order per yard; returns the rest.</summary>
    private List<int> SendToYards(Simulation sim, List<int> ids, Action<Command> issue)
    {
        var u = sim.Units;
        var rest = new List<int>();
        SortedDictionary<int, List<int>>? byYard = null;
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            if (YardOf(sim, i) is not { } yard)
            {
                rest.Add(id);
                continue;
            }
            if (AtYard(sim, i, yard)) continue;
            byYard ??= new();
            if (!byYard.TryGetValue(yard.Id, out var list)) byYard[yard.Id] = list = new List<int>();
            list.Add(id);
        }
        if (byYard != null)
            foreach (var (yardId, list) in byYard)
            {
                var (ax, az) = Approach(sim, sim.Buildings.Get(yardId)!);
                issue(new MoveCommand(Player, list.ToArray(), ax, az));
            }
        return rest;
    }
}
