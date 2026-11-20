using System;
using Rts.Sim.Units;

namespace Rts.Sim.World;

/// <summary>
/// Dominion, the design doc's second way to win: hold most of the continent's ancient sites at once for a set
/// time. The ancient sites are the map's old ruins (<see cref="RuinSystem.Ruins"/>), with <see cref="Simulation.Dominion"/>
/// on (set before the first tick).
///
/// <para><b>Taking a site.</b> A player's fighting units (not workers; not beaten, running away or tamed, see
/// <see cref="CanHold"/>) standing inside a ruin's walls (<see cref="RuinSystem.InReach"/>) with no rival's
/// fighting units there claim it: after <see cref="ClaimTicks"/> of that it is theirs, whoever held it before.
/// While two players' fighting units are inside, the claim stands still; once no claimant is inside, or another
/// player's units have it to themselves, the claim starts again from nothing. A site stays held with nobody in it,
/// until a rival takes it. A player who is out of the match holds nothing.</para>
///
/// <para><b>Winning.</b> A player holding more than half of the sites (and at least <see cref="MinSites"/> sites
/// on the map) starts the dominion clock; at <see cref="DominionTicks"/> they win and every other player still in
/// goes out (see <see cref="MatchSystem"/>). Dropping to half or fewer stops the clock and sets it back to nothing.
/// All numbers are starting guesses to be tuned in playtests.</para>
/// </summary>
public sealed class DominionSystem
{
    /// <summary>Ticks a player's fighting units stand alone in a site to take it: 15 seconds.</summary>
    public const int ClaimTicks = 15 * Simulation.TicksPerSecond;
    /// <summary>Ticks a player holds more than half the sites to win: 3 minutes.</summary>
    public const int DominionTicks = 3 * 60 * Simulation.TicksPerSecond;
    /// <summary>Fewest sites on the map for dominion to count: with fewer, holding most of them is too easy.</summary>
    public const int MinSites = 3;

    private readonly Simulation _sim;

    public DominionSystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>True when dominion is played: <see cref="Simulation.Dominion"/> on and at least <see cref="MinSites"/> sites.</summary>
    public bool Active => _sim.Dominion && _sim.Ruins.Ruins.Count >= MinSites;

    /// <summary>Sites a player must hold to start the dominion clock: more than half.</summary>
    public int Needed => _sim.Ruins.Ruins.Count / 2 + 1;

    /// <summary>The player whose dominion clock is running, or -1.</summary>
    public int Leader { get; private set; } = -1;

    /// <summary>Ticks the leader has held more than half the sites (0 with no leader).</summary>
    public int Clock { get; private set; }

    /// <summary>Ticks left before the leader wins, or -1 with no leader.</summary>
    public int TicksLeft => Leader >= 0 ? DominionTicks - Clock : -1;

    /// <summary>How many sites the player holds.</summary>
    public int Held(int player)
    {
        int n = 0;
        foreach (var r in _sim.Ruins.Ruins)
            if (r.Holder == player) n++;
        return n;
    }

    /// <summary>
    /// True if unit i counts in taking and holding a site: a player's fighting unit (it deals damage and is not a
    /// worker), not beaten, running away, tamed or a city-state's.
    /// </summary>
    public bool CanHold(int i)
    {
        var u = _sim.Units;
        if (!_sim.Ruins.CanSearch(i)) return false;
        var def = _sim.Content.Units[u.Def[i]];
        return def.Damage > 0 && def.Role != UnitRole.Worker;
    }

    public ulong ComputeHash()
    {
        if (!_sim.Dominion) return 0;
        ulong h = 0x84222325CBF29CE4UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 0x100000001B3UL; }
        foreach (var r in _sim.Ruins.Ruins)
        {
            Mix(r.Holder); Mix(r.Claimant); Mix(r.Claim); Mix(r.HeldSince);
        }
        Mix(Leader); Mix(Clock);
        return h;
    }

    /// <summary>
    /// Before the match is checked: each site's claim goes on, stands still or starts again (see the class
    /// notes), then the dominion clock runs or stops; at <see cref="DominionTicks"/> the leader wins.
    /// </summary>
    internal void Step()
    {
        if (!Active || _sim.Match.Over || _sim.Match.Players.Count < 2) return;
        var ruins = _sim.Ruins.Ruins;
        var u = _sim.Units;
        // Who has fighting units inside each site: one player's (their number), several (-2) or nobody (-1).
        Span<int> inside = stackalloc int[ruins.Count];
        inside.Fill(-1);
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Hp[i] <= 0) continue;
            var ruin = _sim.Ruins.At(u.X[i].FloorToInt(), u.Z[i].FloorToInt());
            if (ruin == null || !CanHold(i) || !_sim.Ruins.InReach(ruin, i) || !_sim.Match.InPlay(u.Owner[i])) continue;
            int p = u.Owner[i], k = ruin.Index;
            if (inside[k] == -1) inside[k] = p;
            else if (inside[k] != p) inside[k] = -2;
        }
        foreach (var r in ruins)
        {
            if (r.Holder >= 0 && !_sim.Match.InPlay(r.Holder))
            {
                r.Holder = -1;
                r.HeldSince = -1;
            }
            int p = inside[r.Index];
            if (p == -2) continue; // fought over: the claim stands still
            if (p == -1 || p == r.Holder || p != r.Claimant)
            {
                r.Claimant = p == -1 || p == r.Holder ? -1 : p;
                r.Claim = 0;
                if (r.Claimant < 0) continue;
            }
            if (++r.Claim < ClaimTicks) continue;
            int old = r.Holder;
            r.Holder = p;
            r.HeldSince = _sim.Tick;
            r.Claimant = -1;
            r.Claim = 0;
            _sim.Events.SiteTaken(p, old, r);
        }

        int leader = -1;
        foreach (int p in _sim.Match.Players)
            if (_sim.Match.InPlay(p) && Held(p) >= Needed) leader = p;
        if (leader != Leader)
        {
            Leader = leader;
            Clock = 0;
            if (leader >= 0)
                foreach (int p in _sim.Match.Players)
                    _sim.Events.DominionStarted(p, leader);
        }
        if (Leader < 0) return;
        if (++Clock >= DominionTicks) _sim.Match.WinByDominion(Leader);
    }
}
