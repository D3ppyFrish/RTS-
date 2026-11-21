using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Trade deals between rival players, and caravans trading at a partner's town hall (see <see cref="TradeDealSystem"/>).</summary>
public class TradeDealTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    /// <summary>Three players on a flat map: 0 in the west, 1 in the east (190 blocks apart), 2 in the north.</summary>
    private static Simulation ThreeTowns()
    {
        var sim = Flat(256);
        (int X, int Z)[] starts = { (30, 128), (220, 128), (128, 30) };
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static void Agree(Simulation sim, int a, int b)
    {
        sim.Schedule(sim.Tick, new TradeDealCommand(a, b));
        sim.Schedule(sim.Tick + 1, new TradeDealCommand(b, a));
        sim.Run(2);
        Assert.True(sim.Deals.Partners(a, b));
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Run(1);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks && !done(); t++) sim.Step();
        Assert.True(done(), $"timed out waiting until {what}");
    }

    private static List<GameEvent> Events(Simulation sim, int player, GameEventKind kind) =>
        sim.Events.After(0, player).Where(e => e.Kind == kind).ToList();

    [Fact]
    public void AnOfferAcceptedInTimeMakesTheTwoPartnersUntilOneEndsIt()
    {
        var sim = Flat();
        Do(sim, new TradeDealCommand(0, 1));
        Assert.False(sim.Deals.Partners(0, 1));
        Assert.True(sim.Deals.Offered(0, 1));
        Assert.Equal(0, Assert.Single(Events(sim, 1, GameEventKind.DealOffered)).Subject);

        Do(sim, new TradeDealCommand(1, 0));
        Assert.True(sim.Deals.Partners(0, 1));
        Assert.True(sim.Deals.Partners(1, 0));
        Assert.False(sim.Deals.Partners(0, 2));
        Assert.False(sim.Deals.Offered(0, 1));
        Assert.Single(Events(sim, 0, GameEventKind.DealStarted));
        Assert.Single(Events(sim, 1, GameEventKind.DealStarted));
        // A deal is not a truce: the two still fight.
        Assert.False(sim.Truces.AtPeace(0, 1));

        // It has no end of its own.
        sim.Run(20 * 60 * Tps);
        Assert.True(sim.Deals.Partners(0, 1));

        // Either side ends it at once, and both are told; another waits a minute.
        Do(sim, new EndTradeDealCommand(1, 0));
        Assert.False(sim.Deals.Partners(0, 1));
        var ended = Assert.Single(Events(sim, 0, GameEventKind.DealEnded));
        Assert.Equal((1, 1), (ended.Subject, ended.Def));
        Assert.Single(Events(sim, 1, GameEventKind.DealEnded));
        Assert.False(sim.Deals.CanOffer(0, 1));
        sim.Run(TradeDealSystem.CooldownTicks);
        Assert.True(sim.Deals.CanOffer(0, 1));
    }

    [Fact]
    public void AnOfferNotAcceptedInTimeRunsOutAndCanBeTakenBack()
    {
        var sim = Flat();
        Do(sim, new TradeDealCommand(0, 1));
        sim.Run(TradeDealSystem.OfferTicks);
        Assert.False(sim.Deals.Offered(0, 1));
        Do(sim, new TradeDealCommand(1, 0));
        Assert.False(sim.Deals.Partners(0, 1));

        Do(sim, new EndTradeDealCommand(1, 0));
        Assert.False(sim.Deals.Offered(1, 0));
        Do(sim, new TradeDealCommand(0, 1));
        Assert.False(sim.Deals.Partners(0, 1));

        // Nobody deals with the wild side or with themselves.
        Do(sim, new TradeDealCommand(0, Simulation.WildPlayer));
        Do(sim, new TradeDealCommand(0, 0));
        Assert.False(sim.Deals.Offered(0, Simulation.WildPlayer));
        Assert.False(sim.Deals.Partners(0, 0));
    }

    /// <summary>
    /// A caravan takes 20 food from its own storage to the partner's town hall, which keeps the food, and brings
    /// back 3 gold per 100 blocks of route.
    /// </summary>
    [Fact]
    public void ACaravanSellsFoodAtAPartnersHallAndBringsBackGold()
    {
        var sim = ThreeTowns();
        var mine = sim.Economy.TownHall(0)!;
        var theirs = sim.Economy.TownHall(1)!;
        int route = EconomySystem.MarketRoute(mine, theirs);
        Assert.InRange(route, 185, 195);
        int gain = TradeDealSystem.Earnings(route);
        Assert.Equal(TradeDealSystem.Price * route / 100, gain);
        Assert.True(gain > 0);
        int myFood = mine.Stock[(int)Good.Food], myGold = mine.Stock[(int)Good.Gold], theirFood = theirs.Stock[(int)Good.Food];

        int caravan = sim.SpawnUnit(0, "caravan", mine.X + 3, mine.MaxZ + 3);
        // No deal yet: the order is refused.
        Do(sim, new MarketCommand(0, new[] { caravan }, theirs.Id));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, caravan)]);

        Agree(sim, 0, 1);
        int worker = sim.SpawnUnit(0, "worker", mine.X + 5, mine.MaxZ + 3);
        Do(sim, new MarketCommand(0, new[] { caravan, worker }, theirs.Id));
        Assert.Equal(UnitJob.Market, sim.Units.Job[Index(sim, caravan)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, worker)]);
        RunUntil(sim, () => sim.Units.Carry[Index(sim, caravan)] == Good.Gold, 150 * Tps, "the caravan sells its load");
        Assert.Equal(myFood - TradeDealSystem.Load, mine.Stock[(int)Good.Food]);
        Assert.Equal(theirFood + TradeDealSystem.Load, theirs.Stock[(int)Good.Food]);
        Assert.Equal(gain, sim.Units.CarryAmount[Index(sim, caravan)]);
        RunUntil(sim, () => mine.Stock[(int)Good.Gold] == myGold + gain, 150 * Tps, "the gold comes home");
        // And it goes again.
        RunUntil(sim, () => sim.Units.Carry[Index(sim, caravan)] == Good.Food && sim.Units.CarryAmount[Index(sim, caravan)] > 0,
            10 * Tps, "the caravan takes another load");
        Assert.Equal(UnitJob.Market, sim.Units.Job[Index(sim, caravan)]);
    }

    [Fact]
    public void OnlyAPartnersFinishedTownHallTakesTrade()
    {
        var sim = ThreeTowns();
        Agree(sim, 0, 1);
        int caravan = sim.SpawnUnit(0, "caravan", 30, 140);
        var store1 = sim.Economy.PlaceFinished(1, sim.Content.Building("storehouse"), 200, 100)!;
        // Not a town hall, not a partner's, not one's own.
        Do(sim, new MarketCommand(0, new[] { caravan }, store1.Id));
        Do(sim, new MarketCommand(0, new[] { caravan }, sim.Economy.TownHall(2)!.Id));
        Do(sim, new MarketCommand(0, new[] { caravan }, sim.Economy.TownHall(0)!.Id));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, caravan)]);
    }

    [Fact]
    public void WhenTheDealEndsACaravanTakesWhatItCarriesHome()
    {
        var sim = ThreeTowns();
        var mine = sim.Economy.TownHall(0)!;
        var theirs = sim.Economy.TownHall(1)!;
        int food = mine.Stock[(int)Good.Food];
        Agree(sim, 0, 1);
        int caravan = sim.SpawnUnit(0, "caravan", mine.X + 3, mine.MaxZ + 3);
        Do(sim, new MarketCommand(0, new[] { caravan }, theirs.Id));
        RunUntil(sim, () => sim.Units.CarryAmount[Index(sim, caravan)] == TradeDealSystem.Load, 10 * Tps, "the caravan takes a load");
        sim.Run(15 * Tps);
        Do(sim, new EndTradeDealCommand(1, 0));
        RunUntil(sim, () => sim.Units.Job[Index(sim, caravan)] == UnitJob.None, 90 * Tps, "the caravan gives up the trade");
        Assert.Equal(0, sim.Units.CarryAmount[Index(sim, caravan)]);
        Assert.Equal(food, mine.Stock[(int)Good.Food]);
    }

    [Fact]
    public void APlayerOutOfTheMatchEndsTheirDeals()
    {
        var sim = ThreeTowns();
        Agree(sim, 0, 1);
        Do(sim, new SurrenderCommand(1));
        sim.Run(1);
        Assert.False(sim.Deals.Partners(0, 1));
        Assert.Equal(-1, Assert.Single(Events(sim, 0, GameEventKind.DealEnded)).Def);
    }

    /// <summary>
    /// A partner's soldiers let its caravans pass (though they still fight its soldiers); a third player's soldiers
    /// kill them, and a partner can still kill one on an explicit order.
    /// </summary>
    [Fact]
    public void PartnersLetEachOthersCaravansBe()
    {
        var sim = Flat(96);
        int caravan = sim.SpawnUnit(0, "caravan", 40, 40);
        int guard = sim.SpawnUnit(1, "spearman", 44, 40);
        Agree(sim, 0, 1);
        sim.Run(15 * Tps);
        Assert.True(sim.Units.Exists(caravan));
        Assert.Equal(sim.Content.Unit("caravan").MaxHp, sim.Units.Hp[Index(sim, caravan)]);

        // Its soldiers are fair game.
        int soldier = sim.SpawnUnit(0, "spearman", 44, 46);
        RunUntil(sim, () => sim.Units.Hp[Index(sim, guard)] < sim.Content.Unit("spearman").MaxHp
                            || sim.Units.Hp[Index(sim, soldier)] < sim.Content.Unit("spearman").MaxHp, 20 * Tps, "the soldiers fight");
        Do(sim, new StopCommand(0, new[] { soldier }));
        sim.Units.Hp[Index(sim, soldier)] = 1;
        RunUntil(sim, () => !sim.Units.Exists(soldier), 30 * Tps, "the partner's spearman kills the soldier");

        // On an explicit order it attacks the caravan.
        Do(sim, new AttackCommand(1, new[] { guard }, caravan));
        RunUntil(sim, () => !sim.Units.Exists(caravan), 60 * Tps, "the ordered attack kills the caravan");

        // A third player's soldier goes after one on its own.
        int other = sim.SpawnUnit(0, "caravan", 40, 60);
        sim.SpawnUnit(2, "spearman", 44, 60);
        RunUntil(sim, () => !sim.Units.Exists(other), 60 * Tps, "a third player's spearman kills the caravan");
    }

    [Fact]
    public void AComputerPlayerTakesADealItIsOffered()
    {
        var sim = ThreeTowns();
        var ai = new[] { new AiPlayer(1, 1) };
        sim.Schedule(0, new TradeDealCommand(0, 1));
        for (int t = 0; t < 10 * Tps && !sim.Deals.Partners(0, 1); t++)
        {
            foreach (var p in ai) p.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        Assert.True(sim.Deals.Partners(0, 1));
    }

    [Fact]
    public void DealsAreInTheHashAndReplayFromTheLog()
    {
        var log = CommandLog.UsualStart(5, 128, 48, 128);
        log.Add(1, new TradeDealCommand(0, 1));
        log.Add(3, new TradeDealCommand(1, 0));
        log.Add(5, new MarketCommand(0, new[] { 7, 8 }, 2));
        log.Add(40, new EndTradeDealCommand(0, 1));
        log.Ticks = 60;
        var text = log.ToString();
        Assert.Contains("deal 0 1", text);
        Assert.Contains("enddeal 0 1", text);
        Assert.Contains("market 0 2 7,8", text);
        var again = CommandLog.Parse(text);
        Assert.Equal(log.Commands, again.Commands);

        var a = MatchReplay.Run(log, 10, out var played);
        var b = MatchReplay.Run(again, 10);
        Assert.Equal(-1, MatchReplay.FirstMismatch(a, b));
        Assert.False(played.Deals.Partners(0, 1));
        Assert.False(played.Deals.CanOffer(0, 1));
        var other = log.CreateSimulation();
        other.Run(60);
        Assert.NotEqual(other.StateHash(), played.StateHash());
    }
}
