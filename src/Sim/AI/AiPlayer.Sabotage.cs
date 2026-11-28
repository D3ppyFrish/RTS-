using System;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Sabotage (Clan Kage's saboteurs, see "Clan Kage" in docs/rules.md), at the smart levels (Easy and up). A wave out
// against a rival with a ready saboteur in it that is not fighting, within SabotageLook columns of a rival building it
// sees that is worth wrecking (one training units, the town hall while its owner researches, a smelter, workshop or
// shrine, or one holding SabotageGoods or more) and that no rival soldier it sees stands by, sends that one saboteur to
// sabotage it, the wrecking that matters most first, then the nearest. The rest of the wave goes on; the saboteur is
// left to it (the wave's orders pass it by) until the work is done, it is called off, or SabotageGiveUpTicks pass. One
// saboteur out at a time.
public sealed partial class AiPlayer
{
    /// <summary>Furthest (columns) a saboteur of the wave may be from a rival building to be sent to sabotage it.</summary>
    public const int SabotageLook = 20;

    /// <summary>Goods (all kinds together) in a rival building that make it worth sabotaging, whatever it is.</summary>
    public const int SabotageGoods = 40;

    /// <summary>How long (ticks) a sabotage may take before the saboteur gives up and goes back to the wave.</summary>
    public const int SabotageGiveUpTicks = 30 * Simulation.TicksPerSecond;

    private int _saboteur, _sabotageTarget;
    private long _sabotageSent;

    /// <summary>The saboteur out wrecking a rival building for it now (by id), or 0.</summary>
    public int Saboteur => _saboteur;

    /// <summary>The rival building its saboteur is after (by id), or 0.</summary>
    public int SabotageTarget => _sabotageTarget;

    /// <summary>Saboteurs it has sent out (for the tests and the match runner's trace).</summary>
    public int SaboteursSent { get; private set; }

    /// <summary>Follows its saboteur, or sends one from the wave at a rival building worth wrecking.</summary>
    private void Sabotage(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        if (_saboteur != 0)
        {
            int s = u.IndexOf(_saboteur);
            bool over = s < 0 || u.Job[s] != UnitJob.Sabotage || sim.Tick - _sabotageSent > SabotageGiveUpTicks;
            if (!over) return;
            var target = sim.Buildings.Get(_sabotageTarget);
            Note(sim, target != null && sim.Economy.Stalled(target) ? "its saboteur wrecked the works" : "its saboteur gives up");
            if (s >= 0 && u.Job[s] == UnitJob.Sabotage) issue(new StopCommand(Player, new[] { _saboteur }));
            _saboteur = _sabotageTarget = 0;
            return;
        }
        if (_waveLair != 0 || _wave.Count == 0) return;
        var free = NotFighting(sim, _wave);
        free.RemoveAll(id => !sim.Economy.ReadyToSabotage(u.IndexOf(id)) || _raidParty.Contains(id));
        if (free.Count == 0) return;

        Building? best = null;
        int bestWorth = 0, bestId = 0;
        long bestD = long.MaxValue;
        foreach (var b in sim.Buildings.All)
        {
            if (!Rival(b.Owner) || !sim.Economy.Sabotageable(Player, b)) continue;
            int worth = SabotageWorth(sim, b);
            if (worth == 0 || worth < bestWorth) continue;
            var (cx, cz) = Middle(b);
            if (!sim.Vision.Sees(Player, cx, cz) || sim.Vision.HiddenFrom(Player, b)) continue;
            foreach (int id in free)
            {
                int i = u.IndexOf(id);
                long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz);
                if (d > (long)SabotageLook * SabotageLook) continue;
                if (worth == bestWorth && (d > bestD || d == bestD && id >= bestId)) continue;
                if (NearestEnemy(sim, cx, cz, sim.Content.Combat.Sabotage.Guard + b.Def.SizeX / 2 + 3, soldiersOnly: true) >= 0) break;
                (best, bestWorth, bestD, bestId) = (b, worth, d, id);
            }
        }
        if (best == null) return;
        _saboteur = bestId;
        _sabotageTarget = best.Id;
        _sabotageSent = sim.Tick;
        SaboteursSent++;
        issue(new SabotageCommand(Player, new[] { bestId }, best.Id));
        var (bx, bz) = Middle(best);
        Note(sim, $"sends a saboteur to wreck the {best.Def.Name} at {bx},{bz}");
    }

    /// <summary>
    /// How much wrecking a rival building is worth: 3 for one training units or the town hall while its owner researches,
    /// 2 for a smelter, workshop or shrine, 1 for one holding <see cref="SabotageGoods"/> or more, else 0 (not worth it).
    /// </summary>
    private static int SabotageWorth(Simulation sim, Building b)
    {
        if (b.Queue.Count > 0) return 3;
        if (b.Def.Kind == BuildingKind.TownHall && sim.Economy.Player(b.Owner).Researching >= 0 && sim.Economy.TownHall(b.Owner) == b) return 3;
        if (b.Def.Kind is BuildingKind.Smelter or BuildingKind.Workshop or BuildingKind.Shrine) return 2;
        int goods = 0;
        foreach (int n in b.Stock) goods += n;
        return goods >= SabotageGoods ? 1 : 0;
    }
}
