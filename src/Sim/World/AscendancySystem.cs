using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.World;

/// <summary>
/// Ascendancy, the design doc's third way to win: finish a faction's final tech and great work deep in the most
/// dangerous ground. Each faction's final tech opens its great work (a building of kind
/// <see cref="BuildingKind.GreatWork"/>), which stands only in the deep caves (<see cref="EconomySystem.DeepGround"/>),
/// one per player at a time.
///
/// <para><b>Winning.</b> When a player's great work is finished its clock starts, and everyone is told where it
/// stands. If it is still standing <see cref="AscendancyTicks"/> later, and its owner is still in the match, they win
/// and every other player still in goes out (see <see cref="MatchSystem.WinByAscendancy"/>). Brought down before then,
/// the clock is gone with it: a new great work starts from nothing. Everyone is also told when a player starts
/// building one (a site, not a plan), and when one is brought down. All numbers are starting guesses to be tuned in
/// playtests.</para>
/// </summary>
public sealed class AscendancySystem
{
    /// <summary>Ticks a finished great work must stand for its owner to win: 5 minutes.</summary>
    public const int AscendancyTicks = 5 * 60 * Simulation.TicksPerSecond;

    /// <summary>A great work as this system follows it: its building, owner, and the tick it was finished (-1 while a site).</summary>
    public sealed class Work
    {
        public int Building { get; internal set; }
        public int Owner { get; internal set; }
        public long FinishedAt { get; internal set; } = -1;
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public int Z { get; internal set; }
        /// <summary>The building type's index in <see cref="Content.ContentDb.Buildings"/>.</summary>
        public int Def { get; internal set; }
    }

    private readonly Simulation _sim;
    // The great works sites and finished ones, by building id (ids only grow, so this is in the order they began).
    private readonly List<Work> _works = new();

    public AscendancySystem(Simulation sim)
    {
        _sim = sim;
    }

    /// <summary>Every great work being built or finished, in the order they were begun.</summary>
    public IReadOnlyList<Work> Works => _works;

    /// <summary>The player's finished great work, or null.</summary>
    public Work? Finished(int player)
    {
        foreach (var w in _works)
            if (w.Owner == player && w.FinishedAt >= 0) return w;
        return null;
    }

    /// <summary>Ticks left before the great work's owner wins, or -1 while it is a site.</summary>
    public long TicksLeft(Work w) => w.FinishedAt < 0 ? -1 : System.Math.Max(0, AscendancyTicks - (_sim.Tick - w.FinishedAt));

    /// <summary>The finished great work nearest winning (the one finished first), or null if none stands finished.</summary>
    public Work? Leading
    {
        get
        {
            Work? best = null;
            foreach (var w in _works)
                if (w.FinishedAt >= 0 && (best == null || w.FinishedAt < best.FinishedAt)) best = w;
            return best;
        }
    }

    public ulong ComputeHash()
    {
        ulong h = 0x6C8E9CF570932BD5UL;
        foreach (var w in _works)
            h = (h ^ (ulong)w.Building ^ (ulong)w.Owner << 24 ^ (ulong)w.FinishedAt << 32) * 0x100000001B3UL;
        return _works.Count == 0 ? 0 : h;
    }

    /// <summary>
    /// Before the match is checked: great works begun, finished and brought down are followed and told to everyone,
    /// and a great work that has stood finished for <see cref="AscendancyTicks"/> wins (see the class notes).
    /// </summary>
    internal void Step()
    {
        if (_sim.Match.Over) return;
        // Gone: brought down, or its owner out of the match.
        for (int k = 0; k < _works.Count; k++)
        {
            var w = _works[k];
            var b = _sim.Buildings.Get(w.Building);
            if (b != null && b.Owner == w.Owner && b.State != BuildingState.Planned && (_sim.Match.InPlay(w.Owner) || _sim.Match.Players.Count < 2))
            {
                if (w.FinishedAt < 0 && b.Active)
                {
                    w.FinishedAt = _sim.Tick;
                    Tell(w, GameEventKind.GreatWorkFinished);
                }
                continue;
            }
            if (b == null || b.Owner != w.Owner) Tell(w, GameEventKind.GreatWorkLost);
            _works.RemoveAt(k--);
        }
        // Begun: a site of one that is not followed yet.
        foreach (var b in _sim.Buildings.All)
        {
            if (b.Def.Kind != BuildingKind.GreatWork || b.State == BuildingState.Planned || Followed(b.Id)) continue;
            var w = new Work { Building = b.Id, Owner = b.Owner, X = b.X + b.Def.SizeX / 2, Y = b.Y, Z = b.Z + b.Def.SizeZ / 2, Def = b.Def.Index };
            _works.Add(w);
            if (b.Active)
            {
                w.FinishedAt = _sim.Tick;
                Tell(w, GameEventKind.GreatWorkFinished);
            }
            else Tell(w, GameEventKind.GreatWorkBegun);
        }
        if (_sim.Match.Players.Count < 2) return;
        var lead = Leading;
        // The tick it was finished on counts as one of them.
        if (lead != null && _sim.Tick + 1 - lead.FinishedAt >= AscendancyTicks) _sim.Match.WinByAscendancy(lead.Owner);
    }

    private bool Followed(int id)
    {
        foreach (var w in _works)
            if (w.Building == id) return true;
        return false;
    }

    /// <summary>Tells every player in the match (or the owner, with no match) about the great work.</summary>
    private void Tell(Work w, GameEventKind kind)
    {
        if (_sim.Match.Players.Count == 0)
        {
            _sim.Events.GreatWork(w.Owner, kind, w);
            return;
        }
        foreach (int p in _sim.Match.Players)
            if (_sim.Match.InPlay(p)) _sim.Events.GreatWork(p, kind, w);
    }
}
