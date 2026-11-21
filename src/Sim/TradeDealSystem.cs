using System;

namespace Rts.Sim;

/// <summary>
/// Trade deals between rival players (the design's "factions can open a trade route to a rival or a city-state";
/// "no full diplomacy: only trade deals and short truces"). A player offers a deal to a rival
/// (<see cref="TradeDealCommand"/>); the offer stands for <see cref="OfferTicks"/>, and when the rival offers back
/// in that time (accepts), the two are trade partners until either ends it (<see cref="EndTradeDealCommand"/>,
/// at once) or one of them is out of the match. While the deal stands, each side's caravans may trade at the
/// other's town halls (see <see cref="Economy.EconomySystem"/>'s market trips), and neither side's units pick the
/// other's caravans as a target on their own; an explicit attack order still kills one, and a third player's
/// units hunt them as before. A deal is not a truce: soldiers of the two still fight.
/// The wild side, city-states and players out of the match never take part.
/// </summary>
public sealed class TradeDealSystem
{
    public const int Tps = Simulation.TicksPerSecond;
    /// <summary>How long an offer stands for the rival to accept: 30 seconds.</summary>
    public const int OfferTicks = 30 * Tps;
    /// <summary>After a deal ends, the same two cannot agree another for 1 minute.</summary>
    public const int CooldownTicks = 60 * Tps;
    /// <summary>The food a caravan carries to a partner's town hall on each trip.</summary>
    public const int Load = 20;
    /// <summary>Gold the caravan's owner gets for a load, per 100 blocks between their storage and the partner's hall.</summary>
    public const int Price = 3;
    /// <summary>The shortest route (blocks) that pays anything: a load carried less far than this brings back no gold.</summary>
    public const int MinRoute = 50;

    private const int N = Simulation.WildPlayer;
    private readonly Simulation _sim;
    // By pair (a * N + b, kept the same both ways): true while the two are trade partners.
    private readonly bool[] _deal = new bool[N * N];
    // By pair: no new deal before this tick.
    private readonly long[] _after = new long[N * N];
    // By (from * N + to): the tick an offer from one player to another runs out on.
    private readonly long[] _offers = new long[N * N];

    public TradeDealSystem(Simulation sim) => _sim = sim;

    private static bool IsPlayer(int p) => (uint)p < N;

    /// <summary>True if the two players have a trade deal this tick.</summary>
    public bool Partners(int a, int b) => a != b && IsPlayer(a) && IsPlayer(b) && _deal[a * N + b];

    /// <summary>True if <paramref name="from"/> has an offer of a deal standing to <paramref name="to"/>.</summary>
    public bool Offered(int from, int to) => from != to && IsPlayer(from) && IsPlayer(to) && _offers[from * N + to] > _sim.Tick;

    /// <summary>The tick the offer from one player to another runs out on, or -1 if none stands.</summary>
    public long OfferEnds(int from, int to) => Offered(from, to) ? _offers[from * N + to] : -1;

    /// <summary>The first tick the two may agree a deal again (at or before now: they may).</summary>
    public long FreeAt(int a, int b) => a != b && IsPlayer(a) && IsPlayer(b) ? _after[a * N + b] : 0;

    private bool InMatch(int p) => _sim.Match.Players.Count == 0 || _sim.Match.InPlay(p);

    /// <summary>
    /// True if <paramref name="a"/> could offer <paramref name="b"/> a deal now: two different players, both in the
    /// match (when there is one), the match not over, no deal already and not waiting after the last one.
    /// </summary>
    public bool CanOffer(int a, int b)
    {
        if (a == b || !IsPlayer(a) || !IsPlayer(b) || _sim.Match.Over) return false;
        if (!InMatch(a) || !InMatch(b)) return false;
        return !_deal[a * N + b] && _after[a * N + b] <= _sim.Tick;
    }

    /// <summary>
    /// What a load sold at a partner's hall brings back: <see cref="Price"/> gold per 100 blocks of route, nothing
    /// under <see cref="MinRoute"/>.
    /// </summary>
    public static int Earnings(int route) => route < MinRoute ? 0 : (int)Math.Min((long)Price * route / 100, int.MaxValue);

    /// <summary>An offer to a rival, or (when the rival's offer stands) the acceptance that starts the deal.</summary>
    internal void Apply(TradeDealCommand c)
    {
        int a = c.PlayerId, b = c.Other;
        if (!CanOffer(a, b)) return;
        long now = _sim.Tick;
        if (_offers[b * N + a] > now)
        {
            _deal[a * N + b] = _deal[b * N + a] = true;
            _offers[a * N + b] = _offers[b * N + a] = 0;
            _sim.Events.Truce(a, GameEventKind.DealStarted, b, a);
            _sim.Events.Truce(b, GameEventKind.DealStarted, a, a);
            return;
        }
        bool fresh = _offers[a * N + b] <= now;
        _offers[a * N + b] = now + OfferTicks;
        if (fresh) _sim.Events.Truce(b, GameEventKind.DealOffered, a, OfferTicks);
    }

    /// <summary>The player ends the deal with the rival at once; with no deal, withdraws their standing offer.</summary>
    internal void Apply(EndTradeDealCommand c)
    {
        int a = c.PlayerId, b = c.Other;
        if (!Partners(a, b))
        {
            if (Offered(a, b)) _offers[a * N + b] = 0;
            return;
        }
        End(a, b, a);
    }

    private void End(int a, int b, int by)
    {
        _deal[a * N + b] = _deal[b * N + a] = false;
        _after[a * N + b] = _after[b * N + a] = _sim.Tick + CooldownTicks;
        _sim.Events.Truce(a, GameEventKind.DealEnded, b, by);
        _sim.Events.Truce(b, GameEventKind.DealEnded, a, by);
    }

    /// <summary>A deal ends when either partner is out of the match, or the match is over.</summary>
    internal void Step()
    {
        for (int a = 0; a < N; a++)
        for (int b = a + 1; b < N; b++)
            if (_deal[a * N + b] && (_sim.Match.Over || !InMatch(a) || !InMatch(b)))
                End(a, b, -1);
    }

    /// <summary>Zero while no deal or offer was ever made, so matches without trade hash as before.</summary>
    public ulong ComputeHash()
    {
        ulong h = 0;
        for (int k = 0; k < N * N; k++)
        {
            if (!_deal[k] && _after[k] == 0 && _offers[k] == 0) continue;
            h = (h ^ 0xCBF29CE484222325UL ^ (ulong)k ^ (_deal[k] ? 1UL << 7 : 0) ^ (ulong)_after[k] << 8 ^ (ulong)_offers[k] << 36)
                * 0x100000001B3UL;
        }
        return h;
    }
}
