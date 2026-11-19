using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Searching the map's old ruins for what was left in them (see <see cref="RuinSystem"/>).</summary>
public class RuinTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int P0 = 0, P1 = 1;
    private const int RuinX = 48, RuinZ = 48;

    private const string Cache =
        "[ { \"id\": \"cache\", \"name\": \"a cache\", \"lore\": \"Words.\", \"goods\": { \"stone\": 100, \"iron\": 20 } } ]";
    private const string Circlet =
        "[ { \"id\": \"tomb\", \"name\": \"a tomb\", \"relic\": \"sunstone_circlet\" } ]";

    private static Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        return files;
    }

    /// <summary>A flat world with one ruin in the middle holding the finds given (as ruins.json), and a town hall for each player.</summary>
    private static Simulation WithRuin(string finds = Cache)
    {
        var files = Files();
        files["ruins.json"] = finds;
        var content = ContentLoader.Load(files);
        var map = new RegionMap(1, TestWorlds.Flat(96, 16, 96, Ground));
        map.Ruins.Add((RuinX, Ground, RuinZ));
        var sim = new Simulation(map, content);
        sim.Economy.PlaceFinished(P0, sim.Content.Building("town_hall"), 12, 12);
        sim.Economy.PlaceFinished(P1, sim.Content.Building("town_hall"), 80, 80);
        return sim;
    }

    private static AncientRuin TheRuin(Simulation sim) => sim.Ruins.Ruins[0];

    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Ground + 1);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int Stock(Simulation sim, int player, Good g) => sim.Economy.NearestStorage(player, RuinX, RuinZ)!.Stock[(int)g];

    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
        return maxTicks;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    public void EveryGeneratedRuinHoldsAFindAndTheRelicIsInOneAtMost(ulong seed)
    {
        var sim = new Simulation(seed, 512, 64, 512);
        var ruins = sim.Ruins.Ruins;
        Assert.Equal(sim.Map.Ruins.Count, ruins.Count);
        Assert.NotEmpty(ruins);
        for (int k = 0; k < ruins.Count; k++)
        {
            Assert.Equal(sim.Map.Ruins[k], (ruins[k].X, ruins[k].Y, ruins[k].Z));
            Assert.NotNull(ruins[k].Find);
            Assert.False(ruins[k].Searched);
        }
        Assert.True(ruins.Count(r => r.Find!.Relic != null) <= 1);
        // The same map deals the same finds.
        var again = new Simulation(seed, 512, 64, 512);
        Assert.Equal(ruins.Select(r => r.Find!.Id), again.Ruins.Ruins.Select(r => r.Find!.Id));
    }

    [Fact]
    public void MoreRuinsThanFindsGoRoundTheFindsWithoutARelic()
    {
        var map = new RegionMap(3, TestWorlds.Flat(96, 16, 96, Ground));
        for (int k = 0; k < 12; k++) map.Ruins.Add((8 + k * 6, Ground, 40));
        var sim = new Simulation(map);
        var finds = sim.Ruins.Ruins.Select(r => r.Find!).ToList();
        int all = sim.Content.RuinFinds.Count;
        Assert.Equal(all, finds.Take(all).Distinct().Count());
        Assert.Single(finds, f => f.Relic != null);
        Assert.All(finds.Skip(all), f => Assert.Null(f.Relic));
    }

    [Fact]
    public void AUnitWalksInAndFindsTheGoodsForItsStorage()
    {
        var sim = WithRuin();
        int spear = Spawn(sim, P0, "spearman", RuinX - 20, RuinZ);
        int stone = Stock(sim, P0, Good.Stone), iron = Stock(sim, P0, Good.Iron);
        Assert.True(sim.Ruins.CanUse(P0, 0, new[] { Index(sim, spear) }));
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, new[] { spear }, 0));
        sim.Step();
        Assert.Equal(0, sim.Ruins.Searching(spear));

        int took = RunUntil(sim, () => TheRuin(sim).Searched, 40 * Tps, "the ruin to be searched");
        Assert.True(took > RuinSystem.SearchTicks, "it walks there, then searches the whole time");
        Assert.True(sim.Ruins.InReach(TheRuin(sim), Index(sim, spear)), "it searched inside the walls");
        Assert.Equal(P0, TheRuin(sim).SearchedBy);
        Assert.Equal(spear, TheRuin(sim).Finder);
        Assert.Equal(stone + 100, Stock(sim, P0, Good.Stone));
        Assert.Equal(iron + 20, Stock(sim, P0, Good.Iron));
        Assert.Equal(-1, sim.Ruins.Searching(spear));
        var e = Assert.Single(sim.Events.All, e => e.Kind == GameEventKind.RuinSearched);
        Assert.Equal((P0, spear, 0), (e.Player, e.Subject, e.Def));

        // Once a match: nothing more to find there.
        int stoneAfter = Stock(sim, P0, Good.Stone);
        Assert.False(sim.Ruins.CanUse(P0, 0, new[] { Index(sim, spear) }));
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, new[] { spear }, 0));
        sim.Run(RuinSystem.SearchTicks + Tps);
        Assert.Equal(stoneAfter, Stock(sim, P0, Good.Stone));
        Assert.Single(sim.Events.All, e => e.Kind == GameEventKind.RuinSearched);
    }

    private static int TicksToSearch(int units)
    {
        var sim = WithRuin();
        var ids = Enumerable.Range(0, units).Select(k => Spawn(sim, P0, "worker", RuinX - 1 + k % 3, RuinZ - 1 + k / 3)).ToArray();
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, ids, 0));
        return RunUntil(sim, () => TheRuin(sim).Searched, 20 * Tps, "the ruin to be searched");
    }

    [Fact]
    public void MoreHandsSearchFasterUpToThree()
    {
        int one = TicksToSearch(1), three = TicksToSearch(3), five = TicksToSearch(5);
        Assert.InRange(one, RuinSystem.SearchTicks, RuinSystem.SearchTicks + 2);
        Assert.InRange(three, RuinSystem.SearchTicks / 3, RuinSystem.SearchTicks / 3 + 2);
        Assert.Equal(three, five);
    }

    [Fact]
    public void AnotherOrderStopsTheSearchButTheWorkDoneStays()
    {
        var sim = WithRuin();
        int w = Spawn(sim, P0, "worker", RuinX, RuinZ);
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, new[] { w }, 0));
        sim.Run(5 * Tps);
        int done = sim.Ruins.Progress(P0, 0);
        Assert.InRange(done, 5 * Tps - 2, 5 * Tps);

        sim.Schedule(sim.Tick, new MoveCommand(P0, new[] { w }, RuinX - 15, RuinZ));
        sim.Run(RuinSystem.SearchTicks);
        Assert.False(TheRuin(sim).Searched);
        Assert.Equal(-1, sim.Ruins.Searching(w));
        Assert.Equal(done, sim.Ruins.Progress(P0, 0));

        // Back to it: only the rest of the search is left.
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, new[] { w }, 0));
        sim.Step();
        int walked = RunUntil(sim, () => sim.Ruins.InReach(TheRuin(sim), Index(sim, w)), 20 * Tps, "the worker to walk back in");
        int rest = RunUntil(sim, () => TheRuin(sim).Searched, 20 * Tps, "the ruin to be searched");
        // The last steps to its spot inside the walls are walking, not searching: but far less than a whole search.
        Assert.InRange(rest, RuinSystem.SearchTicks - done - 2, RuinSystem.SearchTicks - Tps);
        Assert.True(walked > 0);
    }

    [Fact]
    public void TheFirstPlayerToFinishGetsItAndRivalSearchesEnd()
    {
        var sim = WithRuin();
        var ours = new[] { Spawn(sim, P0, "worker", RuinX - 1, RuinZ), Spawn(sim, P0, "worker", RuinX, RuinZ), Spawn(sim, P0, "worker", RuinX + 1, RuinZ) };
        int theirs = Spawn(sim, P1, "worker", RuinX, RuinZ + 1);
        int theirStone = Stock(sim, P1, Good.Stone);
        sim.Schedule(sim.Tick, new SearchRuinCommand(P1, new[] { theirs }, 0));
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, ours, 0));
        RunUntil(sim, () => TheRuin(sim).Searched, 20 * Tps, "the ruin to be searched");
        Assert.Equal(P0, TheRuin(sim).SearchedBy);
        Assert.Equal(-1, sim.Ruins.Searching(theirs));
        Assert.Equal(theirStone, Stock(sim, P1, Good.Stone));
        // A player cannot search with another's units.
        var sim2 = WithRuin();
        int other = Spawn(sim2, P1, "worker", RuinX, RuinZ);
        Assert.False(sim2.Ruins.CanUse(P0, 0, new[] { Index(sim2, other) }));
        sim2.Schedule(sim2.Tick, new SearchRuinCommand(P0, new[] { other }, 0));
        sim2.Step();
        Assert.Equal(-1, sim2.Ruins.Searching(other));
    }

    [Fact]
    public void AFightingUnitTakesUpARelicFoundThere()
    {
        var sim = WithRuin(Circlet);
        int spear = Spawn(sim, P0, "spearman", RuinX, RuinZ);
        int hp = sim.Units.Hp[Index(sim, spear)];
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, new[] { spear }, 0));
        RunUntil(sim, () => TheRuin(sim).Searched, 20 * Tps, "the ruin to be searched");
        Assert.Equal("sunstone_circlet", sim.RelicOf(Index(sim, spear))!.Id);
        Assert.Equal(hp + sim.Content.Relic("sunstone_circlet").Hp, sim.Units.Hp[Index(sim, spear)]);
        Assert.Empty(sim.Items.Items);
    }

    [Fact]
    public void AWorkerLeavesTheRelicLyingForOneThatCanBearIt()
    {
        var sim = WithRuin(Circlet);
        int w = Spawn(sim, P0, "worker", RuinX, RuinZ);
        sim.Schedule(sim.Tick, new SearchRuinCommand(P0, new[] { w }, 0));
        RunUntil(sim, () => TheRuin(sim).Searched, 20 * Tps, "the ruin to be searched");
        Assert.Null(sim.RelicOf(Index(sim, w)));
        var item = Assert.Single(sim.Items.Items);
        Assert.Equal("sunstone_circlet", item.Relic!.Id);

        int spear = Spawn(sim, P0, "spearman", RuinX - 6, RuinZ);
        sim.Schedule(sim.Tick, new MoveCommand(P0, new[] { spear }, item.X, item.Z));
        RunUntil(sim, () => sim.Items.Items.Count == 0, 20 * Tps, "the spearman to pick it up");
        Assert.Equal("sunstone_circlet", sim.RelicOf(Index(sim, spear))!.Id);
    }

    [Fact]
    public void SearchingIsPartOfTheStateAndTheLog()
    {
        var a = WithRuin();
        var b = WithRuin();
        int ua = Spawn(a, P0, "worker", RuinX, RuinZ), ub = Spawn(b, P0, "worker", RuinX, RuinZ);
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Schedule(a.Tick, new SearchRuinCommand(P0, new[] { ua }, 0));
        a.Step();
        b.Step();
        Assert.NotEqual(a.StateHash(), b.StateHash());

        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 search-ruin 0 2 4,7\n");
        Assert.Equal(new SearchRuinCommand(0, new[] { 4, 7 }, 2), log.Commands[0].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
        Assert.True(ub > 0);
    }

    [Fact]
    public void BadFindsAreReported()
    {
        var files = Files();
        files["ruins.json"] = "[ { \"id\": \"empty\", \"name\": \"Nothing\" }, { \"id\": \"crown\", \"name\": \"A crown\", \"relic\": \"crown\" },"
                              + " { \"id\": \"empty\", \"name\": \"Again\", \"goods\": { \"wood\": 5 }, \"gold\": 3 } ]";
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.StartsWith("ruins.json: find 'empty'") && m.Contains("needs \"goods\", a \"relic\" or both"));
        Assert.Contains(e.Errors, m => m.StartsWith("ruins.json: find 'crown'") && m.Contains("not in relics.json"));
        Assert.Contains(e.Errors, m => m.StartsWith("ruins.json: find 'empty'") && m.Contains("another find already has this id"));
        Assert.Contains(e.Errors, m => m.StartsWith("ruins.json: find 'empty'") && m.Contains("gold"));
    }

    [Fact]
    public void TheGameDataHasFindsWithLore()
    {
        var finds = ContentDb.Default.RuinFinds;
        Assert.True(finds.Count >= 4);
        Assert.All(finds, f => Assert.False(string.IsNullOrEmpty(f.Lore)));
        Assert.Contains(finds, f => f.Relic != null);
    }
}
