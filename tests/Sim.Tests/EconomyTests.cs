using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

public class EconomyTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Worker(Simulation sim, int owner, int x, int z) => sim.SpawnUnit(owner, "worker", x, z);

    /// <summary>Gives the player a tech without researching it.</summary>
    private static void Grant(Simulation sim, int owner, string tech) => sim.Economy.Grant(owner, sim.Content.Tech(tech));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>Steps until the condition holds; fails the test if it takes longer than maxTicks.</summary>
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

    /// <summary>A tree like the generator's: a trunk and a rounded crown of leaves.</summary>
    private static void Tree(VoxelWorld w, int x, int z, int trunk, BlockType wood = BlockType.Wood, BlockType leaves = BlockType.Leaves)
    {
        for (int y = 1; y <= trunk; y++) w.Set(x, Ground + y, z, wood);
        int top = Ground + trunk;
        for (int dy = -1; dy <= 1; dy++)
        {
            int r = dy == 1 ? 1 : 2;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) == r && Math.Abs(dz) == r && r == 2) continue;
                if (w.Get(x + dx, top + dy, z + dz) == BlockType.Air) w.Set(x + dx, top + dy, z + dz, leaves);
            }
        }
        w.Set(x, top + 2, z, leaves);
    }

    private static int CountBlocks(VoxelWorld w, BlockType b, int x0, int y0, int z0, int x1, int y1, int z1)
    {
        int n = 0;
        for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (w.Get(x, y, z) == b) n++;
        return n;
    }

    [Fact]
    public void WorkerDigsAStoneMoundAndCarriesItToTheStorehouse()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        TestWorlds.Box(sim.World, 30, Ground + 1, 30, 31, Ground + 2, 31, BlockType.Stone);
        int w = Worker(sim, 0, 20, 20);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 2, 30));
        int most = 0;
        RunUntil(sim, () =>
        {
            int i = Index(sim, w);
            most = Math.Max(most, sim.Units.CarryAmount[i]);
            return sim.Tick > 0 && sim.Units.Job[i] == UnitJob.None;
        }, 3000, "the worker to dig out the mound");

        Assert.Equal(0, CountBlocks(sim.World, BlockType.Stone, 30, Ground + 1, 30, 31, Ground + 2, 31));
        Assert.Equal(8, store.Stock[(int)Good.Stone]);
        Assert.Equal(8, sim.Economy.Player(0).Gathered[(int)Good.Stone]);
        Assert.Equal(0, sim.Units.CarryAmount[Index(sim, w)]);
        Assert.Equal(EconomySystem.CarryCapacity, most); // a full load on the first trip, then the rest
    }

    [Fact]
    public void ChoppingTopplesTheTreeAwayFromTheWorkerAndLeavesTheNeighboursCrown()
    {
        var sim = Flat();
        Place(sim, 0, "storehouse", 10, 10);
        Tree(sim.World, 30, 30, 4);
        Tree(sim.World, 30, 34, 5); // crowns touching
        int w = Worker(sim, 0, 26, 30);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 3, 30)); // a block halfway up the trunk

        RunUntil(sim, () => sim.Physics.FallingTrees.Count > 0, 2000, "the tree to be felled");
        var falling = sim.Physics.FallingTrees[0];
        Assert.Equal((1, 0), (falling.DirX, falling.DirZ)); // away from the worker, on its west
        Assert.Equal(4, falling.Logs);
        Assert.Equal(0, CountBlocks(sim.World, BlockType.Wood, 30, 0, 30, 30, 15, 30));
        // The felled crown is gone except where it reaches into the other tree's crown.
        Assert.Equal(0, CountBlocks(sim.World, BlockType.Leaves, 28, 0, 28, 32, 15, 31));
        Assert.True(CountBlocks(sim.World, BlockType.Leaves, 28, 0, 32, 32, 15, 36) >= 15);
        Assert.Equal(0, sim.Economy.Player(0).Gathered[(int)Good.Wood]); // felling gives nothing yet

        // It lands as a line of logs along the ground, one for each block of trunk.
        sim.Run(PhysicsSystem.TreeFallTicks);
        Assert.Empty(sim.Physics.FallingTrees);
        for (int x = 31; x <= 34; x++) Assert.Equal(BlockType.TreeRubble, sim.World.Get(x, Ground + 1, 30));
        Assert.Equal(4, CountBlocks(sim.World, BlockType.TreeRubble, 20, 0, 20, 40, 15, 40));
    }

    [Fact]
    public void AFelledTreeGivesTwiceItsTrunkInWoodAndTheWorkerMovesOnToTheNextTree()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        Tree(sim.World, 30, 30, 4);
        Tree(sim.World, 30, 36, 5);
        int w = Worker(sim, 0, 26, 30);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 1, 30));

        // The worker clears the rubble of the first tree before felling the next.
        RunUntil(sim, () => CountBlocks(sim.World, BlockType.TreeRubble, 20, 0, 20, 40, 15, 40) == 0
            && sim.Physics.FallingTrees.Count == 0 && sim.Tick > 100, 3000, "the first tree's rubble to be cleared");
        Assert.Equal(5, CountBlocks(sim.World, BlockType.Wood, 30, 0, 36, 30, 15, 36)); // the next tree still stands
        Assert.Equal(4 * Harvest.WoodPerTreeRubble, sim.Economy.Player(0).Gathered[(int)Good.Wood]);
        Assert.True(4 * Harvest.WoodPerTreeRubble > 4); // more than the trunk alone used to give

        RunUntil(sim, () => store.Stock[(int)Good.Wood] == 9 * Harvest.WoodPerTreeRubble, 4000, "both trees' wood to arrive");
        Assert.Equal(0, CountBlocks(sim.World, BlockType.Wood, 20, 0, 20, 40, 15, 40));
        Assert.Equal(0, CountBlocks(sim.World, BlockType.TreeRubble, 20, 0, 20, 40, 15, 40));
        Assert.Equal(0, CountBlocks(sim.World, BlockType.Leaves, 20, 0, 20, 40, 15, 40));
    }

    [Theory]
    [InlineData(BlockType.PineWood, BlockType.PineNeedles)]
    [InlineData(BlockType.BirchWood, BlockType.BirchLeaves)]
    [InlineData(BlockType.Wood, BlockType.WillowLeaves)]
    [InlineData(BlockType.Wood, BlockType.DuskLeaves)]
    [InlineData(BlockType.MushroomStem, BlockType.MushroomCap)]
    public void EveryKindOfTreeIsFelledAndClearedForWoodTheSameWay(BlockType wood, BlockType leaves)
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        Tree(sim.World, 30, 30, 5, wood, leaves);
        int w = Worker(sim, 0, 26, 30);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 3, 30));
        sim.Step();
        Assert.Equal(BlockType.Wood, sim.Units.JobBlock[Index(sim, w)]); // cutting wood, whatever the tree

        RunUntil(sim, () => sim.Physics.FallingTrees.Count > 0, 2000, "the tree to be felled");
        Assert.Equal(5, sim.Physics.FallingTrees[0].Logs);
        Assert.Equal(0, CountBlocks(sim.World, wood, 20, 0, 20, 40, 15, 40));
        Assert.Equal(0, CountBlocks(sim.World, leaves, 20, 0, 20, 40, 15, 40)); // its crown goes with it
        RunUntil(sim, () => store.Stock[(int)Good.Wood] == 5 * Harvest.WoodPerTreeRubble, 4000, "its wood to arrive");
        Assert.Equal(0, CountBlocks(sim.World, BlockType.TreeRubble, 20, 0, 20, 40, 15, 40));
    }

    [Fact]
    public void AWoodCutterMovesOnFromOneKindOfTreeToAnother()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        Tree(sim.World, 30, 30, 4);
        Tree(sim.World, 30, 36, 6, BlockType.PineWood, BlockType.PineNeedles);
        int w = Worker(sim, 0, 26, 30);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 1, 30));
        RunUntil(sim, () => store.Stock[(int)Good.Wood] == 10 * Harvest.WoodPerTreeRubble, 6000, "both trees' wood to arrive");
        Assert.Equal(0, CountBlocks(sim.World, BlockType.PineWood, 20, 0, 20, 40, 15, 40));
        Assert.Equal(0, CountBlocks(sim.World, BlockType.PineNeedles, 20, 0, 20, 40, 15, 40));
    }

    [Fact]
    public void WorkersSentToTreeRubbleClearItAsWoodCutters()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        for (int x = 30; x <= 32; x++) sim.World.Set(x, Ground + 1, 30, BlockType.TreeRubble);
        int w = Worker(sim, 0, 26, 30);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 1, 30));
        sim.Step();
        Assert.Equal(BlockType.Wood, sim.Units.JobBlock[Index(sim, w)]);
        RunUntil(sim, () => store.Stock[(int)Good.Wood] == 3 * Harvest.WoodPerTreeRubble, 3000, "the rubble's wood to arrive");
        Assert.Equal(0, CountBlocks(sim.World, BlockType.TreeRubble, 20, 0, 20, 40, 15, 40));
    }

    [Fact]
    public void FelledLogsFollowTheGroundAndHeapOverTheStumpWhereTheyMeetAWall()
    {
        var w = TestWorlds.Flat(32, 16, 32, Ground);
        var placed = new List<(int X, int Y, int Z)>();
        // A one-block step up two columns on, then a wall three high.
        TestWorlds.Box(w, 12, Ground + 1, 10, 13, Ground + 1, 10, BlockType.Soil);
        TestWorlds.Box(w, 14, Ground + 1, 10, 14, Ground + 3, 10, BlockType.Stone);
        Harvest.LayFelled(w, 10, Ground + 1, 10, 5, 1, 0, placed);
        Assert.Equal(5, placed.Count);
        Assert.Equal(BlockType.TreeRubble, w.Get(11, Ground + 1, 10));
        Assert.Equal(BlockType.TreeRubble, w.Get(12, Ground + 2, 10)); // up the step
        Assert.Equal(BlockType.TreeRubble, w.Get(13, Ground + 2, 10));
        Assert.Equal(BlockType.TreeRubble, w.Get(10, Ground + 1, 10)); // the rest heap over the stump
        Assert.Equal(BlockType.TreeRubble, w.Get(10, Ground + 2, 10));

        // Facing water, it turns to the first way with room; a deep drop stops the line.
        const int g = 8; // deep enough for a pit a log cannot drop into
        var v = TestWorlds.Flat(32, 16, 32, g);
        placed.Clear();
        v.Set(21, g, 20, BlockType.Water);
        TestWorlds.Box(v, 20, 1, 23, 20, g, 25, BlockType.Air); // a pit to the south, two columns on
        Harvest.LayFelled(v, 20, g + 1, 20, 4, 1, 0, placed);
        Assert.Equal(BlockType.Air, v.Get(21, g + 1, 20));
        Assert.Equal(BlockType.TreeRubble, v.Get(20, g + 1, 21)); // turned right: +X to +Z
        Assert.Equal(BlockType.TreeRubble, v.Get(20, g + 1, 22));
        Assert.Equal(BlockType.Air, v.Get(20, 1, 23));
        Assert.Equal(4, placed.Count);
        Assert.Equal(BlockType.TreeRubble, v.Get(20, g + 1, 20)); // two heaped over the stump
        Assert.Equal(BlockType.TreeRubble, v.Get(20, g + 2, 20));
    }

    [Fact]
    public void BerryBushesGiveFoodAndWorkersMoveOnToTheNextBush()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        for (int k = 0; k < 3; k++) sim.World.Set(24 + 2 * k, Ground + 1, 24, BlockType.BerryBush);
        int w = Worker(sim, 0, 20, 20);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 24, Ground + 1, 24));
        RunUntil(sim, () => sim.Tick > 0 && sim.Units.Job[Index(sim, w)] == UnitJob.None, 3000, "all bushes picked");
        Assert.Equal(3 * Harvest.FoodPerBush, store.Stock[(int)Good.Food]);
        Assert.Equal(0, CountBlocks(sim.World, BlockType.BerryBush, 20, Ground + 1, 20, 30, Ground + 1, 30));
    }

    [Fact]
    public void GoodsGoToTheNearestStorehouse()
    {
        var sim = Flat();
        var far = Place(sim, 0, "storehouse", 4, 4);
        var near = Place(sim, 0, "storehouse", 40, 30);
        TestWorlds.Box(sim.World, 34, Ground + 1, 34, 35, Ground + 1, 35, BlockType.Stone);
        int w = Worker(sim, 0, 20, 20);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 34, Ground + 1, 34));
        RunUntil(sim, () => sim.Tick > 0 && sim.Units.Job[Index(sim, w)] == UnitJob.None, 3000, "the stone to be stored");
        Assert.Equal(4, near.Stock[(int)Good.Stone]);
        Assert.Equal(0, far.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void AGroupSpreadsOverTheResource()
    {
        var sim = Flat();
        Place(sim, 0, "storehouse", 10, 10);
        TestWorlds.Box(sim.World, 30, Ground + 1, 30, 32, Ground + 1, 32, BlockType.Stone);
        var ids = new[] { Worker(sim, 0, 20, 20), Worker(sim, 0, 21, 20), Worker(sim, 0, 22, 20) };
        sim.Schedule(0, new GatherCommand(0, ids, 31, Ground + 1, 31));
        sim.Step();
        var targets = new HashSet<(int, int, int)>();
        foreach (int id in ids)
        {
            int i = Index(sim, id);
            Assert.Equal(UnitJob.Gather, sim.Units.Job[i]);
            targets.Add((sim.Units.JobX[i], sim.Units.JobY[i], sim.Units.JobZ[i]));
        }
        Assert.Equal(3, targets.Count);
        Assert.Contains((31, Ground + 1, 31), targets);
    }

    [Fact]
    public void AGathererThatCannotGetToItsBlocksGivesUp()
    {
        // A pit six blocks deep in deep ground: a worker on top can drop three at most, so it cannot get in,
        // and the pit's stone is all it could dig.
        const int ground = 10;
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64, ground));
        TestWorlds.Box(sim.World, 30, 5, 30, 33, ground, 33, BlockType.Air);
        int w = Worker(sim, 0, 20, 31);
        // The only places to stand at it are down in the pit, which it could not climb out of again: it does
        // not go, and gives the job up at once.
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, 4, 31));
        sim.Run(5);
        int i = Index(sim, w);
        Assert.Equal(UnitJob.None, sim.Units.Job[i]);
        sim.Run(60 * Tps);
        i = Index(sim, w);
        Assert.Equal(0, sim.Units.CarryAmount[i]);
        Assert.True(sim.Units.Y[i] > ground, "it stayed on top");
        Assert.Equal(16, CountBlocks(sim.World, BlockType.Stone, 30, 4, 30, 33, 4, 33)); // the pit floor is untouched
    }

    [Fact]
    public void AMoveOrderEndsTheJobButTheWorkerKeepsItsLoad()
    {
        var sim = Flat();
        Place(sim, 0, "storehouse", 10, 10);
        TestWorlds.Box(sim.World, 40, Ground + 1, 40, 41, Ground + 1, 41, BlockType.Stone);
        int w = Worker(sim, 0, 38, 38);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 40, Ground + 1, 40));
        RunUntil(sim, () => sim.Units.CarryAmount[Index(sim, w)] == 2, 1000, "two blocks dug");
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { w }, 50, 50));
        sim.Run(5);
        int i = Index(sim, w);
        Assert.Equal(UnitJob.None, sim.Units.Job[i]);
        Assert.Equal(UnitOrder.Move, sim.Units.Order[i]);
        Assert.Equal(2, sim.Units.CarryAmount[i]);
        Assert.Equal(Good.Stone, sim.Units.Carry[i]);
    }

    [Fact]
    public void RoadsDoubleWalkingSpeed()
    {
        int Walk(bool road)
        {
            var sim = Flat();
            var store = Place(sim, 0, "storehouse", 4, 40);
            store.Stock[(int)Good.Stone] = 100;
            Grant(sim, 0, "roads");
            if (road) sim.Schedule(0, new RoadCommand(0, 10, 20, 50, 20));
            int u = sim.SpawnUnit(0, "spearman", 10, 20);
            sim.Schedule(1, new MoveCommand(0, new[] { u }, 50, 20));
            sim.Run(2);
            if (road)
            {
                Assert.Equal(BlockType.Road, sim.World.Get(30, Ground, 20));
                Assert.Equal(59, store.Stock[(int)Good.Stone]); // 41 road blocks at one stone each
            }
            return RunUntil(sim, () => sim.Units.Order[Index(sim, u)] == UnitOrder.Idle, 1000, "the walk");
        }
        int plain = Walk(false), onRoad = Walk(true);
        Assert.InRange(onRoad * 100 / plain, 45, 56);
    }

    [Fact]
    public void UnitsTakeARoadDetourWhenItIsFaster()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 4, 50);
        store.Stock[(int)Good.Stone] = 100;
        Grant(sim, 0, "roads");
        // Straight across is 40 blocks; the road goes 6 north, 40 east and 6 back: 52 blocks at double speed.
        sim.Schedule(0, new RoadCommand(0, 10, 30, 10, 24));
        sim.Schedule(0, new RoadCommand(0, 10, 24, 50, 24));
        sim.Schedule(0, new RoadCommand(0, 50, 24, 50, 30));
        int u = sim.SpawnUnit(0, "spearman", 10, 30);
        sim.Schedule(1, new MoveCommand(0, new[] { u }, 50, 30));
        sim.Run(2);
        RunUntil(sim, () => sim.Units.X[Index(sim, u)] >= Fix64.FromInt(30), 400, "the unit to get halfway");
        Assert.Equal(24, sim.Units.Z[Index(sim, u)].FloorToInt());
        int ticks = RunUntil(sim, () => sim.Units.Order[Index(sim, u)] == UnitOrder.Idle, 400, "arrival");
        Assert.True(sim.Tick < 170, $"took {sim.Tick} ticks; straight across would take about 200");
    }

    [Fact]
    public void RoadsStopWhenTheStoneRunsOut()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 4, 40);
        store.Stock[(int)Good.Stone] = 5;
        Grant(sim, 0, "roads");
        sim.Schedule(0, new RoadCommand(0, 10, 20, 30, 20));
        sim.Step();
        Assert.Equal(5, CountBlocks(sim.World, BlockType.Road, 0, 0, 0, 63, 15, 63));
        Assert.Equal(0, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void SmelterTurnsTwoOreAndOneCoalIntoAnIngot()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.IronOre] = 4;
        store.Stock[(int)Good.Coal] = 2;
        var smelter = Place(sim, 0, "smelter", 24, 10);
        int w = Worker(sim, 0, 18, 18);
        sim.Schedule(0, new WorkCommand(0, new[] { w }, smelter.Id));
        RunUntil(sim, () => store.Stock[(int)Good.Iron] == 2, 4000, "two ingots stored");
        Assert.Equal(0, store.Stock[(int)Good.IronOre]);
        Assert.Equal(0, store.Stock[(int)Good.Coal]);
        Assert.Equal(0, smelter.Stock[(int)Good.IronOre]);
        Assert.Equal(0, smelter.Stock[(int)Good.Coal]);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);
        // Nothing left to smelt: the hauler waits by the smelter rather than leaving.
        sim.Run(200);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);
        Assert.Equal(2, store.Stock[(int)Good.Iron]);
    }

    [Fact]
    public void FarmersBringFoodFromTheFarm()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        var farm = Place(sim, 0, "farm", 24, 10);
        Assert.Equal(36, CountBlocks(sim.World, BlockType.Farmland, 24, Ground, 10, 29, Ground, 15));
        var ids = new[] { Worker(sim, 0, 18, 20), Worker(sim, 0, 19, 20), Worker(sim, 0, 20, 20) };
        sim.Schedule(0, new WorkCommand(0, ids, farm.Id));
        sim.Step();
        // Two places to work on a farm: the third worker is not taken on.
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, ids[2])]);
        RunUntil(sim, () => sim.Economy.Player(0).Gathered[(int)Good.Food] >= 4 * EconomySystem.FarmYield, 3000, "four harvests");
        var p = sim.Economy.Player(0);
        int carried = 0;
        foreach (int id in ids) carried += sim.Units.CarryAmount[Index(sim, id)];
        Assert.Equal(p.Gathered[(int)Good.Food] - p.Eaten[(int)Good.Food], store.Stock[(int)Good.Food] + carried);
    }

    [Fact]
    public void FoodPerMinuteIsTheLastMinutesFoodLessUpkeep()
    {
        var sim = Flat();
        Place(sim, 0, "storehouse", 10, 10);
        var farm = Place(sim, 0, "farm", 24, 10);
        var ids = new[] { Worker(sim, 0, 18, 20), Worker(sim, 0, 19, 20) };
        sim.Step();
        int upkeep = sim.Economy.Upkeep(0);
        Assert.True(upkeep > 0);
        // Nothing gathered yet: only what the people eat.
        Assert.Equal(-upkeep, sim.Economy.FoodPerMinute(0));

        sim.Schedule(sim.Tick, new WorkCommand(0, ids, farm.Id));
        sim.Run(2 * 60 * Tps); // farmers working for a while
        while (sim.Tick % Tps != 1) sim.Step(); // just past a whole second, when the minute is counted
        var p = sim.Economy.Player(0);
        int before = p.Gathered[(int)Good.Food];
        sim.Run(60 * Tps);
        int gained = p.Gathered[(int)Good.Food] - before;
        Assert.True(gained > 0, "the farm brings food in");
        Assert.Equal(gained - sim.Economy.Upkeep(0), sim.Economy.FoodPerMinute(0));
    }

    [Fact]
    public void WorkersBuildAHouseLayerByLayerAndItRaisesHousing()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Wood] = 100;
        Assert.Equal(25, sim.Economy.Housing(0));
        var ids = new[] { Worker(sim, 0, 20, 20), Worker(sim, 0, 21, 20) };
        var house = sim.Content.Building("house");
        sim.Schedule(0, new BuildCommand(0, ids, "house", 30, 30));
        sim.Step();
        Assert.Equal(70, hall.Stock[(int)Good.Wood]);
        var site = Assert.Single(sim.Buildings.All, b => b.Def == house);
        Assert.Equal(BuildingState.Site, site.State);

        RunUntil(sim, () => site.Placed > 0, 600, "the first blocks");
        Assert.Equal(0, CountBlocks(sim.World, BlockType.Thatch, 30, 0, 30, 33, 15, 33)); // walls before the roof
        RunUntil(sim, () => site.Active, 1000, "the house to be finished");
        int built = (int)sim.Tick;
        sim.Step();
        Assert.Equal(30, sim.Economy.Housing(0));
        foreach (var t in house.Blocks)
            Assert.Equal(t.Block, sim.World.Get(30 + t.X, site.Y + t.Y, 30 + t.Z));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, ids[0])]);
        // Two workers share the work: about half the time one would take, plus the walk.
        Assert.InRange(built, house.BuildTicks / 2, house.BuildTicks / 2 + 200);
    }

    [Fact]
    public void BuildingsNeedLevelClearGroundAndTheirCost()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var econ = sim.Economy;
        var house = sim.Content.Building("house");
        Assert.Equal(PlaceResult.CannotAfford, econ.CanPlace(0, house, 30, 30, out _));
        hall.Stock[(int)Good.Wood] = 30;
        Assert.Equal(PlaceResult.Ok, econ.CanPlace(0, house, 30, 30, out int baseY));
        Assert.Equal(Ground + 1, baseY);
        Assert.Equal(PlaceResult.Crowded, econ.CanPlace(0, house, 17, 12, out _)); // touching the town hall
        Assert.Equal(PlaceResult.OutOfBounds, econ.CanPlace(0, house, 61, 30, out _));
        // A one-block step is levelled; two blocks is too steep.
        TestWorlds.Box(sim.World, 30, Ground + 1, 30, 31, Ground + 1, 33, BlockType.Soil);
        Assert.Equal(PlaceResult.Ok, econ.CanPlace(0, house, 30, 30, out _));
        TestWorlds.Box(sim.World, 30, Ground + 2, 30, 30, Ground + 2, 30, BlockType.Soil);
        Assert.Equal(PlaceResult.Uneven, econ.CanPlace(0, house, 30, 30, out _));
        Tree(sim.World, 42, 42, 4);
        Assert.Equal(PlaceResult.Uneven, econ.CanPlace(0, house, 40, 40, out _)); // nowhere to stand in a trunk
        sim.World.Set(50, Ground + 1, 50, BlockType.BerryBush);
        Assert.Equal(PlaceResult.Blocked, econ.CanPlace(0, house, 49, 49, out _));
        // A puddle in the ground is filled in. Deep water has no ground to level, and water over the ground
        // the house stands on is in the way.
        sim.World.Set(52, Ground, 22, BlockType.Water);
        Assert.Equal(PlaceResult.Ok, econ.CanPlace(0, house, 51, 21, out _));
        sim.World.Set(52, Ground + 1, 22, BlockType.Water);
        Assert.Equal(PlaceResult.Uneven, econ.CanPlace(0, house, 51, 21, out _));
        sim.World.Set(52, Ground, 22, BlockType.Grass);
        Assert.Equal(PlaceResult.Blocked, econ.CanPlace(0, house, 51, 21, out _));
    }

    [Fact]
    public void TrainingWaitsForHousing()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 200;
        for (int k = 0; k < 24; k++) sim.SpawnUnit(0, "spearman", 30 + k % 12, 30 + k / 12 * 2);
        for (int k = 0; k < 3; k++) sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Step();
        Assert.Equal(110, hall.Stock[(int)Good.Food]);
        Assert.Equal(3, hall.Queue.Count);
        sim.Run(3 * 200 + 10);
        Assert.Equal(25, sim.Economy.Population(0)); // the town hall houses 25
        Assert.Equal(2, hall.Queue.Count);
        Place(sim, 0, "house", 30, 40);
        sim.Run(2 * 200 + 10);
        Assert.Equal(27, sim.Economy.Population(0));
        Assert.Empty(hall.Queue);
        // A new worker steps out in front of the door.
        int newest = sim.Units.Count - 1;
        Assert.Equal(UnitRole.Worker, sim.Content.Units[sim.Units.Def[newest]].Role);
        Assert.True(hall.ColumnDistance(sim.Units.X[newest].FloorToInt(), sim.Units.Z[newest].FloorToInt()) <= 2);
    }

    /// <summary>Issue #68: the town hall houses 25, so the first new units need no houses; a second hall houses 25 more.</summary>
    [Fact]
    public void TheTownHallHousesTwentyFiveWithNoHouses()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        Assert.Equal(25, sim.Economy.Housing(0));
        hall.Stock[(int)Good.Food] = 1000;
        for (int k = 0; k < 21; k++) sim.SpawnUnit(0, "spearman", 30 + k % 11, 30 + k / 11 * 2);
        for (int k = 0; k < EconomySystem.MaxQueue; k++) sim.Schedule(0, new TrainCommand(0, hall.Id, "worker"));
        sim.Run(5 * 200 + 10);
        Assert.Equal(25, sim.Economy.Population(0)); // four trained with no houses, the fifth waits
        Assert.Single(hall.Queue);
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def.Id == "house");

        Place(sim, 0, "town_hall", 40, 10); // a later town hall houses 25 too
        Assert.Equal(50, sim.Economy.Housing(0));
        sim.Run(200 + 10);
        Assert.Equal(26, sim.Economy.Population(0));
        Assert.Empty(hall.Queue);
    }

    [Fact]
    public void UnitsEatEveryMinuteAndHungerStopsTraining()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        hall.Stock[(int)Good.Food] = 5;
        for (int k = 0; k < 3; k++) sim.SpawnUnit(0, "spearman", 30 + k, 30);
        sim.Run(EconomySystem.UpkeepInterval + 1);
        Assert.Equal(2, hall.Stock[(int)Good.Food]);
        Assert.False(sim.Economy.Starving(0));
        sim.Run(EconomySystem.UpkeepInterval);
        Assert.Equal(0, hall.Stock[(int)Good.Food]);
        Assert.True(sim.Economy.Starving(0));
        Assert.Equal(5, sim.Economy.Player(0).Eaten[(int)Good.Food]);

        hall.Stock[(int)Good.Food] = 30;
        sim.Schedule(sim.Tick, new TrainCommand(0, hall.Id, "worker"));
        sim.Run(400);
        Assert.Equal(3, sim.Economy.Population(0)); // paid for, but nobody is trained while people go hungry
        Assert.Single(hall.Queue);
        hall.Stock[(int)Good.Food] = 100;
        RunUntil(sim, () => sim.Economy.Population(0) == 4, 2000, "training to resume once everyone is fed");
        Assert.False(sim.Economy.Starving(0));
    }

    [Fact]
    public void AnUndefendedStorehouseIsCapturedWithItsGoods()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 50;
        int raider = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(0, new CaptureCommand(1, new[] { raider }, store.Id));
        RunUntil(sim, () => store.Owner == 1, 1000, "the capture");
        Assert.Equal(50, sim.Economy.Stock(1, Good.Wood));
        Assert.Equal(0, sim.Economy.Stock(0, Good.Wood));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, raider)]);
    }

    [Fact]
    public void ADefenderNearbyStopsTheRaid()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        int raider = sim.SpawnUnit(1, "spearman", 40, 40);
        // Within eight columns. A worker, which stands its ground without fighting: a soldier would also
        // attack the raider (see CombatTests).
        int guard = sim.SpawnUnit(0, "worker", 20, 12);
        sim.Schedule(0, new CaptureCommand(1, new[] { raider }, store.Id));
        sim.Run(EconomySystem.CaptureTicks + 400);
        Assert.Equal(0, store.Owner);
        Assert.Equal(UnitJob.Capture, sim.Units.Job[Index(sim, raider)]);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { guard }, 50, 50));
        RunUntil(sim, () => store.Owner == 1, EconomySystem.CaptureTicks + 400, "the capture once the guard left");
    }

    [Fact]
    public void BurningAStorehouseDestroysItAndItsGoods()
    {
        var sim = Flat();
        var store = Place(sim, 0, "storehouse", 10, 10);
        store.Stock[(int)Good.Food] = 80;
        var ids = new[] { sim.SpawnUnit(1, "spearman", 40, 40), sim.SpawnUnit(1, "rider", 41, 40) };
        sim.Schedule(0, new BurnCommand(1, ids, store.Id));
        RunUntil(sim, () => sim.Buildings.Get(store.Id) == null, 1000, "the fire");
        Assert.Equal(0, sim.Economy.Stock(0, Good.Food));
        Assert.Equal(0, sim.Economy.Stock(1, Good.Food));
        Assert.Equal(0, CountBlocks(sim.World, BlockType.Planks, 10, 0, 10, 14, 15, 14));
        Assert.Equal(0, CountBlocks(sim.World, BlockType.RoofTile, 10, 0, 10, 14, 15, 14));
    }

    [Fact]
    public void OnlyStorehousesCanBeRaided()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 10, 10);
        int raider = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(0, new CaptureCommand(1, new[] { raider }, hall.Id));
        sim.Schedule(0, new BurnCommand(1, new[] { raider }, hall.Id));
        sim.Step();
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, raider)]);
    }

    [Fact]
    public void EconomyCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100, StartUnits = 3, StartBuildings = true };
        log.Add(1, new GatherCommand(0, new[] { 1, 2 }, 30, 12, 41));
        log.Add(2, new BuildCommand(0, new[] { 3 }, "house", 60, 70));
        log.Add(3, new WorkCommand(1, new[] { 5, 6 }, 7));
        log.Add(4, new CaptureCommand(1, new[] { 8 }, 2));
        log.Add(4, new BurnCommand(0, new[] { 9 }, 3));
        log.Add(5, new RoadCommand(0, 10, 10, 30, 12));
        log.Add(6, new TrainCommand(1, 4, "worker"));
        var copy = CommandLog.Parse(log.ToString());
        Assert.True(copy.StartBuildings);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(log.ToString(), copy.ToString());
        Assert.Throws<FormatException>(() => CommandLog.Parse(log.ToString().Replace("house", "big house")));
    }

    /// <summary>
    /// Issue #8's check: five workers gather for five minutes from the real start area of a generated
    /// region, and every good is accounted for (stored, carried or eaten).
    /// </summary>
    [Fact]
    public void FiveWorkersForFiveMinutesFillTheStockpile()
    {
        var (first, totals) = RunFiveWorkers();
        var (second, _) = RunFiveWorkers();
        Assert.Equal(first.StateHash(), second.StateHash());

        var p = first.Economy.Player(0);
        var start = new int[Goods.Count];
        foreach (var g in EconomySystem.StartingGoods) start[(int)g.Good] = g.Amount;
        foreach (var good in Goods.All)
        {
            int k = (int)good;
            Assert.Equal(start[k] + p.Gathered[k] - p.Spent[k] - p.Eaten[k], totals[k]);
        }
        // Two on wood, one each on stone, iron ore and berries. Measured (wood 67, and the start's stone
        // outcrop, ore outcrop and berry patch dug or picked clean: 10, 6 and 30), then set a little below.
        Assert.InRange(p.Gathered[(int)Good.Wood], 55, 200);
        Assert.InRange(p.Gathered[(int)Good.Stone], 8, 60);
        Assert.InRange(p.Gathered[(int)Good.IronOre], 5, 60);
        Assert.InRange(p.Gathered[(int)Good.Food], 25, 30);
        Assert.Equal(4 * 5, p.Eaten[(int)Good.Food]); // five workers, upkeep at minutes one to four
    }

    private static (Simulation Sim, int[] Totals) RunFiveWorkers()
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        var start = sim.Map.Starts[0];
        var hall = Assert.Single(sim.Buildings.All, b => b.Owner == 0);
        var w = new int[5];
        for (int k = 0; k < 5; k++) w[k] = sim.SpawnUnit(0, "worker", start.X - 2 + k, hall.MaxZ + 2);

        var wood = Nearest(sim, BlockType.Wood, start);
        var stone = Nearest(sim, BlockType.Stone, start);
        var ore = Nearest(sim, BlockType.IronOre, start);
        var bush = Nearest(sim, BlockType.BerryBush, start);
        sim.Schedule(0, new GatherCommand(0, new[] { w[0], w[1] }, wood.X, wood.Y, wood.Z));
        sim.Schedule(0, new GatherCommand(0, new[] { w[2] }, stone.X, stone.Y, stone.Z));
        sim.Schedule(0, new GatherCommand(0, new[] { w[3] }, ore.X, ore.Y, ore.Z));
        sim.Schedule(0, new GatherCommand(0, new[] { w[4] }, bush.X, bush.Y, bush.Z));
        sim.Run(5 * 60 * Simulation.TicksPerSecond);

        var totals = sim.Economy.Stockpile(0);
        foreach (int id in w)
        {
            int i = sim.Units.IndexOf(id);
            totals[(int)sim.Units.Carry[i]] += sim.Units.CarryAmount[i];
        }
        return (sim, totals);
    }

    /// <summary>The block of that type above ground nearest to the start (a start-area resource).</summary>
    private static (int X, int Y, int Z) Nearest(Simulation sim, BlockType type, World.StartArea start)
    {
        var w = sim.World;
        var best = (X: -1, Y: -1, Z: -1);
        long bestD = long.MaxValue;
        for (int z = start.Z - 40; z <= start.Z + 40; z++)
        for (int x = start.X - 40; x <= start.X + 40; x++)
        for (int y = start.Y + 1; y <= start.Y + 4; y++)
        {
            if (w.Get(x, y, z) != type) continue;
            long d = (long)(x - start.X) * (x - start.X) + (long)(z - start.Z) * (z - start.Z);
            if (d >= bestD) continue;
            bestD = d;
            best = (x, y, z);
        }
        Assert.True(best.X >= 0, $"no {type} near the start");
        return best;
    }
}
