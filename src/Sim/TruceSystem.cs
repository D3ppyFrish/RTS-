using System;

namespace Rts.Sim;

/// <summary>
/// Short truces between rival players (the design's "no full diplomacy: only trade deals and short truces").
/// A player offers a truce to a rival (<see cref="TruceCommand"/>); the offer stands for <see cref="OfferTicks"/>,
/// and when the rival offers back in that time (accepts), the two are at peace for <see cref="TruceTicks"/>.
/// At peace, neither side's units, guards or raiders fight the other's (see <see cref="Hostile"/>): nobody picks
/// the other as a target, orders to attack the other are refused, and a fight already going stops. Either side
/// may end it early (<see cref="EndTruceCommand"/>), but only with <see cref="WarnTicks"/> of warning, so a truce
/// cannot be broken by surprise. When it ends, the two cannot agree another for <see cref="CooldownTicks"/>.
/// There are no shared victories: a truce never makes the two allies, so the match goes on as before.
/// The wild side, city-states' neutral guards and players out of the match never take part.
/// </summary>
public sealed class TruceSystem
{
    public const int Tps = Simulation.TicksPerSecond;
    /// <summary>How long a truce lasts once both sides agree: 5 minutes.</summary>
    public const int TruceTicks = 5 * 60 * Tps;
    /// <summary>How long an offer stands for the rival to accept: 30 seconds.</summary>
    public const int OfferTicks = 30 * Tps;
    /// <summary>The warning both sides get before a truce ends, on its own or broken off: 30 seconds.</summary>
    public const int WarnTicks = 30 * Tps;
    /// <summary>After a truce ends, the same two cannot agree another for 3 minutes.</summary>
    public const int CooldownTicks = 3 * 60 * Tps;

    private const int N = Simulation.WildPlayer;
    private readonly Simulation _sim;
    // By pair (a * N + b, kept the same both ways): the tick the truce ends on (at peace while the tick is below it).
    private readonly long[] _ends = new long[N * N];
    // By pair: no new truce before this tick.
    private readonly long[] _after = new long[N * N];
    // By pair: the player who broke the truce off, or -1.
    private readonly int[] _breaker = new int[N * N];
    // By (from * N + to): the tick an offer from one player to another runs out on.
    private readonly long[] _offers = new long[N * N];

    public TruceSystem(Simulation sim)
    {
        _sim = sim;
        Array.Fill(_breaker, -1);
    }

    private static bool IsPlayer(int p) => (uint)p < N;

    /// <summary>True if the two players are at peace this tick.</summary>
    public bool AtPeace(int a, int b) => a != b && IsPlayer(a) && IsPlayer(b) && _ends[a * N + b] > _sim.Tick;

    /// <summary>True if units of the two sides fight each other: different sides, not at peace.</summary>
    public bool Hostile(int a, int b) => a != b && !AtPeace(a, b);

    /// <summary>The tick the truce between the two ends on, or -1 if they are not at peace.</summary>
    public long EndsAt(int a, int b) => AtPeace(a, b) ? _ends[a * N + b] : -1;

    /// <summary>The player who broke the truce between the two off (it ends at <see cref="EndsAt"/>), or -1.</summary>
    public int BrokenBy(int a, int b) => AtPeace(a, b) ? _breaker[a * N + b] : -1;

    /// <summary>True if <paramref name="from"/> has an offer standing to <paramref name="to"/>.</summary>
    public bool Offered(int from, int to) => from != to && IsPlayer(from) && IsPlayer(to) && _offers[from * N + to] > _sim.Tick;

    /// <summary>The tick the offer from one player to another runs out on, or -1 if none stands.</summary>
    public long OfferEnds(int from, int to) => Offered(from, to) ? _offers[from * N + to] : -1;

    /// <summary>The first tick the two may agree a truce again (at or before now: they may).</summary>
    public long FreeAt(int a, int b) => a != b && IsPlayer(a) && IsPlayer(b) ? _after[a * N + b] : 0;

