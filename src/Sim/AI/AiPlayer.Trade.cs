using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

/// <summary>
/// Trade deals with rivals (see <see cref="TradeDealSystem"/>). Every level takes the deals it is offered; the
/// smart levels (Easy and up) also offer them, send caravans to a partner's town hall while a deal is on, and
/// end a deal that feeds a rival grown too strong. The numbers are first guesses for playtesting.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>How often (ticks) the smart levels look at their trade: every 5 seconds.</summary>
    public const int TradeTicks = 5 * Tps;

    /// <summary>No offers of its own before this tick (its town is set up first): 6 minutes in.</summary>
    public const int TradeFromTicks = 6 * 60 * Tps;

    /// <summary>It offers the same rival again no sooner than this after an offer (taken or not): 3 minutes.</summary>
    public const int TradeOfferAgainTicks = 3 * 60 * Tps;

    /// <summary>The caravans it keeps while it has a deal on.</summary>
    public const int CaravansPerDeal = 2;

    /// <summary>Food in hand below which its caravans stop trading (so trade never starves the town).</summary>
    public const int TradeFoodFloor = 80;

    /// <summary>
    /// Food in hand it wants before it trains a caravan or sends one out (above <see cref="TradeFoodFloor"/>, so they
    /// do not flap). Food is what a town is usually short of, so its own caravans go only when it has plenty.
    /// </summary>
    public const int TradeSendFood = 140;

    /// <summary>
    /// A rival's soldiers worth more than this % of its own: it offers that rival no deal, refuses theirs (the smart
    /// levels) and ends one it has, rather than feed the stronger side food and gold.
    /// </summary>
    public const int TradeStrongPercent = 200;

    /// <summary>A deal stands at least this long before it ends one (so a deal does not flap): 2 minutes.</summary>
    public const int DealKeepTicks = 2 * 60 * Tps;

    private const int Players = Simulation.WildPlayer;
    // By rival: the tick it last offered them a deal (0: never), and the tick it saw the deal with them start (-1: none).
    private readonly long[] _dealOfferAt = new long[Players];
    private readonly long[] _dealSince = NoDeals();

    private static long[] NoDeals()
    {
        var a = new long[Players];
        Array.Fill(a, -1L);
        return a;
    }

    /// <summary>
    /// Answers the trade deals it is offered: it takes them (the food a partner's caravans bring is its to keep, and
    /// a deal stops none of its fights), except that the smart levels refuse a rival that is clearly the stronger
    /// (see <see cref="TradeStrongPercent"/>).
    /// </summary>
    private void AnswerDeals(Simulation sim, Action<Command> issue)
    {
        long mine = -1;
        for (int p = 0; p < Players; p++)
        {
            if (!sim.Deals.Offered(p, Player) || !sim.Deals.CanOffer(Player, p)) continue;
            if (Smart)
            {
                if (mine < 0) mine = PowerOf(sim, Soldiers(sim));
                if (TooStrong(sim, p, mine)) continue;
            }
            Note(sim, $"agrees a trade deal with player {p}");
            issue(new TradeDealCommand(Player, p));
        }
    }

    /// <summary>True if the rival's soldiers are worth more than <see cref="TradeStrongPercent"/>% of <paramref name="mine"/>.</summary>
    private static bool TooStrong(Simulation sim, int rival, long mine) =>
        PowerOf(sim, Soldiers(sim, rival)) * 100 > Math.Max(mine, 1) * TradeStrongPercent;

    /// <summary>
    /// The smart levels' trade: end deals with rivals grown too strong, offer one when none is on (it costs
    /// nothing, and a partner's caravans bring it food), keep <see cref="CaravansPerDeal"/> caravans while one is on, and send the idle ones to the nearest
    /// partner town hall that pays.
    /// </summary>
    private void Trade(Simulation sim, Building hall, Action<Command> issue)
    {
        var deals = sim.Deals;
        long now = sim.Tick;
        long mine = PowerOf(sim, Soldiers(sim));
        bool any = false;
        for (int p = 0; p < Players; p++)
        {
            if (!deals.Partners(Player, p))
            {
                _dealSince[p] = -1;
                continue;
            }
            if (_dealSince[p] < 0) _dealSince[p] = now;
            if (now - _dealSince[p] >= DealKeepTicks && TooStrong(sim, p, mine))
            {
                Note(sim, $"ends its trade deal with player {p}, grown too strong");
                issue(new EndTradeDealCommand(Player, p));
                _dealSince[p] = -1;
                continue;
            }
            any = true;
        }

        int food = sim.Economy.Stock(Player, Good.Food) - _keep[(int)Good.Food];
        if (!any && now >= TradeFromTicks && OfferTo(sim, hall, mine) is int rival)
        {
            Note(sim, $"offers a trade deal to player {rival}");
            issue(new TradeDealCommand(Player, rival));
            _dealOfferAt[rival] = now;
        }
        if (!any) return;

        // Its caravans: which are idle, which are trading with a partner.
        var u = sim.Units;
        var idle = new List<int>();
        var trading = new List<int>();
        int caravans = 0;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Owner[i] != Player || !sim.Content.Units[u.Def[i]].IsTrader) continue;
            caravans++;
            if (u.Job[i] == UnitJob.None) idle.Add(u.Id[i]);
            else if (u.Job[i] == UnitJob.Market) trading.Add(u.Id[i]);
        }
        foreach (int q in hall.Queue)
            if (sim.Content.Units[q].IsTrader) caravans++;

        // Short of food: the caravans stop (those carrying gold or a load take it home first, as at a deal's end).
        if (food < TradeFoodFloor)
        {
            if (trading.Count > 0)
            {
                Note(sim, "stops its caravans, short of food");
                issue(new StopCommand(Player, trading.ToArray()));
            }
            return;
        }

        if (caravans < CaravansPerDeal && food >= TradeSendFood && hall.Queue.Count == 0 && FirstTrained(sim, hall, UnitRole.Trader) is { } caravan
            && Rich(sim, caravan.Cost))
        {
            issue(new TrainCommand(Player, hall.Id, caravan.Id));
            if (Hard) Spend(caravan.Cost);
        }

        if (idle.Count > 0 && food >= TradeSendFood && MarketHall(sim, hall) is { } market)
        {
            Note(sim, $"sends {idle.Count} caravans to trade at player {market.Owner}'s town hall");
            issue(new MarketCommand(Player, idle.ToArray(), market.Id));
        }
    }

    /// <summary>
    /// The rival it would offer a deal to now: one it may offer (see <see cref="TradeDealSystem.CanOffer"/>), not
    /// offered lately, not too strong, with a town hall far enough to pay; the nearest such (a caravan earns about
    /// the same gold a minute on any paying route, and a short one is safer). Null if none.
    /// </summary>
    private int? OfferTo(Simulation sim, Building hall, long mine)
    {
        int? best = null;
        int bestRoute = int.MaxValue;
        for (int p = 0; p < Players; p++)
        {
            if (!sim.Deals.CanOffer(Player, p) || sim.Deals.Offered(Player, p)) continue;
            if (_dealOfferAt[p] > 0 && sim.Tick - _dealOfferAt[p] < TradeOfferAgainTicks) continue;
            if (Route(sim, hall, p) is not { } route || route.Length >= bestRoute) continue;
            if (TooStrong(sim, p, mine)) continue;
            (best, bestRoute) = (p, route.Length);
        }
        return best;
    }

    /// <summary>The partner town hall its caravans trade at: the nearest one, of any partner, whose route pays.</summary>
    private Building? MarketHall(Simulation sim, Building hall)
    {
        Building? best = null;
        int bestRoute = int.MaxValue;
        for (int p = 0; p < Players; p++)
        {
            if (!sim.Deals.Partners(Player, p)) continue;
            if (Route(sim, hall, p) is { } route && route.Length < bestRoute) (best, bestRoute) = (route.Hall, route.Length);
        }
        return best;
    }

    /// <summary>The player's nearest finished town hall to its own whose route pays (at least <see cref="TradeDealSystem.MinRoute"/>), and the route's length.</summary>
    private static (Building Hall, int Length)? Route(Simulation sim, Building hall, int owner)
    {
        (Building, int)? best = null;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != owner || !b.Active || b.Planned || b.Def.Kind != BuildingKind.TownHall) continue;
            int route = EconomySystem.MarketRoute(hall, b);
            if (TradeDealSystem.Earnings(route) <= 0) continue;
            if (best is not var (_, length) || route < length) best = (b, route);
        }
        return best;
    }
}
