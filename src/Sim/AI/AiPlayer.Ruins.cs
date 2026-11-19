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
/// sets out once the match is <see cref="RuinTicks"/> old, not while its town is under attack, with soldiers to spare beyond the guard (a wave may be out),
/// and only for a ruin with no lair it knows of close by and no hostile force it sees there that the party does
/// not outweigh. The party searches together (it gives the search order again to any of its own that a fight
/// took off it), and ends when the ruin is searched (by it or anyone), after <see cref="RuinGiveUpTicks"/>, or
/// when what it sees at the ruin outweighs it: then it comes home and leaves that ruin alone for
/// <see cref="ShunTicks"/>. With no ruin seen to go to, the pair goes looking for one, as a player would: it walks
/// to the unseen ground within reach nearest it (on a <see cref="RuinLookGrid"/> grid, sweeping round the town
/// hall in rings), on and on, for at most <see cref="RuinLookTicks"/>, and goes to search the first ruin it sees. The classic level leaves ruins be.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The first ruin party sets out no earlier than this tick; after one ends the next may set out this many ticks later.</summary>
    public const int RuinTicks = 6 * 60 * Tps, RuinRetryTicks = 20 * Tps;
    /// <summary>A ruin party goes to ruins at most this far (columns) from the town hall, and comes home after this many ticks.</summary>
    public const int RuinReach = 240, RuinGiveUpTicks = 3 * 60 * Tps;
    /// <summary>Soldiers in a ruin party.</summary>
    public const int RuinParty = 2;
    /// <summary>Columns round a ruin in which it counts the hostile units it sees (and keeps off known lairs).</summary>
    public const int RuinDangerRadius = 20;
    /// <summary>A pair looking for ruins comes home after this many ticks; it looks no nearer the town hall than <see cref="RuinLookFrom"/> columns.</summary>
    public const int RuinLookTicks = 3 * 60 * Tps, RuinLookFrom = 128;
    /// <summary>Columns between the places a pair looking for ruins walks to.</summary>
    public const int RuinLookGrid = 24;
    /// <summary><see cref="RuinTarget"/> while the pair is out looking for a ruin.</summary>
    public const int LookingForRuins = -2;

    /// <summary>The soldiers out searching a ruin, by id.</summary>
    public IReadOnlyList<int> RuinPartyIds => _ruinParty;
    /// <summary>The ruin (index in <see cref="RuinSystem.Ruins"/>) the ruin party is searching, <see cref="LookingForRuins"/>, or -1.</summary>
    public int RuinTarget => _ruinParty.Count > 0 ? _ruinTarget : -1;
    /// <summary>The ruins it found by looking for them.</summary>
    public int RuinsSpotted { get; private set; }
    /// <summary>Ruins it has searched, and ruin parties that broke off.</summary>
    public int RuinsSearched { get; private set; }
    public int RuinsBrokenOff { get; private set; }

    private readonly List<int> _ruinParty = new();
    private int _ruinTarget = -1;
    private long _ruinSent, _ruinAt = RuinTicks;
    // Ruins left alone after a party broke off, until when; in the order they were left.
    private readonly List<(int Ruin, long Until)> _shunnedRuins = new();
    // Where the pair looking for ruins is walking, and the places it went to look at.
    private (int X, int Z) _lookGoal;
    private readonly List<(int X, int Z)> _lookedAt = new();

    /// <summary>
    /// The ruin party: with one out, it keeps it searching (see <see cref="StepRuin"/>) or looking (see
    /// <see cref="StepLook"/>); with none, it may send one. The party is taken out of <paramref name="home"/>.
    /// </summary>
    private void RuinStep(Simulation sim, Building hall, List<int> home, int guardPercent, Action<Command> issue)
    {
        var u = sim.Units;
        _ruinParty.RemoveAll(id => !u.Exists(id));
        _shunnedRuins.RemoveAll(s => sim.Tick >= s.Until);
        if (_ruinTarget == LookingForRuins)
        {
            home.RemoveAll(_ruinParty.Contains);
            StepLook(sim, hall, issue);
            return;
        }
        if (_ruinTarget >= 0)
        {
            home.RemoveAll(_ruinParty.Contains);
            StepRuin(sim, hall, issue);
            return;
        }
        if (sim.Tick < _ruinAt || sim.Ruins.Ruins.Count == 0) return;

        var (hx, hz) = Middle(hall);
        // Not while its town is under attack, or (Hard) was lately.
        if (NearestEnemy(sim, hx, hz, 2 * DefendRadius, soldiersOnly: true) >= 0 || (Hard && sim.Tick - _alarm <= AlarmTicks)) return;
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
        if (best == null)
        {
            // None seen: the pair nearest to unseen ground goes looking.
            if (LookGoal(sim, hx, hz, hx, hz) is not { } goal) return;
            SortNearest(sim, ready, goal.X, goal.Z);
            _ruinParty.AddRange(ready.GetRange(0, RuinParty));
            home.RemoveAll(_ruinParty.Contains);
            _ruinTarget = LookingForRuins;
            _ruinSent = sim.Tick;
            _lookGoal = goal;
            _lookedAt.Add(goal);
            Note(sim, $"sends {_ruinParty.Count} to look for old ruins, first at {goal.X},{goal.Z}");
            issue(new MoveCommand(Player, _ruinParty.ToArray(), goal.X, goal.Z));
            return;
        }

        SortNearest(sim, ready, best.X, best.Z);
        _ruinParty.AddRange(ready.GetRange(0, RuinParty));
        home.RemoveAll(_ruinParty.Contains);
        _ruinTarget = best.Index;
        _ruinSent = sim.Tick;
        Note(sim, $"sends {_ruinParty.Count} to search the old ruin at {best.X},{best.Z}");
        issue(new SearchRuinCommand(Player, _ruinParty.ToArray(), best.Index));
    }

    /// <summary>
    /// Where a pair at (x, z) looking for ruins goes next: the point of a <see cref="RuinLookGrid"/> grid it has
    /// not seen nor gone to look at, between <see cref="RuinLookFrom"/> and <see cref="RuinReach"/> columns of the
    /// town hall at (hx, hz), nearest the pair and as far from the town hall as the pair (so it sweeps round
    /// the town hall in rings, and outward once a ring is seen). Null if there is none.
    /// </summary>
    private (int X, int Z)? LookGoal(Simulation sim, int hx, int hz, int x, int z)
    {
        var w = sim.World;
        (int X, int Z)? best = null;
        long bestD = long.MaxValue;
        long ring = Math.Max(RuinLookFrom, IntMath.ISqrt(Dist2(hx, hz, x, z)));
        long near = (long)RuinLookFrom * RuinLookFrom, far = (long)RuinReach * RuinReach;
        for (int gz = RuinLookGrid / 2; gz < w.SizeZ; gz += RuinLookGrid)
        for (int gx = RuinLookGrid / 2; gx < w.SizeX; gx += RuinLookGrid)
        {
            long fromHall = Dist2(hx, hz, gx, gz);
            if (fromHall < near || fromHall > far) continue;
            if (sim.Vision.At(Player, gx, gz) != Sight.Unseen || _lookedAt.Contains((gx, gz))) continue;
            long off = IntMath.ISqrt(fromHall) - ring;
            long d = Dist2(x, z, gx, gz) + 4 * off * off;
            if (d < bestD) (bestD, best) = (d, (gx, gz));
        }
        return best;
    }

    /// <summary>
    /// The pair out looking: it goes to search the first ruin it may that it has seen; else, once at its goal
    /// (or near enough to see it), on to the next; and comes home after <see cref="RuinLookTicks"/>, with
    /// nowhere left to look, or when what it sees around it outweighs it.
    /// </summary>
    private void StepLook(Simulation sim, Building hall, Action<Command> issue)
    {
        var u = sim.Units;
        if (_ruinParty.Count == 0)
        {
            EndRuin(sim, hall, issue, home: false);
            return;
        }
        var (cx, cz) = Centroid(sim, _ruinParty);
        long force = PowerOf(sim, _ruinParty);
        AncientRuin? best = null;
        long bestD = long.MaxValue;
        var (hx, hz) = Middle(hall);
        foreach (var ruin in sim.Ruins.Ruins)
        {
            if (Dist2(hx, hz, ruin.X, ruin.Z) > (long)RuinReach * RuinReach || !Searchable(sim, ruin) || !SafeAt(sim, ruin, force)) continue;
            long d = Dist2(cx, cz, ruin.X, ruin.Z);
            if (d < bestD) (bestD, best) = (d, ruin);
        }
        if (best != null)
        {
            RuinsSpotted++;
            _ruinTarget = best.Index;
            _ruinSent = sim.Tick;
            Note(sim, $"its pair spots an old ruin at {best.X},{best.Z} and goes to search it");
            issue(new SearchRuinCommand(Player, _ruinParty.ToArray(), best.Index));
            return;
        }
        if (sim.Tick - _ruinSent > RuinLookTicks || HostileWorthNear(sim, cx, cz, RuinDangerRadius) >= force)
        {
            Note(sim, "its pair looking for ruins comes home");
            EndRuin(sim, hall, issue, home: true);
            return;
        }
        if (sim.Tick % (5 * ArmyTicks) >= ArmyTicks) return;
        bool there = sim.Vision.At(Player, _lookGoal.X, _lookGoal.Z) != Sight.Unseen
            || Dist2(cx, cz, _lookGoal.X, _lookGoal.Z) <= (long)Slack * Slack;
        if (there)
        {
            if (LookGoal(sim, hx, hz, cx, cz) is not { } next)
            {
                Note(sim, "its pair has nowhere left to look for ruins");
                EndRuin(sim, hall, issue, home: true);
                return;
            }
            _lookGoal = next;
            _lookedAt.Add(next);
        }
        var go = Unordered(sim, _ruinParty, _lookGoal.X, _lookGoal.Z);
        if (go.Count > 0) issue(new MoveCommand(Player, go.ToArray(), _lookGoal.X, _lookGoal.Z));
    }

    /// <summary>
    /// A ruin a party may go to: not searched as far as it knows (by it, or by another where it sees the
    /// ruin now), seen, not shunned, and with no lair it knows of close by.
    /// </summary>
    private bool Searchable(Simulation sim, AncientRuin ruin)
    {
        if (ruin.Searched && (ruin.SearchedBy == Player || sim.Vision.At(Player, ruin.X, ruin.Z) == Sight.Visible)) return false;
        if (!RuinSeen(sim, ruin)) return false;
        foreach (var s in _shunnedRuins)
            if (s.Ruin == ruin.Index && sim.Tick < s.Until) return false;
        foreach (var lair in sim.Monsters.Lairs)
            if (!lair.Cleared && Dist2(ruin.X, ruin.Z, lair.X, lair.Z) <= (long)RuinDangerRadius * RuinDangerRadius && LairKnown(sim, lair))
                return false;
        return true;
    }

    /// <summary>True if it has seen the ruin: its middle, or any corner of its floor.</summary>
    private bool RuinSeen(Simulation sim, AncientRuin ruin)
    {
        const int r = RuinSystem.Reach;
        return sim.Vision.At(Player, ruin.X, ruin.Z) != Sight.Unseen
            || sim.Vision.At(Player, ruin.X - r, ruin.Z - r) != Sight.Unseen || sim.Vision.At(Player, ruin.X + r, ruin.Z - r) != Sight.Unseen
            || sim.Vision.At(Player, ruin.X - r, ruin.Z + r) != Sight.Unseen || sim.Vision.At(Player, ruin.X + r, ruin.Z + r) != Sight.Unseen;
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
