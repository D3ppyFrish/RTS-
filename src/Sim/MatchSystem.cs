using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim;

/// <summary>
/// Who is still in the match, and who won. Every player given a town hall at match start
/// (<see cref="Simulation.PlaceStartingBuildings"/>) is in the match. A player with no finished town hall
/// left standing is out, and so is a player who surrenders (<see cref="SurrenderCommand"/>). When one
/// player is left in, they win. If the last players go out on the same tick, the match ends in a draw. A
/// match with fewer than two players (a sandbox, most tests) never ends. Checked at the end of every tick. With
/// <see cref="Simulation.Dominion"/> on, a player holding most of the ancient sites long enough wins at once
/// (<see cref="World.DominionSystem"/>), and so does a player whose great work stands finished long enough
/// (<see cref="World.AscendancySystem"/>).
/// </summary>
public sealed class MatchSystem
{
    private readonly Simulation _sim;
    private readonly List<int> _players = new();
    // The tick each player went out on (by their place in _players), or -1 while they are in.
    private readonly List<long> _outAt = new();
    private readonly int[] _halls = new int[Simulation.WildPlayer];

    public MatchSystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>The players in the match, in order.</summary>
    public IReadOnlyList<int> Players => _players;
    /// <summary>True once the match has ended, with a winner or in a draw.</summary>
    public bool Over { get; private set; }
    /// <summary>The player who won, or -1 (no result yet, or a draw).</summary>
    public int Winner { get; private set; } = -1;
    /// <summary>True if the winner won by dominion, holding most of the ancient sites (see <see cref="World.DominionSystem"/>).</summary>
    public bool ByDominion { get; private set; }
    /// <summary>True if the winner won by ascendancy, their great work standing long enough (see <see cref="World.AscendancySystem"/>).</summary>
    public bool ByAscendancy { get; private set; }
    /// <summary>The tick the match ended on, or -1.</summary>
    public long EndTick { get; private set; } = -1;

    /// <summary>Puts the player in the match (match setup).</summary>
    internal void Join(int player)
    {
        if (player < 0 || player >= Simulation.WildPlayer || _players.Contains(player)) return;
        int at = 0;
        while (at < _players.Count && _players[at] < player) at++;
        _players.Insert(at, player);
        _outAt.Insert(at, -1);
    }

    /// <summary>True if the player is in the match and has not gone out.</summary>
    public bool InPlay(int player)
    {
        int k = _players.IndexOf(player);
        return k >= 0 && _outAt[k] < 0;
    }

    /// <summary>The tick the player went out on, or -1 if they are still in (or never played).</summary>
    public long OutAt(int player)
    {
        int k = _players.IndexOf(player);
        return k >= 0 ? _outAt[k] : -1;
    }

    /// <summary>A surrender: the player is out from this tick on (who won is settled at the end of the tick).</summary>
    internal void Apply(SurrenderCommand c)
    {
        if (Over) return;
        int k = _players.IndexOf(c.PlayerId);
        if (k >= 0 && _outAt[k] < 0) _outAt[k] = _sim.Tick;
    }

    /// <summary>The player held most of the ancient sites long enough: they win, and every other player still in goes out.</summary>
    internal void WinByDominion(int player)
    {
        if (WinOutright(player)) ByDominion = true;
    }

    /// <summary>The player's great work stood long enough: they win, and every other player still in goes out.</summary>
    internal void WinByAscendancy(int player)
    {
        if (WinOutright(player)) ByAscendancy = true;
    }

    /// <summary>The player wins at once and everyone else still in goes out; false if the match is over or they are out.</summary>
    private bool WinOutright(int player)
    {
        if (Over || !InPlay(player)) return false;
        for (int k = 0; k < _players.Count; k++)
            if (_players[k] != player && _outAt[k] < 0) _outAt[k] = _sim.Tick;
        Over = true;
        Winner = player;
        EndTick = _sim.Tick;
        return true;
    }

    internal void Step()
    {
        if (Over || _players.Count < 2) return;
        System.Array.Clear(_halls);
        foreach (var b in _sim.Buildings.All)
            if (b.Active && b.Def.Kind == BuildingKind.TownHall && b.Owner >= 0 && b.Owner < _halls.Length) _halls[b.Owner]++;
        int left = 0, last = -1;
        for (int k = 0; k < _players.Count; k++)
        {
            if (_outAt[k] >= 0) continue;
            if (_halls[_players[k]] == 0)
            {
                _outAt[k] = _sim.Tick;
                continue;
            }
            left++;
            last = _players[k];
        }
        if (left > 1) return;
        Over = true;
        Winner = last;
        EndTick = _sim.Tick;
    }

    public ulong ComputeHash()
    {
        ulong h = 0xCBF29CE484222325UL;
        for (int k = 0; k < _players.Count; k++)
            h = (h ^ (ulong)_players[k] ^ (ulong)_outAt[k] << 8) * 0x100000001B3UL;
        return h ^ (ulong)(Winner + 1) << 56 ^ (ulong)EndTick ^ (ByDominion ? 0x40000000000000UL : 0)
            ^ (ByAscendancy ? 0x20000000000000UL : 0);
    }
}