    /// <summary>
    /// True if <paramref name="a"/> could offer <paramref name="b"/> a truce now: two different players, both in
    /// the match (when there is one), the match not over, not at peace already and not waiting after the last one.
    /// </summary>
    public bool CanOffer(int a, int b)
    {
        if (a == b || !IsPlayer(a) || !IsPlayer(b) || _sim.Match.Over) return false;
        if (_sim.Match.Players.Count > 0 && (!_sim.Match.InPlay(a) || !_sim.Match.InPlay(b))) return false;
        return !AtPeace(a, b) && _after[a * N + b] <= _sim.Tick;
    }

    /// <summary>An offer to a rival, or (when the rival's offer stands) the acceptance that starts the truce.</summary>
    internal void Apply(TruceCommand c)
    {
        int a = c.PlayerId, b = c.Other;
        if (!CanOffer(a, b)) return;
        long now = _sim.Tick;
        if (_offers[b * N + a] > now)
        {
            long ends = now + TruceTicks;
            Set(_ends, a, b, ends);
            Set(_after, a, b, ends + CooldownTicks);
            _breaker[a * N + b] = _breaker[b * N + a] = -1;
            _offers[a * N + b] = _offers[b * N + a] = 0;
            _sim.Events.Truce(a, GameEventKind.TruceStarted, b, TruceTicks);
            _sim.Events.Truce(b, GameEventKind.TruceStarted, a, TruceTicks);
            return;
        }
        bool fresh = _offers[a * N + b] <= now;
        _offers[a * N + b] = now + OfferTicks;
        if (fresh) _sim.Events.Truce(b, GameEventKind.TruceOffered, a, OfferTicks);
    }

    /// <summary>
    /// The player breaks the truce off: it ends <see cref="WarnTicks"/> from now (sooner if it was ending anyway).
    /// Not at peace, it withdraws the player's standing offer to the rival.
    /// </summary>
    internal void Apply(EndTruceCommand c)
    {
        int a = c.PlayerId, b = c.Other;
        if (!AtPeace(a, b))
        {
            if (Offered(a, b)) _offers[a * N + b] = 0;
            return;
        }
        long now = _sim.Tick, ends = now + WarnTicks;
        if (_ends[a * N + b] <= ends) return;
        Set(_ends, a, b, ends);
        Set(_after, a, b, ends + CooldownTicks);
        _breaker[a * N + b] = _breaker[b * N + a] = a;
    }

    private static void Set(long[] by, int a, int b, long value) => by[a * N + b] = by[b * N + a] = value;

    /// <summary>Tells both sides when a truce is about to end, and when it has.</summary>
    internal void Step()
    {
        long now = _sim.Tick;
        for (int a = 0; a < N; a++)
        for (int b = a + 1; b < N; b++)
        {
            long ends = _ends[a * N + b];
            if (ends == 0) continue;
            int by = _breaker[a * N + b];
            if (ends - now == WarnTicks)
            {
                _sim.Events.Truce(a, GameEventKind.TruceEnding, b, by);
                _sim.Events.Truce(b, GameEventKind.TruceEnding, a, by);
            }
            else if (ends == now)
            {
                _sim.Events.Truce(a, GameEventKind.TruceEnded, b, by);
                _sim.Events.Truce(b, GameEventKind.TruceEnded, a, by);
            }
        }
    }

    public ulong ComputeHash()
    {
        ulong h = 0xCBF29CE484222325UL;
        for (int k = 0; k < N * N; k++)
        {
            if (_ends[k] == 0 && _after[k] == 0 && _offers[k] == 0) continue;
            h = (h ^ (ulong)k ^ (ulong)_ends[k] << 8 ^ (ulong)_after[k] << 24 ^ (ulong)_offers[k] << 40 ^ (ulong)(_breaker[k] + 1) << 60)
                * 0x100000001B3UL;
        }
        return h;
    }
}
