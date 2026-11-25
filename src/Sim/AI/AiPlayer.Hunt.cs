using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// Hunting parties (issue #78): the smart levels clear the lairs spread across the map (boar, stone giants,
/// bog horrors, crabs, wolves) for their rewards and prizes, but weigh each pack first. With no wave out and
/// soldiers to spare, a party sets out for the nearest lair it has seen within <see cref="HuntReach"/> that it
/// outweighs by <see cref="LairOddsPercent"/>: just enough soldiers, nearest the den first, and never so many
/// that fewer than the guard stay home. A lair too strong for what it can spare is left alone. A party that
/// is losing (what is left of it worth less than <see cref="BreakOffPercent"/> of what is left of the pack)
/// comes home, and that lair is left alone for <see cref="ShunTicks"/>.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The first hunting party sets out no earlier than this tick; after one ends the next may set out this many ticks later.</summary>
    public const int HuntTicks = 4 * 60 * Tps, HuntRetryTicks = 60 * Tps;
    /// <summary>A hunting party goes for lairs at most this far (columns) from the town hall, and comes home after this many ticks.</summary>
    public const int HuntReach = 200, HuntGiveUpTicks = 4 * 60 * Tps;
    /// <summary>A party (or the opening raid) is sent at a pack only worth at most 100/this of the soldiers sent (percent).</summary>
    public const int LairOddsPercent = 200;
    /// <summary>A party worth less than this percent of what is left of the pack breaks off and comes home.</summary>
    public const int BreakOffPercent = 100;
    /// <summary>A lair a party broke off from is left alone for this many ticks.</summary>
    public const int ShunTicks = 10 * 60 * Tps;
    /// <summary>The fewest soldiers in a hunting party.</summary>
    public const int LeastHunt = 2;

    /// <summary>The soldiers out hunting a lair, by id.</summary>
    public IReadOnlyList<int> HuntPartyIds => _huntParty;
    /// <summary>The lair the hunting party is after (0 if none is out).</summary>
    public int HuntLair => _huntParty.Count > 0 ? _huntLair : 0;
    /// <summary>Lairs it has cleared with a hunting party, and hunts it broke off.</summary>
    public int Hunts { get; private set; }
    public int HuntsBrokenOff { get; private set; }

    private readonly List<int> _huntParty = new();
    private int _huntLair;
    private long _huntSent, _huntAt = HuntTicks;
    // Lairs left alone after a party broke off, until when; in the order they were left.
    private readonly List<(int Lair, long Until)> _shunned = new();

    /// <summary>What the monsters left in the lair's pack are worth (see <see cref="Power"/>), as they are now.</summary>
    private static long PackWorth(Simulation sim, Lair lair) => PowerOf(sim, (List<int>)[.. lair.Pack]);

    /// <summary>True if soldiers worth <paramref name="force"/> are enough to send at the lair's pack (see <see cref="LairOddsPercent"/>).</summary>
    private static bool Outweighs(Simulation sim, long force, Lair lair) => force * 100 >= PackWorth(sim, lair) * LairOddsPercent;

    /// <summary>
    /// A lair a hunting party may go for: on open ground (not the cavern; the dusk tree's only while it wants the tree,
    /// see <see cref="WantsDuskTree"/>), with monsters still about, and not shunned.
    /// </summary>
    private bool Huntable(Simulation sim, Lair lair)
    {
        if (lair.Cleared || lair.Pack.Count == 0) return false;
        if (lair.Monster.Lair is not { Cavern: CavernSpot.None } def) return false;
        if (def.DuskTree && !WantsDuskTree(sim)) return false; // the warden only for a player that wants the tree
        foreach (var s in _shunned)
            if (s.Lair == lair.Id && sim.Tick < s.Until) return false;
        if (KeepsOff(sim, lair.X, lair.Z)) return false; // on the corrupted ground
        return !Smart || LairKnown(sim, lair);
    }

    /// <summary>
    /// The hunting party: it attack-moves on the pack (where it sees one of them, else the den), and ends when
    /// the lair is cleared, when it is wiped out or losing, or after <see cref="HuntGiveUpTicks"/>. With none out,
    /// no wave out and soldiers to spare at home, a new one sets out. The party is taken out of <paramref name="home"/>.
    /// </summary>
    private void HuntStep(Simulation sim, Building hall, List<int> home, int guardPercent, Action<Command> issue)
    {
        var u = sim.Units;
        _huntParty.RemoveAll(id => !u.Exists(id));
        _shunned.RemoveAll(s => sim.Tick >= s.Until);
        if (_huntParty.Count > 0)
        {
            home.RemoveAll(_huntParty.Contains);
            StepHunt(sim, hall, issue);
            return;
        }
        if (sim.Tick < _huntAt || _wave.Count > 0) return;

        var (hx, hz) = Middle(hall);
        var ready = new List<int>(home);
        ready.RemoveAll(_mending.Contains);
        ready.RemoveAll(id => Hurt(sim, u.IndexOf(id), HealedPercent));
        int guard = Math.Max(LeastGuard, home.Count * guardPercent / 100);
        int spare = ready.Count - guard;
        if (spare < LeastHunt) return;
        long force = 0;
        foreach (int id in ready) force += PowerOf(sim, [id]);

        // The nearest lair it can beat with what it can spare.
        Lair? best = null;
        long bestD = (long)HuntReach * HuntReach + 1;
        foreach (var lair in sim.Monsters.Lairs)
        {
            if (!Huntable(sim, lair)) continue;
            long d = Dist2(hx, hz, lair.X, lair.Z);
            if (d >= bestD) continue;
            // What it can spare: all but the guard, those nearest the den first.
            SortNearest(sim, ready, lair.X, lair.Z);
            if (!Outweighs(sim, PowerOf(sim, ready.GetRange(0, spare)), lair)) continue;
            (bestD, best) = (d, lair);
        }
        if (best == null) return;

        // Just enough of them: the nearest to the den until the party outweighs the pack.
        SortNearest(sim, ready, best.X, best.Z);
        long worth = 0, pack = PackWorth(sim, best);
        foreach (int id in ready)
        {
            if (_huntParty.Count >= spare) break;
            if (_huntParty.Count >= LeastHunt && worth * 100 >= pack * LairOddsPercent) break;
            _huntParty.Add(id);
            worth += PowerOf(sim, [id]);
        }
        home.RemoveAll(_huntParty.Contains);
        _huntLair = best.Id;
        _huntSent = sim.Tick;
        Note(sim, $"sends a party of {_huntParty.Count} (worth {worth / 1000}k) to clear the {best.Monster.Name} lair at {best.X},{best.Z} (worth {pack / 1000}k)");
        issue(new AttackMoveCommand(Player, _huntParty.ToArray(), best.X, best.Z));
    }

    /// <summary>The party out: see <see cref="HuntStep"/>.</summary>
    private void StepHunt(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        var lair = sim.Monsters.GetLair(_huntLair);
        if (lair == null || lair.Cleared)
        {
            if (lair != null && lair.ClearedBy == Player) Hunts++;
            Note(sim, $"its hunting party is done: the lair is {(lair?.ClearedBy == Player ? "ours" : "cleared")}");
            EndHunt(sim, hall, issue, home: false);
            return;
        }
        long worth = PowerOf(sim, _huntParty), pack = PackWorth(sim, lair);
        if (worth * 100 < pack * BreakOffPercent)
        {
            HuntsBrokenOff++;
            _shunned.Add((lair.Id, sim.Tick + ShunTicks));
            Note(sim, $"its hunting party breaks off from the {lair.Monster.Name} lair: {worth / 1000}k left against {pack / 1000}k");
            EndHunt(sim, hall, issue, home: true);
            return;
        }
        if (sim.Tick - _huntSent > HuntGiveUpTicks)
        {
            Note(sim, "its hunting party gives up and comes home");
            EndHunt(sim, hall, issue, home: true);
            return;
        }
        if (sim.Tick % (5 * ArmyTicks) >= ArmyTicks) return;
        // After the pack where it sees one of them (a pack roams), else to the den.
        var (gx, gz) = (lair.X, lair.Z);
        foreach (int id in lair.Pack)
        {
            int i = u.IndexOf(id);
            if (i < 0 || !sim.Vision.SeesUnit(Player, i)) continue;
            (gx, gz) = (u.X[i].FloorToInt(), u.Z[i].FloorToInt());
            break;
        }
        var idle = Unordered(sim, _huntParty, gx, gz);
        if (idle.Count > 0) issue(new AttackMoveCommand(Player, idle.ToArray(), gx, gz));
    }

    /// <summary>The hunt is over: the hurt go home to heal, and all of them home if <paramref name="home"/>.</summary>
    private void EndHunt(Simulation sim, Building hall, Action<Command> issue, bool home)
    {
        var u = sim.Units;
        foreach (int id in _huntParty)
            if (Hurt(sim, u.IndexOf(id), HealedPercent) && !_mending.Contains(id)) _mending.Add(id);
        if (home && _huntParty.Count > 0)
        {
            var (rx, rz) = RallyAt(sim, hall, null);
            issue(new MoveCommand(Player, _huntParty.ToArray(), rx, rz));
        }
        _huntParty.Clear();
        _huntLair = 0;
        _huntAt = sim.Tick + HuntRetryTicks;
    }

    /// <summary>Sorts the units by id nearest (x, z) first (ties by id).</summary>
    private static void SortNearest(Simulation sim, List<int> ids, int x, int z)
    {
        var u = sim.Units;
        ids.Sort((a, b) =>
        {
            int ia = u.IndexOf(a), ib = u.IndexOf(b);
            int c = Dist2(u.X[ia].FloorToInt(), u.Z[ia].FloorToInt(), x, z).CompareTo(Dist2(u.X[ib].FloorToInt(), u.Z[ib].FloorToInt(), x, z));
            return c != 0 ? c : a.CompareTo(b);
        });
    }
}
