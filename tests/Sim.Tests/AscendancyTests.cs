using System.Linq;
using System;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Ascendancy: a faction's great work in the deep caves, standing long enough, wins (see <see cref="AscendancySystem"/>).</summary>
public class AscendancyTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int P0 = 0, P1 = 1; // the Settlers and the Orcs
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>
    /// A flat world with a start for each of two players. Deep (128 high), the ground at 3 is in the deep rock under
    /// the land, as the floor of a deep cave is; shallow (16 high), the map has no deep caves.
    /// </summary>
    private static Simulation WithStarts(bool deep = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, deep ? 128 : 16, 96, Ground));
        sim.Map.Starts.Add(new StartArea(P0, 16, Ground + 1, 20));
        sim.Map.Starts.Add(new StartArea(P1, 80, Ground + 1, 20));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static BuildingDef Hall(Simulation sim) => sim.Content.Building("hall_of_legends");

    /// <summary>The Settlers' final tech and goods enough for their great work.</summary>
    private static void Ready(Simulation sim, int player = P0)
    {
        sim.Economy.Grant(player, sim.Content.Tech("settler_legends"));
        var store = sim.Economy.PlaceFinished(player, sim.Content.Building("storehouse"), 40, 60)!;
        foreach (var g in Hall(sim).Cost) store.Stock[(int)g.Good] += g.Amount * 2;
    }

    private static Building Finish(Simulation sim, int player = P0, string def = "hall_of_legends", int x = 40, int z = 40) =>
        sim.Economy.PlaceFinished(player, sim.Content.Building(def), x, z)!;

    /// <summary>Digs out every block of the building this tick, so it collapses.</summary>
    private static void Raze(Simulation sim, Building b)
    {
        foreach (var t in b.Def.Blocks)
            sim.Schedule(sim.Tick, new DigCommand(b.Owner, b.X + t.X, b.Y + t.Y, b.Z + t.Z));
    }

    [Fact]
    public void EachFactionHasAGreatWorkOpenedByItsFinalTech()
    {
        var sim = WithStarts();
        foreach (var (faction, building, tech) in new[] { ("settlers", "hall_of_legends", "settler_legends"), ("orcs", "beast_throne", "orc_beast_king") })
        {
            var def = sim.Content.Building(building);
            Assert.Equal(BuildingKind.GreatWork, def.Kind);
            Assert.Equal(faction, def.Faction?.Id);
            Assert.Equal(tech, Assert.Single(def.Requires).Id);
            Assert.Equal(faction, sim.Content.Tech(tech).Faction?.Id);
            Assert.NotEmpty(sim.Content.Tech(tech).Requires);
        }
    }

    [Fact]
    public void AGreatWorkNeedsTheFinalTechAndIsTheFactionsOwn()
    {
        var sim = WithStarts();
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(P0, Hall(sim), 40, 40, out _));
        Ready(sim);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(P0, Hall(sim), 40, 40, out _));
        sim.Economy.Grant(P1, sim.Content.Tech("settler_legends"));
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(P1, Hall(sim), 40, 40, out _));
    }

    [Fact]
    public void AGreatWorkStandsOnlyInTheDeepCaves()
    {
        var shallow = WithStarts(deep: false);
        Ready(shallow);
        Assert.Equal(PlaceResult.NotDeep, shallow.Economy.CanPlace(P0, Hall(shallow), 40, 40, out _));
        Assert.False(shallow.Economy.DeepGround(Ground + 1));

        // On a deep map, ground up in the land (above the deep rock) does not count either.
        var deep = WithStarts();
        Ready(deep);
        int top = RegionGenerator.DeepLayers(deep.World.SizeY);
        Assert.True(deep.Economy.DeepGround(Ground + 1));
        Assert.False(deep.Economy.DeepGround(top));
        TestWorlds.Box(deep.World, 38, Ground + 1, 38, 50, top, 50, BlockType.Stone);
        TestWorlds.Box(deep.World, 38, top, 38, 50, top, 50, BlockType.Grass);
        Assert.Equal(PlaceResult.NotDeep, deep.Economy.CanPlace(P0, Hall(deep), 40, 40, out int y));
        Assert.Equal(top + 1, y);
    }

    [Fact]
    public void APlayerHasOneGreatWorkAtATime()
    {
        var sim = WithStarts();
        Ready(sim);
        int w = sim.SpawnUnit(P0, "worker", 36, 40);
        sim.Schedule(0, new BuildCommand(P0, new[] { w }, "hall_of_legends", 40, 40));
        sim.Step();
        Assert.Single(sim.Buildings.All, b => b.Def.Kind == BuildingKind.GreatWork);
        Assert.Equal(PlaceResult.OnlyOne, sim.Economy.CanPlace(P0, Hall(sim), 40, 60 + 8, out _));
        // Its rival may still raise theirs.
        sim.Economy.Grant(P1, sim.Content.Tech("orc_beast_king"));
        var store = sim.Economy.PlaceFinished(P1, sim.Content.Building("storehouse"), 70, 60)!;
        foreach (var g in sim.Content.Building("beast_throne").Cost) store.Stock[(int)g.Good] += g.Amount;
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(P1, sim.Content.Building("beast_throne"), 60, 40, out _));
    }

    [Fact]
    public void BeginningAGreatWorkIsToldToEveryone()
    {
        var sim = WithStarts();
        Ready(sim);
        int w = sim.SpawnUnit(P0, "worker", 36, 40);
        sim.Schedule(0, new BuildCommand(P0, new[] { w }, "hall_of_legends", 40, 40));
        sim.Run(2);
        var begun = sim.Events.All.Where(e => e.Kind == GameEventKind.GreatWorkBegun).ToList();
        Assert.Equal(new[] { P0, P1 }, begun.Select(e => e.Player).OrderBy(p => p));
        Assert.All(begun, e => Assert.Equal((P0, Hall(sim).Index), (e.Subject, e.Def)));
        var work = Assert.Single(sim.Ascendancy.Works);
        Assert.Equal(-1, work.FinishedAt);
        Assert.Equal(-1, sim.Ascendancy.TicksLeft(work));
        Assert.Null(sim.Ascendancy.Leading);
    }

    [Fact]
    public void AFinishedGreatWorkStandingLongEnoughWins()
    {
        var sim = WithStarts();
        Finish(sim);
        sim.Step();
        var work = Assert.Single(sim.Ascendancy.Works);
        Assert.Equal(work, sim.Ascendancy.Leading);
        Assert.Equal(work, sim.Ascendancy.Finished(P0));
        Assert.Equal(2, sim.Events.All.Count(e => e.Kind == GameEventKind.GreatWorkFinished && e.Subject == P0));

        sim.Run(AscendancySystem.AscendancyTicks - 2);
        Assert.False(sim.Match.Over);
        Assert.Equal(1, sim.Ascendancy.TicksLeft(work));
        sim.Step();
        Assert.True(sim.Match.Over);
        Assert.Equal(P0, sim.Match.Winner);
        Assert.True(sim.Match.ByAscendancy);
        Assert.False(sim.Match.ByDominion);
        Assert.False(sim.Match.InPlay(P1));
    }

    [Fact]
    public void AGreatWorkBroughtDownBeforeItsTimeWinsNothingAndANewOneStartsAgain()
    {
        var sim = WithStarts();
        var hall = Finish(sim);
        sim.Run(AscendancySystem.AscendancyTicks / 2);
        Raze(sim, hall);
        sim.Run(5 * Tps);
        Assert.Null(sim.Buildings.Get(hall.Id));
        Assert.Empty(sim.Ascendancy.Works);
        Assert.Equal(2, sim.Events.All.Count(e => e.Kind == GameEventKind.GreatWorkLost && e.Subject == P0));

        var again = Finish(sim);
        sim.Run(AscendancySystem.AscendancyTicks - 10);
        Assert.False(sim.Match.Over);
        Assert.True(sim.Ascendancy.TicksLeft(sim.Ascendancy.Finished(P0)!) > 0);
        sim.Run(20);
        Assert.True(sim.Match.ByAscendancy);
        Assert.Equal(P0, sim.Match.Winner);
        Assert.NotNull(sim.Buildings.Get(again.Id));
    }

    [Fact]
    public void TheGreatWorkFinishedFirstWins()
    {
        var sim = WithStarts();
        Finish(sim, P1, "beast_throne", 60, 40);
        sim.Run(Tps);
        Finish(sim, P0);
        sim.Run(AscendancySystem.AscendancyTicks);
        Assert.Equal(P1, sim.Match.Winner);
        Assert.True(sim.Match.ByAscendancy);
    }

    [Fact]
    public void AGreatWorkOfAPlayerWhoIsOutWinsNothing()
    {
        var sim = WithStarts();
        Finish(sim);
        sim.Step();
        sim.Schedule(sim.Tick, new SurrenderCommand(P0));
        sim.Run(2);
        Assert.Equal(P1, sim.Match.Winner);
        Assert.False(sim.Match.ByAscendancy);
    }

    [Fact]
    public void TheSameGreatWorkGivesTheSameHash()
    {
        ulong Run()
        {
            var sim = WithStarts();
            Finish(sim);
            sim.Run(30);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
        var none = WithStarts();
        Assert.Equal(0UL, none.Ascendancy.ComputeHash());
    }

    /// <summary>
    /// A flat deep 192 by 192 world: the computer player (the Orcs, player 1) at (40, 96) with that many spearmen by
    /// its hall, and the Settlers (player 0) far off at (170, 24) with their great work finished well away from both, at (110, 160).
    /// </summary>
    private static (Simulation Sim, Building Work) RivalsGreatWork(int spearmen)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(192, 128, 192, Ground));
        sim.Map.Starts.Add(new StartArea(P0, 170, Ground + 1, 24));
        sim.Map.Starts.Add(new StartArea(P1, 40, Ground + 1, 96));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(P1)!;
        for (int k = 0; k < spearmen; k++) sim.SpawnUnit(P1, "spearman", 36 + k % 8, hall.MaxZ + 2 + k / 8);
        var work = Finish(sim, P0, "hall_of_legends", 110, 160);
        return (sim, work);
    }

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void ComputerPlayersSendEveryoneTheyCanSpareToBringDownARivalsGreatWork(AiLevel level)
    {
        var (sim, work) = RivalsGreatWork(16);
        var ai = new AiPlayer(P1, P0, level);
        Assert.True(Play(sim, ai, () => ai.StrikeForce.Count > 0, 10 * Tps), "a strike force to set out");
        Assert.Equal(work.Id, ai.StrikeTarget);
        Assert.Equal(16 - AiPlayer.LeastGuard, ai.StrikeForce.Count);
        bool down = Play(sim, ai, () => sim.Buildings.Get(work.Id) == null, 4 * 60 * Tps);
        var u = sim.Units;
        string where = string.Join(" ", ai.StrikeForce.Select(id => $"{u.X[u.IndexOf(id)].FloorToInt()},{u.Z[u.IndexOf(id)].FloorToInt()}"));
        Assert.True(down, $"the great work to be brought down: lost {work.Lost} of {work.Def.Toughness}; strikers at {where}");
        Assert.False(sim.Match.ByAscendancy);
        Assert.True(Play(sim, ai, () => ai.StrikeTarget == 0, 10 * Tps), "the strike force to stand down");
    }

    [Fact]
    public void TheClassicComputerPlayerLeavesGreatWorksBe()
    {
        var (sim, _) = RivalsGreatWork(10);
        var ai = new AiPlayer(P1, P0, AiLevel.Classic);
        Assert.False(Play(sim, ai, () => ai.StrikeForce.Count > 0, 10 * Tps));
    }

    /// <summary>
    /// A deep world whose land stands over the deep rock (ground at 70), with a chamber dug out of the rock under it:
    /// floor of stone at 20, open from 21 to 34, over columns 30..50 by 30..50.
    /// </summary>
    private static Simulation UnderTheLand()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 128, 96, 70));
        sim.Map.Starts.Add(new StartArea(P0, 16, 71, 20));
        sim.Map.Starts.Add(new StartArea(P1, 80, 71, 80));
        TestWorlds.Box(sim.World, 30, 21, 30, 50, 34, 50, BlockType.Air);
        sim.PlaceStartingBuildings();
        return sim;
    }

    [Fact]
    public void AGreatWorkIsPlacedInAChamberUnderTheLandAtTheLevelGiven()
    {
        var sim = UnderTheLand();
        Ready(sim);
        // On top of the land it does not fit; at the chamber's floor it does.
        Assert.Equal(PlaceResult.NotDeep, sim.Economy.CanPlace(P0, Hall(sim), 36, 36, out int top));
        Assert.Equal(71, top);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(P0, Hall(sim), 36, 36, out int floor, level: 21));
        Assert.Equal(21, floor);
        // Up against the chamber's wall, the rock is in the way.
        Assert.NotEqual(PlaceResult.Ok, sim.Economy.CanPlace(P0, Hall(sim), 26, 36, out _, level: 21));

        int w = sim.SpawnUnit(P0, "worker", 32, 32, 21);
        sim.Schedule(0, new BuildCommand(P0, new[] { w }, "hall_of_legends", 36, 36, 21));
        sim.Step();
        var site = Assert.Single(sim.Buildings.All, b => b.Def.Kind == BuildingKind.GreatWork);
        Assert.Equal((36, 21, 36), (site.X, site.Y, site.Z));
        Assert.Equal(21, EconomySystem.LevelOf(site));
    }

    [Fact]
    public void TheLevelOfABuildOrPlanIsLogged()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 build 0 hall_of_legends 36 36 7 21\n6 plan 0 hall_of_legends 36 36 21\n7 build 0 house 4 4 7\n");
        Assert.Equal(new BuildCommand(0, new[] { 7 }, "hall_of_legends", 36, 36, 21), log.Commands[0].Command);
        Assert.Equal(new PlanCommand(0, "hall_of_legends", 36, 36, 21), log.Commands[1].Command);
        Assert.Equal(new BuildCommand(0, new[] { 7 }, "house", 4, 4), log.Commands[2].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
        Assert.Contains("5 build 0 hall_of_legends 36 36 7 21", log.ToString());
        Assert.Contains("7 build 0 house 4 4 7\n", log.ToString().Replace("\r", ""));
    }
}
