using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Quarry and mine buildings: workers work them for stone, iron ore and coal that never run out (#50).</summary>
public class MineTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, string def, int x, int z) =>
        sim.Economy.PlaceFinished(0, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int[] Workers(Simulation sim, int n) => Enumerable.Range(0, n).Select(k => sim.SpawnUnit(0, "worker", 20 + k, 26)).ToArray();

    private static int Count(Simulation sim, UnitJob job, int building) =>
        Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Job[i] == job && sim.Units.JobTarget[i] == building);

    [Fact]
    public void TheDataHasAQuarryForStoneAndAMineForOreAndCoal()
    {
        var db = ContentDb.Default;
        var quarry = db.Building("quarry");
        var mine = db.Building("mine");
        Assert.Equal(BuildingKind.Quarry, quarry.Kind);
        Assert.Equal(BuildingKind.Quarry, mine.Kind);
        Assert.Equal(new[] { new GoodAmount(Good.Stone, 5) }, quarry.Works);
        Assert.Equal(new[] { new GoodAmount(Good.IronOre, 4), new GoodAmount(Good.Coal, 2) }, mine.Works);
        Assert.Empty(quarry.Requires);
        Assert.Equal(new[] { "smelting" }, mine.Requires.Select(t => t.Id));
    }

    [Fact]
    public void WorkersAtAQuarryBringStoneSteadily()
    {
        // Flat grass: no stone open to the air anywhere. The quarry gives it anyway, round after round.
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 20);
        var quarry = Place(sim, "quarry", 20, 20);
        var w = Workers(sim, 4);
        sim.Schedule(0, new WorkCommand(0, w, quarry.Id));
        sim.Run(2);
        Assert.Equal(quarry.Def.WorkerSlots, Count(sim, UnitJob.Quarry, quarry.Id)); // three places: the fourth is left out

        sim.Run(60 * Tps);
        int first = store.Stock[(int)Good.Stone];
        sim.Run(60 * Tps);
        int second = store.Stock[(int)Good.Stone] - first;
        // Three workers, a round of 12 seconds each plus the walk to storage: well over 30 stone a minute, every minute.
        Assert.InRange(first, 30, 90);
        Assert.InRange(second, 30, 90);
        Assert.Equal(0, store.Stock[(int)Good.IronOre] + store.Stock[(int)Good.Coal]);
        Assert.Equal(BlockType.Grass, sim.World.Get(30, Ground, 30)); // the ground was not dug
    }

    [Fact]
    public void AMineGivesTwiceAsMuchOreAsCoal()
    {
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 20);
        var mine = Place(sim, "mine", 20, 20);
        var w = Workers(sim, 2);
        sim.Schedule(0, new WorkCommand(0, w, mine.Id));
        sim.Run(3 * 60 * Tps);
        int ore = store.Stock[(int)Good.IronOre], coal = store.Stock[(int)Good.Coal];
        Assert.True(ore >= 24, $"ore: {ore}");
        Assert.InRange(ore - 2 * coal, 0, 8); // each worker's rounds go ore, coal, ore, coal: at most one ore round ahead each
        Assert.Equal(0, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void GatheringTechsSpeedUpTheRounds()
    {
        var sim = Flat();
        var def = sim.Content.Building("quarry");
        int before = sim.Economy.QuarryTicks(0, def);
        Assert.Equal(def.WorkTicks, before);
        sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
        Assert.True(sim.Economy.QuarryTicks(0, def) < before);
    }

    [Fact]
    public void AMineNeedsSmelting()
    {
        var sim = Flat();
        var hall = Place(sim, "town_hall", 10, 10);
        hall.Stock[(int)Good.Wood] = 500;
        hall.Stock[(int)Good.Stone] = 500;
        var w = Workers(sim, 1);
        sim.Schedule(0, new BuildCommand(0, w, "mine", 30, 30));
        sim.Run(2);
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def.Id == "mine");

        sim.Economy.Grant(0, sim.Content.Tech("smelting"));
        sim.Schedule(sim.Tick, new BuildCommand(0, w, "mine", 30, 30));
        sim.Run(2);
        var site = Assert.Single(sim.Buildings.All, b => b.Def.Id == "mine");
        for (int t = 0; t < 120 * Tps && !site.Active; t++) sim.Step();
        Assert.True(site.Active, "the worker built the mine");
    }

    [Fact]
    public void AutomaticWorkersWorkAQuarryWhenStoneIsNeeded()
    {
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 20);
        store.Stock[(int)Good.Food] = 600; // plenty of food and wood: stone is what is needed
        store.Stock[(int)Good.Wood] = 600;
        var quarry = Place(sim, "quarry", 20, 20);
        var mine = Place(sim, "mine", 30, 20); // no smelter: ore and coal are not wanted
        var w = Workers(sim, 4);
        sim.Schedule(0, new AutomateCommand(0, w, true));
        sim.Run(2 * Tps);
        Assert.Equal(quarry.Def.WorkerSlots, Count(sim, UnitJob.Quarry, quarry.Id));
        Assert.Equal(0, Count(sim, UnitJob.Quarry, mine.Id));
        sim.Run(60 * Tps);
        Assert.True(store.Stock[(int)Good.Stone] >= 30, $"stone: {store.Stock[(int)Good.Stone]}");
    }

    [Fact]
    public void AutomaticWorkersWorkAMineOnceThereIsASmelter()
    {
        var sim = Flat();
        var store = Place(sim, "storehouse", 10, 20);
        foreach (var g in new[] { Good.Food, Good.Wood, Good.Stone }) store.Stock[(int)g] = 600;
        Place(sim, "smelter", 10, 30);
        var mine = Place(sim, "mine", 30, 20);
        var w = Workers(sim, 2);
        sim.Schedule(0, new AutomateCommand(0, w, true));
        sim.Run(2 * Tps);
        Assert.Equal(mine.Def.WorkerSlots, Count(sim, UnitJob.Quarry, mine.Id));
        sim.Run(60 * Tps);
        Assert.True(store.Stock[(int)Good.IronOre] + store.Stock[(int)Good.Coal] > 0
            || Enumerable.Range(0, sim.Units.Count).Any(i => sim.Units.Job[i] == UnitJob.Haul), "ore and coal came in");
    }

    [Theory]
    [InlineData("\"works\": { \"stone\": 5 }, \"workTime\": 10", "\"workTime\": 10",
        "building 'quarry': a quarry needs \"works\"")]
    [InlineData("\"workerSlots\": 3, \"works\"", "\"workerSlots\": 0, \"works\"",
        "building 'quarry': a quarry needs \"workerSlots\" of 1 or more")]
    [InlineData("\"cost\": { \"wood\": 30 }, \"buildTime\": 15, \"housing\": 5,", "\"cost\": { \"wood\": 30 }, \"buildTime\": 15, \"housing\": 5, \"workTime\": 3,",
        "building 'house': only a quarry has \"works\" and \"workTime\"")]
    public void QuarryDataIsChecked(string old, string replacement, string message)
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        int at = files["buildings.json"].IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"buildings.json does not contain {old}");
        files["buildings.json"] = files["buildings.json"][..at] + replacement + files["buildings.json"][(at + old.Length)..];
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.StartsWith("buildings.json: " + message));
    }
}
