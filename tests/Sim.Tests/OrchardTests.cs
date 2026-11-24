using System;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Orchards (Crop rotation, issue #148): fruit trees that grow, bear food with nobody working them and screen sight.</summary>
public class OrchardTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static Simulation Flat(bool fog = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        sim.Fog = fog;
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    /// <summary>An orchard at (22, 22), its trees grown.</summary>
    private static Building Grown(Simulation sim, int owner = 0)
    {
        var orchard = Place(sim, owner, "orchard", 22, 22);
        sim.Run(orchard.Def.GrowTicks + Tps);
        Assert.True(orchard.Grown);
        return orchard;
    }

    private static int Trees(Simulation sim, Building b)
    {
        int n = 0;
        for (int y = b.Y; y <= b.Y + 3; y++)
        for (int z = b.Z; z <= b.MaxZ; z++)
        for (int x = b.X; x <= b.MaxX; x++)
            if (sim.World.Get(x, y, z) == BlockType.Orchard) n++;
        return n;
    }

    [Fact]
    public void CropRotationGivesOrchardsAndNoLongerAddsToHarvests()
    {
        var db = ContentDb.Default;
        var orchard = db.Building("orchard");
        Assert.Equal(BuildingKind.Orchard, orchard.Kind);
        Assert.Equal((6, 6), (orchard.SizeX, orchard.SizeZ));
        Assert.Equal("crop_rotation", Assert.Single(orchard.Requires).Id);
        Assert.Equal(240 * Tps, orchard.GrowTicks);
        Assert.Equal(new GoodAmount(Good.Food, 10), Assert.Single(orchard.Yield));
        Assert.Equal(30 * Tps, orchard.YieldTicks);
        Assert.True(orchard.Raidable);
        Assert.True(orchard.Walkable);
        Assert.DoesNotContain(db.Tech("crop_rotation").Effects, e => e.Type == TechEffectType.FarmYield);
    }

    [Fact]
    public void AnOrchardIsPlantedAsSaplingsThatGrowIntoTrees()
    {
        var sim = Flat();
        var orchard = Place(sim, 0, "orchard", 22, 22);
        // Saplings at the four trunks, nothing over them yet.
        Assert.False(orchard.Grown);
        Assert.Equal(4, Trees(sim, orchard));
        Assert.Equal(BlockType.Orchard, sim.World.Get(23, Ground + 1, 23));
        Assert.Equal(BlockType.Air, sim.World.Get(23, Ground + 2, 23));
        sim.Run(orchard.Def.GrowTicks / 2);
        Assert.InRange(sim.Economy.GrowthPercent(orchard), 45, 55);
        Assert.False(orchard.Grown);
        sim.Run(orchard.Def.GrowTicks / 2 + Tps);
        Assert.True(orchard.Grown);
        Assert.Equal(100, sim.Economy.GrowthPercent(orchard));
        // The crowns fill the footprint at head height.
        for (int z = orchard.Z; z <= orchard.MaxZ; z++)
        for (int x = orchard.X; x <= orchard.MaxX; x++)
            Assert.Equal(BlockType.Orchard, sim.World.Get(x, Ground + 2, z));
        Assert.True(Trees(sim, orchard) > 36);
    }

    [Fact]
    public void AGrownOrchardBringsInFoodWithNoWorkerAssigned()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 4, 4);
        int worker = sim.SpawnUnit(0, "worker", 14, 14);
        sim.Schedule(0, new AutomateCommand(0, new[] { worker }, true));
        var orchard = Place(sim, 0, "orchard", 22, 22);
        int food = hall.Stock[(int)Good.Food];
        // Growing, it bears nothing.
        sim.Run(orchard.Def.GrowTicks - 2 * Tps);
        Assert.Equal(0, orchard.Stock[(int)Good.Food]);
        Assert.Equal(food, hall.Stock[(int)Good.Food]);
        // Grown, nobody working it: baskets come, and the automatic worker carries them in.
        sim.Run(4 * orchard.Def.YieldTicks);
        Assert.True(hall.Stock[(int)Good.Food] >= food + 30, $"food in storage: {hall.Stock[(int)Good.Food] - food}");
        Assert.True(sim.Economy.Player(0).Gathered[(int)Good.Food] >= 30);
    }

    [Fact]
    public void BasketsWaitUntilCarriedAndThenTheTreesStopBearing()
    {
        var sim = Flat();
        Place(sim, 0, "town_hall", 4, 4);
        var orchard = Grown(sim);
        sim.Run((EconomySystem.OrchardBaskets + 3) * orchard.Def.YieldTicks);
        Assert.Equal(EconomySystem.OrchardBaskets, EconomySystem.Baskets(orchard));
        Assert.Equal(EconomySystem.OrchardBaskets * 10, orchard.Stock[(int)Good.Food]);
        // A worker sent there carries them to storage.
        int food = sim.Economy.Stock(0, Good.Food);
        int worker = sim.SpawnUnit(0, "worker", 20, 20);
        sim.Schedule(sim.Tick, new WorkCommand(0, new[] { worker }, orchard.Id));
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, worker)]);
        sim.Run(90 * Tps);
        Assert.True(sim.Economy.Stock(0, Good.Food) >= food + 30, $"{sim.Economy.Stock(0, Good.Food) - food}");
    }

    [Fact]
    public void ACartCarriesAnOrchardsFruitToStorage()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 4, 4);
        var orchard = Grown(sim);
        sim.Run(3 * orchard.Def.YieldTicks);
        int food = hall.Stock[(int)Good.Food];
        int baskets = orchard.Stock[(int)Good.Food];
        Assert.True(baskets >= 20);
        int cart = sim.SpawnUnit(0, "cart", 20, 20);
        sim.Units.Auto[Index(sim, cart)] = true;
        sim.Run(60 * Tps);
        Assert.True(hall.Stock[(int)Good.Food] >= food + baskets, $"carried: {hall.Stock[(int)Good.Food] - food} of {baskets}");
    }

    [Fact]
    public void AGrownOrchardBlocksShotsThroughIt()
    {
        var sim = Flat();
        var orchard = Place(sim, 2, "orchard", 22, 22);
        var c = sim.Combat;
        Fix64 Mid(int v) => Fix64.FromInt(v) + Fix64.Half;
        int stand = Ground + 1;
        Assert.True(c.ClearLine(Mid(20), stand, Mid(25), Mid(29), stand, Mid(25)), "over saplings");
        sim.Run(orchard.Def.GrowTicks + Tps);
        Assert.False(c.ClearLine(Mid(20), stand, Mid(25), Mid(29), stand, Mid(25)), "through the trees");
        Assert.True(c.ClearLine(Mid(20), stand, Mid(20), Mid(29), stand, Mid(20)), "past them");
        // From high over the trees the shot goes over them.
        Assert.True(c.ClearLine(Mid(20), stand + 4, Mid(25), Mid(29), stand + 4, Mid(25)), "over the top");
    }

    [Fact]
    public void AUnitBehindAGrownOrchardCannotBeSeenFromTheOtherSide()
    {
        var sim = Flat(fog: true);
        var orchard = Place(sim, 2, "orchard", 22, 22);
        sim.SpawnUnit(0, "archer", 20, 25); // sees 10 columns
        sim.Run(VisionSystem.UpdateTicks);
        // Saplings hide nothing.
        Assert.True(sim.Vision.Sees(0, 29, 25));
        Assert.True(sim.Vision.Sees(0, 26, 25));
        sim.Run(orchard.Def.GrowTicks + Tps);
        Assert.True(orchard.Grown);
        Assert.False(sim.Vision.Sees(0, 29, 25), "behind the orchard");
        Assert.False(sim.Vision.Sees(0, 26, 25), "deep in it");
        Assert.True(sim.Vision.Sees(0, 23, 25), "at its edge");
        Assert.True(sim.Vision.Sees(0, 24, 17), "beside it");
        Assert.True(sim.Vision.Sees(0, 15, 25), "the other way");
        // A unit standing behind it is not seen there.
        int hidden = sim.SpawnUnit(1, "worker", 29, 25);
        sim.Run(VisionSystem.UpdateTicks);
        Assert.False(sim.Vision.SeesUnit(0, Index(sim, hidden)));
    }

    [Fact]
    public void UnitsPushThroughAnOrchardSlowly()
    {
        int Crossing(bool grown)
        {
            // Stone from wall to wall either side of the orchard: the only way east is through it.
            var w = TestWorlds.Flat(64, 16, 64, Ground);
            TestWorlds.Box(w, 20, Ground + 1, 0, 29, Ground + 3, 21, BlockType.Stone);
            TestWorlds.Box(w, 20, Ground + 1, 28, 29, Ground + 3, 63, BlockType.Stone);
            var sim = TestWorlds.Sim(w);
            var orchard = Place(sim, 0, "orchard", 22, 22);
            if (grown) sim.Run(orchard.Def.GrowTicks + Tps);
            int id = sim.SpawnUnit(0, "spearman", 21, 25);
            sim.Schedule(sim.Tick, new MoveCommand(0, new[] { id }, 29, 25));
            // The ticks it takes to cross the middle of the orchard, from column 23 to column 26.
            long start = sim.Tick;
            int inside = 0;
            while (sim.Units.X[Index(sim, id)].FloorToInt() < 28 && sim.Tick - start < 60 * Tps)
            {
                sim.Step();
                int x = sim.Units.X[Index(sim, id)].FloorToInt();
                if (x >= 23 && x < 26) inside++;
            }
            Assert.InRange(sim.Units.X[Index(sim, id)].FloorToInt(), 28, 29);
            return inside;
        }
        int open = Crossing(false), through = Crossing(true);
        Assert.True(through > open * 3 / 2, $"through the trees {through} ticks, over saplings {open}");
    }

    [Fact]
    public void UnitsGoRoundAGrownOrchardWhenThatIsQuicker()
    {
        var sim = Flat();
        var orchard = Place(sim, 0, "orchard", 22, 22);
        sim.Run(orchard.Def.GrowTicks + Tps);
        int id = sim.SpawnUnit(0, "spearman", 21, 23);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { id }, 29, 23));
        int inTrees = 0;
        long start = sim.Tick;
        while (sim.Units.X[Index(sim, id)].FloorToInt() < 28 && sim.Tick - start < 60 * Tps)
        {
            sim.Step();
            int i = Index(sim, id), x = sim.Units.X[i].FloorToInt(), z = sim.Units.Z[i].FloorToInt();
            if (sim.World.Get(x, sim.Units.Y[i] + 1, z) == BlockType.Orchard) inTrees++;
        }
        Assert.True(sim.Units.X[Index(sim, id)].FloorToInt() >= 28, "it got there");
        Assert.True(inTrees <= 5, $"it went round the trees (at most clipping a corner): {inTrees} ticks in them");
    }

    [Fact]
    public void ABurntOrchardGivesNothingUntilItRegrows()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 4, 4);
        var orchard = Grown(sim);
        sim.Run(2 * orchard.Def.YieldTicks);
        Assert.True(orchard.Stock[(int)Good.Food] > 0);
        int raider = sim.SpawnUnit(1, "spearman", 40, 40);
        sim.Schedule(sim.Tick, new BurnCommand(1, new[] { raider }, orchard.Id));
        long start = sim.Tick;
        while (orchard.Grown && sim.Tick - start < 60 * Tps) sim.Step();
        Assert.False(orchard.Grown);
        Assert.NotNull(sim.Buildings.Get(orchard.Id)); // cut down, not gone
        Assert.Equal(0, orchard.Owner);
        Assert.Equal(0, orchard.Stock[(int)Good.Food]);
        Assert.Equal(4, Trees(sim, orchard)); // saplings again
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { raider }, 50, 50));
        var gathered = sim.Economy.Player(0).Gathered[(int)Good.Food];
        sim.Run(orchard.Def.GrowTicks - 5 * Tps);
        Assert.Equal(0, orchard.Stock[(int)Good.Food]);
        Assert.Equal(gathered, sim.Economy.Player(0).Gathered[(int)Good.Food]);
        sim.Run(5 * Tps + 2 * orchard.Def.YieldTicks);
        Assert.True(orchard.Grown);
        Assert.True(orchard.Stock[(int)Good.Food] > 0);
        _ = hall;
    }

    [Fact]
    public void AnOrchardThatIsDestroyedTakesItsTreesWithIt()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        sim.TestMode = true;
        var orchard = Grown(sim);
        Assert.True(Trees(sim, orchard) > 0);
        sim.Schedule(sim.Tick, new SandboxClearCommand(0, 25, 25, 2));
        sim.Step();
        Assert.Null(sim.Buildings.Get(orchard.Id));
        Assert.Equal(0, Trees(sim, orchard));
    }
}
