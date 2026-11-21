using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Gold: lodes in the hills and specks in the caves' walls, dug by workers and carried to storage, paying the
/// goblins' and the ogres' mercenaries; the goblins pay their tribute in it, and automatic workers dig it once
/// their player holds a town whose mercenaries cost it.
/// </summary>
public class GoldTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;
    // The goblin town's market, and the town hall 120 blocks west of it (as in CityStateTests).
    private const int TownX = 131, TownZ = 79;
    private const int HallX = 8, HallZ = 76;

    private static readonly Lazy<RegionMap> DefaultMap = new(() =>
        RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize));

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 24, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

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

    private static List<int> UnitsOf(Simulation sim, int owner, string unit)
    {
        var u = sim.Units;
        return Enumerable.Range(0, u.Count).Where(i => u.Owner[i] == owner && sim.Content.Units[u.Def[i]].Id == unit)
            .Select(i => u.Id[i]).OrderBy(id => id).ToList();
    }

    /// <summary>The player's spearmen break the town's totem and it stands again in their hands; the spearmen are then removed.</summary>
    private static void Conquer(Simulation sim, CityState town, int player)
    {
        var squad = Enumerable.Range(0, 12).Select(k => sim.SpawnUnit(player, "spearman", town.X - 2 + k % 4, town.Z + 3 + k / 4 * 2)).ToArray();
        var totem = sim.Cities.Totem(town) ?? throw new InvalidOperationException("the totem is down");
        Do(sim, new AttackBuildingCommand(player, squad, totem.Id));
        RunUntil(sim, () => town.Holder == player && town.TotemId != 0, 90 * Tps, "the squad breaks the totem and it stands again");
        foreach (int id in squad) sim.Units.Remove(id);
    }

    [Fact]
    public void GoldIsAGoodDugFromGoldOre()
    {
        Assert.Contains(Good.Gold, Goods.All);
        Assert.Equal(Goods.All.Length, Goods.Count);
        Assert.Equal("Gold", Goods.Name(Good.Gold));
        Assert.Equal(Good.Gold, Goods.Parse("Gold"));
        Assert.True(Harvest.CanGather(BlockType.GoldOre));
        Assert.True(Harvest.Yields(BlockType.GoldOre, out var good));
        Assert.Equal(Good.Gold, good);
        Assert.True(Harvest.Repeats(BlockType.GoldOre), "a worker moves on to the next gold nearby");
        Assert.True(Harvest.WorkTicks(BlockType.GoldOre) > Harvest.WorkTicks(BlockType.IronOre), "gold is slow to dig");
        // It is rock: it falls like stone, and breaks into stone rubble.
        Assert.True(BlockInfo.CanFall(BlockType.GoldOre));
        Assert.Equal(BlockType.StoneRubble, BlockInfo.RubbleOf(BlockType.GoldOre));
        Assert.True(BlockInfo.Last >= BlockType.GoldOre, "saved maps may hold gold ore");
    }

    [Fact]
    public void AWorkerDigsGoldAndCarriesItToStorage()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        for (int x = 24; x < 27; x++) sim.World.Set(x, Stand, 30, BlockType.GoldOre);
        int w = sim.SpawnUnit(P, "worker", 20, 30);
        sim.Schedule(0, new GatherCommand(P, new[] { w }, 24, Stand, 30));
        RunUntil(sim, () => store.Stock[(int)Good.Gold] == 3, 6000, "three gold stored");
        Assert.Equal(3, sim.Economy.Player(P).Gathered[(int)Good.Gold]);
        Assert.Equal(BlockType.Air, sim.World.Get(25, Stand, 30));
    }

    /// <summary>
    /// The default map has gold lodes in its hills, a few blocks under the rock and showing on it, all well away
    /// from the starts; and gold in the walls of the deep caves, deep under the land.
    /// </summary>
    [Fact]
    public void TheHillsHoldGoldLodesAwayFromTheStartsAndTheCavesSomeToo()
    {
        var map = DefaultMap.Value;
        var w = map.World;
        int deep = RegionGenerator.DeepLayers(w.SizeY);
        int hills = 0, showing = 0, caves = 0;
        var lodeColumns = new HashSet<(int, int)>();
        for (int z = 0; z < w.SizeZ; z++)
        for (int x = 0; x < w.SizeX; x++)
        for (int y = 1; y < w.SizeY; y++)
        {
            if (w.Get(x, y, z) != BlockType.GoldOre) continue;
            if (y < deep) { caves++; continue; }
            if (map.BiomeAt(x, z) != Biome.Hills) continue;
            hills++;
            lodeColumns.Add((x / 16, z / 16));
            if (w.Get(x, y + 1, z) == BlockType.Air) showing++;
            foreach (var s in map.Starts)
            {
                long dx = s.X - x, dz = s.Z - z;
                long keep = RegionGenerator.GoldKeepOff * w.SizeX / 512 - 4;
                Assert.True(dx * dx + dz * dz >= keep * keep, $"gold at {x},{y},{z} is too near the start at {s.X},{s.Z}");
            }
        }
        Assert.InRange(hills, 150, 20000);
        Assert.True(showing >= 20, $"only {showing} gold blocks show on the hills");
        Assert.True(lodeColumns.Count >= RegionGenerator.GoldLodesPerHill, "the gold lies in several lodes, not one heap");
        Assert.True(caves >= 100, $"only {caves} gold blocks in the deep caves' walls");
    }

    /// <summary>A map made with the same seed has its gold in the same places.</summary>
    [Fact]
    public void GoldIsPlacedTheSameEveryTime()
    {
        var a = RegionGenerator.Generate(77, 256, 64, 256);
        var b = RegionGenerator.Generate(77, 256, 64, 256);
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        int gold = 0;
        for (int z = 0; z < 256; z++)
        for (int x = 0; x < 256; x++)
        for (int y = 1; y < 64; y++)
            if (a.World.Get(x, y, z) == BlockType.GoldOre) gold++;
        Assert.True(gold > 0, "a small map has gold too");
    }

    [Fact]
    public void TheGoblinsAndOgresMercenariesCostGoldAndTheGoblinsPayTributeInIt()
    {
        var db = ContentDb.Default;
        var goblins = db.CityState("goblins");
        Assert.Equal((Good.Gold, 10), (goblins.TributeGood, goblins.TributeAmount));
        foreach (var id in new[] { "goblins", "ogres" })
            Assert.All(db.CityState(id).Mercenaries, m => Assert.Contains(m.Cost, c => c.Good == Good.Gold));
        // The kobolds still sell for crystal.
        Assert.All(db.CityState("kobolds").Mercenaries, m => Assert.DoesNotContain(m.Cost, c => c.Good == Good.Gold));
    }

    /// <summary>Hiring a goblin takes its gold from storage; with no gold there is no hire.</summary>
    [Fact]
    public void HiringAGoblinTakesItsGold()
    {
        var sim = Flat(160);
        var hall = Place(sim, P, "town_hall", HallX, HallZ);
        hall.Stock[(int)Good.Food] = 1000;
        var town = sim.Cities.Place(sim.Content.CityState("goblins"), TownX, TownZ) ?? throw new InvalidOperationException("no town");
        Conquer(sim, town, P);

        Do(sim, new HireCommand(P, town.Id, "goblin_cutthroat"));
        Assert.Empty(UnitsOf(sim, P, "goblin_cutthroat").Where(id => sim.Units.Town[Index(sim, id)] == 0));

        hall.Stock[(int)Good.Gold] = 25;
        Do(sim, new HireCommand(P, town.Id, "goblin_cutthroat"));
        Assert.Single(UnitsOf(sim, P, "goblin_cutthroat").Where(id => sim.Units.Town[Index(sim, id)] == 0));
        Assert.Equal(15, hall.Stock[(int)Good.Gold]);
        Assert.Equal(10, sim.Economy.Player(P).Spent[(int)Good.Gold]);
    }

    /// <summary>
    /// An automatic worker with gold ore at hand leaves it while gold buys nothing, and digs it once its player
    /// holds a town whose mercenaries cost gold.
    /// </summary>
    [Fact]
    public void AutomaticWorkersDigGoldOnlyWhenItCanHireMercenaries()
    {
        var sim = Flat(160);
        var hall = Place(sim, P, "town_hall", HallX, HallZ);
        hall.Stock[(int)Good.Food] = 1000;
        hall.Stock[(int)Good.Wood] = 1000;
        hall.Stock[(int)Good.Stone] = 1000;
        // Gold ore beside the hall, and nothing else to gather.
        for (int x = 20; x < 30; x++)
        for (int z = 70; z < 74; z++)
            sim.World.Set(x, Stand, z, BlockType.GoldOre);
        int w = sim.SpawnUnit(P, "worker", 18, 80);
        Do(sim, new AutomateCommand(P, new[] { w }, true));
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.Equal(0, hall.Stock[(int)Good.Gold] + sim.Units.CarryAmount[Index(sim, w)]);
        Assert.NotEqual(UnitJob.Gather, sim.Units.Job[Index(sim, w)]);

        var town = sim.Cities.Place(sim.Content.CityState("goblins"), TownX, TownZ) ?? throw new InvalidOperationException("no town");
        Conquer(sim, town, P);
        RunUntil(sim, () => hall.Stock[(int)Good.Gold] >= 2, 120 * Tps, "the worker digs gold for the goblins");
    }
}
