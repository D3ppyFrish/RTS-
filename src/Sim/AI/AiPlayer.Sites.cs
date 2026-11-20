using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// The ancient sites, with dominion on (see <see cref="DominionSystem"/>): the smart levels (Easy and up) hold some
/// and answer a rival who holds most. From <see cref="SiteTicks"/> into the match, with soldiers to spare beyond the
/// guard, it sends a garrison of <see cref="SiteParty"/> soldiers to the nearest site it has seen within
/// <see cref="RuinReach"/> of its town hall that it does not hold, with no lair it knows of close by and no hostile
/// force it sees there that the garrison does not outweigh; it keeps up to <see cref="Garrisons"/> of them (one at
/// Easy). A garrison stands inside its site (it is sent back in whenever a fight or anything else took it out) and
/// comes home when what it sees round the site outweighs it, leaving that site alone for <see cref="ShunTicks"/>.
/// When a rival's dominion clock is running, every soldier it can spare beyond the guard goes to retake the site of
/// the rival's nearest its town hall, at once and whatever it sees there. The classic level leaves sites be.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The first garrison sets out no earlier than this tick.</summary>
    public const int SiteTicks = 8 * 60 * Tps;
    /// <summary>Soldiers in a garrison.</summary>
    public const int SiteParty = 4;

    private sealed class Garrison
    {
        public int Ruin;
        public readonly List<int> Ids = new();
        public bool Retake;
    }

    // Garrisons out, in the order they were sent.
    private readonly List<Garrison> _garrisons = new();
    private readonly List<(int Ruin, long Until)> _shunnedSites = new();

    /// <summary>Most garrisons it keeps at the sites (Normal and up two, Easy one).</summary>
    public int Garrisons => Skilled ? 2 : 1;

    /// <summary>The sites (indexes in <see cref="RuinSystem.Ruins"/>) its garrisons are holding or going to.</summary>
    public IEnumerable<int> GarrisonSites
    {
        get
        {
            foreach (var g in _garrisons) yield return g.Ruin;
        }
    }

    /// <summary>Soldiers in its garrisons, by id.</summary>
    public int GarrisonSoldiers
    {
        get
        {
            int n = 0;
            foreach (var g in _garrisons) n += g.Ids.Count;
            return n;
        }
    }

    /// <summary>The garrisons: see the class notes. Soldiers in them are taken out of <paramref name="home"/>.</summary>
    private void SiteStep(Simulation sim, Building hall, List<int> home, int guardPercent, Action<Command> issue)
    {
        if (!sim.Sites.Active) return;
        var u = sim.Units;
        _shunnedSites.RemoveAll(s => sim.Tick >= s.Until);
        for (int k = 0; k < _garrisons.Count; k++)
        {
            var g = _garrisons[k];
            g.Ids.RemoveAll(id => !u.Exists(id));
            var ruin = sim.Ruins.Ruins[g.Ruin];
            bool done = g.Ids.Count == 0 || (g.Retake && ruin.Holder == Player)
                || (!g.Retake && !SafeAt(sim, ruin, PowerOf(sim, g.Ids)));
            if (!done) continue;
            if (g.Ids.Count > 0 && !g.Retake)
            {
                _shunnedSites.Add((g.Ruin, sim.Tick + ShunTicks));
                Note(sim, $"its garrison breaks off from the ancient site at {ruin.X},{ruin.Z}");
            }
            // A retaking force that won the site back stays on as its garrison, as many as one holds.
            if (g.Retake && ruin.Holder == Player && g.Ids.Count > 0)
            {
                g.Retake = false;
                if (g.Ids.Count > SiteParty) g.Ids.RemoveRange(SiteParty, g.Ids.Count - SiteParty);
                Note(sim, $"retook the ancient site at {ruin.X},{ruin.Z}");
                continue;
            }
            if (g.Ids.Count > 0)
            {
                var (rx, rz) = RallyAt(sim, hall, null);
                issue(new MoveCommand(Player, g.Ids.ToArray(), rx, rz));
            }
            _garrisons.RemoveAt(k--);
        }
        foreach (var g in _garrisons) home.RemoveAll(g.Ids.Contains);

        var (hx, hz) = Middle(hall);
        bool attacked = NearestEnemy(sim, hx, hz, 2 * DefendRadius, soldiersOnly: true) >= 0;
        var ready = new List<int>(home);
        ready.RemoveAll(_mending.Contains);
        ready.RemoveAll(id => id == _scout || id == _explorer);
        ready.RemoveAll(id => Hurt(sim, u.IndexOf(id), HealedPercent) || !sim.Sites.CanHold(u.IndexOf(id)));
        int guard = Math.Max(LeastGuard, home.Count * guardPercent / 100);
        int leader = sim.Sites.Leader;

        if (!attacked && leader >= 0 && leader != Player && ready.Count - guard > 0 && !_garrisons.Exists(g => g.Retake))
        {
            // A rival is on the way to winning: everyone it can spare goes to take back the site of theirs nearest home.
            AncientRuin? target = null;
            long bestD = long.MaxValue;
            foreach (var ruin in sim.Ruins.Ruins)
            {
                if (ruin.Holder != leader) continue;
                long d = Dist2(hx, hz, ruin.X, ruin.Z);
                if (d < bestD) (bestD, target) = (d, ruin);
            }
            if (target != null)
            {
                var g = new Garrison { Ruin = target.Index, Retake = true };
                SortNearest(sim, ready, target.X, target.Z);
                g.Ids.AddRange(ready.GetRange(0, ready.Count - guard));
                _garrisons.Add(g);
                home.RemoveAll(g.Ids.Contains);
                Note(sim, $"sends {g.Ids.Count} to take back the ancient site at {target.X},{target.Z}: player {leader} holds most of them");
                issue(new AttackMoveCommand(Player, g.Ids.ToArray(), target.X, target.Z));
                return;
            }
        }

        if (!attacked && sim.Tick >= SiteTicks && _garrisons.Count < Garrisons && ready.Count - guard >= SiteParty)
        {
            AncientRuin? best = null;
            long bestD = (long)RuinReach * RuinReach + 1;
            foreach (var ruin in sim.Ruins.Ruins)
            {
                if (ruin.Holder == Player || _garrisons.Exists(g => g.Ruin == ruin.Index) || !SiteOpen(sim, ruin)) continue;
                long d = Dist2(hx, hz, ruin.X, ruin.Z);
                if (d >= bestD) continue;
                SortNearest(sim, ready, ruin.X, ruin.Z);
                if (!SafeAt(sim, ruin, PowerOf(sim, ready.GetRange(0, SiteParty)))) continue;
                (bestD, best) = (d, ruin);
            }
            if (best != null)
            {
                var g = new Garrison { Ruin = best.Index };
                SortNearest(sim, ready, best.X, best.Z);
                g.Ids.AddRange(ready.GetRange(0, SiteParty));
                _garrisons.Add(g);
                home.RemoveAll(g.Ids.Contains);
                Note(sim, $"sends a garrison of {g.Ids.Count} to hold the ancient site at {best.X},{best.Z}");
                issue(new MoveCommand(Player, g.Ids.ToArray(), best.X, best.Z));
            }
        }

        // Garrisons stand inside their sites: any of their own a fight (or anything else) took out are sent back in.
        if (sim.Tick % (5 * ArmyTicks) >= ArmyTicks) return;
        foreach (var g in _garrisons)
        {
            var ruin = sim.Ruins.Ruins[g.Ruin];
            _scratch.Clear();
            foreach (int id in Unordered(sim, g.Ids, ruin.X, ruin.Z))
                if (!sim.Ruins.InReach(ruin, u.IndexOf(id))) _scratch.Add(id);
            if (_scratch.Count == 0) continue;
            if (g.Retake) issue(new AttackMoveCommand(Player, _scratch.ToArray(), ruin.X, ruin.Z));
            else issue(new MoveCommand(Player, _scratch.ToArray(), ruin.X, ruin.Z));
        }
    }

    /// <summary>A site a garrison may go to: seen, not shunned, and with no lair it knows of close by.</summary>
    private bool SiteOpen(Simulation sim, AncientRuin ruin)
    {
        if (!RuinSeen(sim, ruin)) return false;
        foreach (var s in _shunnedSites)
            if (s.Ruin == ruin.Index && sim.Tick < s.Until) return false;
        foreach (var lair in sim.Monsters.Lairs)
            if (!lair.Cleared && Dist2(ruin.X, ruin.Z, lair.X, lair.Z) <= (long)RuinDangerRadius * RuinDangerRadius && LairKnown(sim, lair))
                return false;
        return true;
    }
}
