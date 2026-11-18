using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Houses, halls and the rest upgraded where they stand into bigger, better buildings.</summary>
public class UpgradeTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4

    private static Simulation Flat()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, Ground));
        sim.TestMode = true;
        return sim;
    }

    private static Building Put(Simulation sim, string def, int x, int z, int owner = 0) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"no room for a {def} at {x}, {z}");

    private static Building Store(Simulation sim, int wood, int stone, int food = 0)
    {
        var store = Put(sim, "storehouse", 6, 6);
        store.Stock[(int)Good.Wood] = wood;
        store.Stock[(int)Good.Stone] = stone;
        store.Stock[(int)Good.Food] = food;
        return store;
    }

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    [Fact]
    public void EachBuildingHasAWayUpToSomethingBiggerAndBetter()
    {
        var db = Flat().Content;
        foreach (var (from, to) in new[]
                 {
                     ("house", "large_house"), ("large_house", "manor"), ("town_hall", "great_hall"), ("great_hall", "castle_keep"),
                     ("storehouse", "warehouse"), ("barracks", "great_barracks"), ("smelter", "foundry"), ("farm", "large_farm"),
                 })
        {
            var a = db.Building(from);
            var b = db.Building(to);
            Assert.Same(b, a.Upgrade);
            Assert.Same(a, b.UpgradeOf);
            Assert.Equal(a.Kind, b.Kind);
            Assert.True(b.SizeX > a.SizeX && b.SizeZ > a.SizeZ, $"a {to} is bigger than a {from}");
            Assert.True(b.Height >= a.Height, $"a {to} is at least as tall as a {from}");
            // Better at what it does.
            Assert.True(b.Housing > a.Housing || b.Speed > a.Speed || b.WorkerSlots > a.WorkerSlots || (a.Raidable && !b.Raidable),
                $"a {to} does more than a {from}");
            // It trains what it was upgraded from trains.
            Assert.Equal(a.Trains, b.Trains);
        }
        Assert.Contains("worker", db.Building("castle_keep").Trains);
        Assert.Equal(new[] { 5, 12, 25 }, new[] { "house", "large_house", "manor" }.Select(id => db.Building(id).Housing));
        Assert.Equal(new[] { 25, 40, 60 }, new[] { "town_hall", "great_hall", "castle_keep" }.Select(id => db.Building(id).Housing));
    }

    [Fact]
    public void AHouseGrowsFromItsMiddleIntoALargeHouseAndThenAManor()
    {
        var sim = Flat();
        var db = sim.Content;
        var store = Store(sim, wood: 200, stone: 200);
        var house = Put(sim, "house", 30, 30);
        int y = house.Y;
        int worker = sim.SpawnUnit(0, "worker", 28, 36);
        // An idle soldier stands on the ground the large house grows onto.
        int idler = sim.SpawnUnit(0, "spearman", 34, 31);
        Assert.Equal(5, sim.Economy.Housing(0));

        Assert.Equal(PlaceResult.Ok, sim.Economy.CanUpgrade(house));
        sim.Schedule(0, new UpgradeCommand(0, new[] { worker }, house.Id));
        sim.Step();
        Assert.Same(db.Building("large_house"), house.Upgrade);
        Assert.Equal(200 - 45, store.Stock[(int)Good.Wood]);
        Assert.Equal(200 - 10, store.Stock[(int)Good.Stone]);
        // Meanwhile it stands as it is, and nothing can be put on the ground it is to grow onto (nor next to it).
        Assert.Equal("house", house.Def.Id);
        Assert.Equal((29, 29, 34, 34), house.Claim);
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, db.Building("house"), 35, 30, out _));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, db.Building("house"), 36, 30, out _));

        RunUntil(sim, () => house.Upgrade == null, 120 * Tps, "the worker to upgrade the house");
        Assert.Equal("large_house", house.Def.Id);
        Assert.True(house.Active);
        Assert.Equal((29, y, 29), (house.X, house.Y, house.Z)); // a column further out on each side (6 for 4)
        Assert.Equal(12, sim.Economy.Housing(0));
        Assert.Equal(house.MaxHealth, house.Health);
        // The new walls stand on the new ground, and where the old walls stood is the room inside.
        Assert.Equal(BlockType.StoneBrick, sim.World.Get(29, y, 29));
        Assert.Equal(BlockType.Planks, sim.World.Get(34, y + 1, 29));
        Assert.Equal(BlockType.Air, sim.World.Get(30, y + 1, 30));
        Assert.Equal(BlockType.Air, sim.World.Get(33, y + 2, 32));
        // The soldier was sent off the new ground.
        int i = sim.Units.IndexOf(idler);
        Assert.False(house.Covers(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()));

        // A manor needs masonry.
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanUpgrade(house));
        sim.Schedule(sim.Tick, new SandboxTechCommand(0, 0, "masonry"));
        sim.Step();
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanUpgrade(house));
        sim.Schedule(sim.Tick, new UpgradeCommand(0, new[] { worker }, house.Id));
        RunUntil(sim, () => house.Def.Id == "manor", 180 * Tps, "the worker to make the large house a manor");
        Assert.Equal((28, 28, 35, 35), (house.X, house.Z, house.MaxX, house.MaxZ));
        Assert.Equal(25, sim.Economy.Housing(0));
        Assert.Equal(BlockType.Planks, sim.World.Get(28, y + 4, 31));
        Assert.Equal(BlockType.Air, sim.World.Get(29, y + 1, 31));
        // A manor is as big as houses get.
        Assert.Null(house.Def.Upgrade);
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(house));
    }

    [Fact]
    public void ABuildingWithoutRoomRoundItCannotGrow()
    {
        var sim = Flat();
        Store(sim, wood: 200, stone: 200);
        // A gap of one column between two houses, and none to spare for either to grow into.
        var west = Put(sim, "house", 30, 30);
        var east = Put(sim, "house", 35, 30);
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(west));
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(east));
        sim.Schedule(0, new UpgradeCommand(0, Array.Empty<int>(), west.Id));
        sim.Step();
        Assert.Null(west.Upgrade);
        Assert.Equal(200, sim.Economy.Stock(0, Good.Wood));

        // Ground that rises more than a block where it would grow is in the way too, and so is a tree.
        var hill = Put(sim, "house", 30, 44);
        TestWorlds.Box(sim.World, 29, Ground + 1, 46, 29, Ground + 2, 46, BlockType.Stone);
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(hill));
        TestWorlds.Box(sim.World, 29, Ground + 1, 46, 29, Ground + 2, 46, BlockType.Air);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanUpgrade(hill));
        TestWorlds.Tree(sim.World, 34, 47, 4, Ground);
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(hill));
    }

    [Fact]
    public void AGreatHallTrainsWorkersFasterThanATownHall()
    {
        int Ticks(string hallDef)
        {
            var sim = Flat();
            var hall = Put(sim, hallDef, 30, 30);
            hall.Stock[(int)Good.Food] = 500;
            int units = sim.Units.Count;
            sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
            int t = 0;
            while (sim.Units.Count == units && t < 60 * Tps)
            {
                sim.Step();
                t++;
            }
            return t;
        }
        int hall = Ticks("town_hall"), great = Ticks("great_hall"), keep = Ticks("castle_keep");
        int train = Flat().Content.Unit("worker").TrainTicks;
        Assert.InRange(hall, train, train + 2);
        Assert.InRange(great, train * 2 / 3, train * 2 / 3 + 2);
        Assert.InRange(keep, train / 2, train / 2 + 2);
    }

    [Fact]
    public void AFoundrySmeltsTwiceAsFastAsASmelter()
    {
        int Ticks(string def)
        {
            var sim = Flat();
            var smelter = Put(sim, def, 30, 30);
            smelter.Stock[(int)Good.IronOre] = 100;
            smelter.Stock[(int)Good.Coal] = 100;
            int t = 0;
            while (smelter.Stock[(int)Good.Iron] < 3 && t < 120 * Tps)
            {
                sim.Step();
                t++;
            }
            return t;
        }
        int smelter = Ticks("smelter"), foundry = Ticks("foundry");
        Assert.InRange(foundry, smelter / 2 - 3, smelter / 2 + 3);
    }

    [Fact]
    public void AFarmGrowsIntoALargeFarmThatMoreWorkersWork()
    {
        var sim = Flat();
        Store(sim, wood: 100, stone: 0);
        var farm = Put(sim, "farm", 30, 30);
        int y = farm.Y;
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanUpgrade(farm));
        sim.Schedule(0, new SandboxCheatCommand(0, 0, SandboxCheat.InstantBuild, true)); // the work done at once
        sim.Schedule(0, new UpgradeCommand(0, Array.Empty<int>(), farm.Id));
        RunUntil(sim, () => farm.Def.Id == "large_farm", 10, "the farm's upgrade");
        Assert.Equal("large_farm", farm.Def.Id);
        Assert.Equal((29, 29, 37, 37), (farm.X, farm.Z, farm.MaxX, farm.MaxZ));
        Assert.Equal(4, farm.Def.WorkerSlots);
        for (int x = 29; x <= 37; x++)
        for (int z = 29; z <= 37; z++)
            Assert.Equal(BlockType.Farmland, sim.World.Get(x, y - 1, z));
    }

    [Fact]
    public void AWarehouseCannotBeRaided()
    {
        var db = Flat().Content;
        Assert.True(db.Building("storehouse").Raidable);
        Assert.False(db.Building("warehouse").Raidable);
        Assert.True(db.Building("warehouse").Storage);
    }
}
