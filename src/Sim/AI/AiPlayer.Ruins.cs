using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.AI;

/// <summary>
/// Searching the old ruins (follow-up to the ruins' finds, PR #87): the smart levels (Easy and up) send a small
/// party of <see cref="RuinParty"/> soldiers to search the nearest old ruin they have seen within
/// <see cref="RuinReach"/> of their town hall, for the goods (and maybe the Sunstone circlet) left in it. A party
/// sets out once the match is <see cref="RuinTicks"/> old, with no wave out and soldiers to spare beyond the guard,
/// and only for a ruin with no lair it knows of close by and no hostile force it sees there that the party does
/// not outweigh. The party searches together (it gives the search order again to any of its own that a fight
/// took off it), and ends when the ruin is searched (by it or anyone), after <see cref="RuinGiveUpTicks"/>, or
/// when what it sees at the ruin outweighs it: then it comes home and leaves that ruin alone for
/// <see cref="ShunTicks"/>. The classic level leaves ruins be.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The first ruin party sets out no earlier than this tick; after one ends the next may set out this many ticks later.</summary>
    public const int RuinTicks = 2 * 60 * Tps, RuinRetryTicks = 20 * Tps;
    /// <summary>A ruin party goes to ruins at most this far (columns) from the town hall, and comes home after this many ticks.</summary>
    public const int RuinReach = 220, RuinGiveUpTicks = 3 * 60 * Tps;
    /// <summary>Soldiers in a ruin party.</summary>
    public const int RuinParty = 2;
    /// <summary>Columns round a ruin in which it counts the hostile units it sees (and keeps off known lairs).</summary>
    public const int RuinDangerRadius = 20;

    /// <summary>The soldiers out searching a ruin, by id.</summary>
    public IReadOnlyList<int> RuinPartyIds => _ruinParty;
    /// <summary>The ruin (index in <see cref="RuinSystem.Ruins"/>) the ruin party is searching, or -1.</summary>
    public int RuinTarget => _ruinParty.Count > 0 ? _ruinTarget : -1;
    /// <summary>Ruins it has searched, and ruin parties that broke off.</summary>
    public int RuinsSearched { get; private set; }
    public int RuinsBrokenOff { get; private set; }

    private readonly List<int> _ruinParty = new();
    private int _ruinTarget = -1;
    private long _ruinSent, _ruinAt = RuinTicks;
    // Ruins left alone after a party broke off, until when; in the order they were left.
    private readonly List<(int Ruin, long Until)> _shunnedRuins = new();

    /// <summary>
    /// The ruin party: with one out, it keeps it searching (see <see cref="StepRuin"/>); with none, and none
    /// of its waves out, it may send one. The party is taken out of <paramref name="home"/>.
    /// </summary>
    private void RuinStep(Simulation sim, Building hall, List<int> home, int guardPercent, Action<Command> issue)
    {
        var u = sim.Units;
        _ruinParty.RemoveAll(id => !u.Exists(id));
        _shunnedRuins.RemoveAll(s => sim.Tick >= s.Until);
        if (_ruinTarget >= 0)
        {
            home.RemoveAll(_ruinParty.Contains);
            StepRuin(sim, hall, issue);
            return;
        }
        if (sim.Tick < _ruinAt || _wave.Count > 0 || sim.Ruins.Ruins.Count == 0) return;

        var (hx, hz) = Middle(hall);
        var ready = new List<int>(home);
        ready.RemoveAll(_mending.Contains);
        ready.RemoveAll(id => id == _scout || id == _explorer);
        ready.RemoveAll(id => Hurt(sim, u.IndexOf(id), HealedPercent) || !sim.Ruins.CanSearch(u.IndexOf(id)));
        int guard = Math.Max(LeastGuard, home.Count * guardPercent / 100);
        if (ready.Count - guard < RuinParty) return;

        // The nearest ruin it has seen, still to search, that the party can go to.
        AncientRuin? best = null;
        long bestD = (long)RuinReach * RuinReach + 1;
        foreach (var ruin in sim.Ruins.Ruins)
        {
            if (!Searchable(sim, ruin)) continue;
            long d = Dist2(hx, hz, ruin.X, ruin.Z);
            if (d >= bestD) continue;
            SortNearest(sim, ready, ruin.X, ruin.Z);
            if (!SafeAt(sim, ruin, PowerOf(sim, ready.GetRange(0, RuinParty)))) continue;
            (bestD, best) = (d, ruin);
        }
        if (best == null) return;

        SortNearest(sim, ready, best.X, best.Z);
        _ruinParty.AddRange(ready.GetRange(0, RuinParty));
        home.RemoveAll(_ruinParty.Contains);
        _ruinTarget = best.Index;
        _ruinSent = sim.Tick;
        Note(sim, $"sends {_ruinParty.Count} to search the old ruin at {best.X},{best.Z}");
        issue(new SearchRuinCommand(Player, _ruinParty.ToArray(), best.Index));
    }

    /// <summary>
    /// A ruin a party may go to: not searched as far as it knows (by it, or by another where it sees the
    /// ruin now), seen, not shunned, and with no lair it knows of close by.
    /// </summary>
    private bool Searchable(Simulation sim, AncientRuin ruin)
    {
        if (ruin.Searched && (ruin.SearchedBy == Player || sim.Vision.At(Player, ruin.X, ruin.Z) == Sight.Visible)) return false;
        if (sim.Vision.At(Player, ruin.X, ruin.Z) == Sight.Unseen) return false;
        foreach (var s in _shunnedRuins)
            if (s.Ruin == ruin.Index && sim.Tick < s.Until) return false;
        foreach (var lair in sim.Monsters.Lairs)
            if (!lair.Cleared && Dist2(ruin.X, ruin.Z, lair.X, lair.Z) <= (long)RuinDangerRadius * RuinDangerRadius && LairKnown(sim, lair))
                return false;
        return true;
    }

    /// <summary>True if soldiers worth <paramref name="force"/> outweigh the hostile fighters it sees round the ruin.</summary>
    private bool SafeAt(Simulation sim, AncientRuin ruin, long force) => force > HostileWorthNear(sim, ruin.X, ruin.Z, RuinDangerRadius);

    /// <summary>What the fighting units of anyone else (wild monsters too) it sees within the radius of (x, z) are worth.</summary>
    private long HostileWorthNear(Simulation sim, int x, int z, int radius)
    {
        var u = sim.Units;
        long sum = 0, r2 = (long)radius * radius;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] == Player || u.Subdued[i] || u.Hp[i] <= 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (!def.CanAttack || def.Role is UnitRole.Worker or UnitRole.Trader) continue;
            if (Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), x, z) > r2 || !sim.Vision.SeesUnit(Player, i)) continue;
            sum += Power(def, u.Hp[i]);
        }
        return sum;
    }

    /// <summary>The party out: see <see cref="RuinStep"/>.</summary>
    private void StepRuin(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        var ruin = sim.Ruins.Ruins[_ruinTarget];
        if (ruin.Searched)
        {
            if (ruin.SearchedBy == Player) RuinsSearched++;
            Note(sim, $"its ruin party is done: the ruin at {ruin.X},{ruin.Z} is {(ruin.SearchedBy == Player ? "searched" : "searched by another")}");
            EndRuin(sim, hall, issue, home: true);
            return;
        }
        if (_ruinParty.Count == 0 || !SafeAt(sim, ruin, PowerOf(sim, _ruinParty)))
        {
            RuinsBrokenOff++;
            _shunnedRuins.Add((ruin.Index, sim.Tick + ShunTicks));
            Note(sim, $"its ruin party breaks off from the ruin at {ruin.X},{ruin.Z}");
            EndRuin(sim, hall, issue, home: true);
            return;
        }
        if (sim.Tick - _ruinSent > RuinGiveUpTicks)
        {
            Note(sim, "its ruin party gives up and comes home");
            EndRuin(sim, hall, issue, home: true);
            return;
        }
        if (sim.Tick % (5 * ArmyTicks) >= ArmyTicks) return;
        // Any of its own a fight (or anything else) took off the search are sent to search again.
        _scratch.Clear();
        foreach (int id in NotFighting(sim, _ruinParty))
        {
            int i = u.IndexOf(id);
            if (sim.Ruins.Searching(id) != ruin.Index && sim.Ruins.CanSearch(i)) _scratch.Add(id);
        }
        if (_scratch.Count > 0) issue(new SearchRuinCommand(Player, _scratch.ToArray(), ruin.Index));
    }

    /// <summary>The search is over: the hurt go home to heal, and all of them home if <paramref name="home"/>.</summary>
    private void EndRuin(Simulation sim, Building hall, Action<Command> issue, bool home)
    {
        var u = sim.Units;
        foreach (int id in _ruinParty)
            if (Hurt(sim, u.IndexOf(id), HealedPercent) && !_mending.Contains(id)) _mending.Add(id);
        if (home && _ruinParty.Count > 0)
        {
            var (rx, rz) = RallyAt(sim, hall, null);
            issue(new MoveCommand(Player, _ruinParty.ToArray(), rx, rz));
        }
        _ruinParty.Clear();
        _ruinTarget = -1;
        _ruinAt = sim.Tick + RuinRetryTicks;
    }
}
