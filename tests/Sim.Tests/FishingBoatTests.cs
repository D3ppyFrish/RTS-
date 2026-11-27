using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Fishing boats: built at a dock, they fish out on open water and bring the catch home, again and again.</summary>
public class FishingBoatTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3;
    private const int LakeX0 = 30, LakeX1 = 60; // a deep lake from x = 30 to 60, right along z

    /// <summary>Flat ground with a deep lake east of x = 30; a storehouse, a house and a dock on the west shore.</summary>
    private static (Simulation Sim, Building Store, Building Dock) Harbor(int lakeX1 = LakeX1)
    {
        var w = TestWorlds.Flat(80, 16, 64, Ground);
        TestWorlds.Box(w, LakeX0, 1, 0, lakeX1, Ground, 63, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 10)!;
        foreach (var g in new[] { Good.Wood, Good.Planks, Good.Food }) store.Stock[(int)g] = 500;
        sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 10, 20);
        var dock = sim.Economy.PlaceFinished(0, sim.Content.Building("dock"), 25, 20)!;
        Assert.NotNull(dock);
        return (sim, store, dock);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int[] Boats(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Where(i => sim.Content.Units[sim.Units.Def[i]].IsFisher).Select(i => sim.Units.Id[i]).ToArray();

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
    public void TheFishingBoatIsABoatWithACatchAndNoSeats()
    {
        var def = new Simulation(new World.RegionMap(1, TestWorlds.Flat())).Content.Unit("fishing_boat");
        Assert.True(def.IsBoat);
        Assert.True(def.IsFisher);
        Assert.Equal(0, def.Seats);
        Assert.Equal(12, def.Catch);
        Assert.Equal(20 * Tps, def.FishTicks);
        Assert.Equal("dock", def.TrainedAt!.Id);
    }

    [Fact]
    public void ADockBuildsAFishingBoatThatFishesOpenWaterAndBringsTheCatchHome()
    {
        var (sim, store, dock) = Harbor();
        sim.Schedule(0, new TrainCommand(0, dock.Id, "fishing_boat"));
        RunUntil(sim, () => Boats(sim).Length == 1, 60 * Tps, "the dock to build a fishing boat");
        int boat = Boats(sim)[0];
        int i = Index(sim, boat);
        Assert.Equal(UnitJob.Fish, sim.Units.Job[i]);
        int food = store.Stock[(int)Good.Food];

        // It sails out to open water and fishes there.
        RunUntil(sim, () => sim.Units.Phase[Index(sim, boat)] == JobPhase.Working, 30 * Tps, "the boat to reach its fishing ground");
        i = Index(sim, boat);
        int x = sim.Units.X[i].FloorToInt(), z = sim.Units.Z[i].FloorToInt();
        Assert.True(sim.Economy.OpenWater(x, z), "fishing on open water");
        Assert.True(x >= LakeX0 + EconomySystem.OpenReach, "clear of the shore");

        // Then home with the catch, into storage.
        RunUntil(sim, () => store.Stock[(int)Good.Food] == food + 12, 60 * Tps, "the catch to reach storage");
        Assert.Equal(UnitJob.Fish, sim.Units.Job[Index(sim, boat)]);
        Assert.True(sim.Economy.Player(0).Gathered[(int)Good.Food] >= 12);

        // And again.
        RunUntil(sim, () => store.Stock[(int)Good.Food] == food + 24, 90 * Tps, "a second catch");
    }

    [Fact]
    public void FishingBoatsKeepTheirGroundsApart()
    {
        var (sim, _, dock) = Harbor();
        int a = sim.SpawnUnit(0, "fishing_boat", LakeX0 + 1, 22);
        int b = sim.SpawnUnit(0, "fishing_boat", LakeX0 + 1, 23);
        sim.Schedule(0, new FishCommand(0, new[] { a, b }, dock.Id));
        RunUntil(sim, () => sim.Units.Phase[Index(sim, a)] == JobPhase.Working && sim.Units.Phase[Index(sim, b)] == JobPhase.Working,
            40 * Tps, "both boats to be fishing");
        int ia = Index(sim, a), ib = Index(sim, b);
        int apart = Math.Max(Math.Abs(sim.Units.JobX[ia] - sim.Units.JobX[ib]), Math.Abs(sim.Units.JobZ[ia] - sim.Units.JobZ[ib]));
        Assert.True(apart >= EconomySystem.FishApart, $"grounds {apart} apart");
    }

    [Fact]
    public void AMoveOrderStopsFishingAndGoFishingSendsItOutAgain()
    {
        var (sim, store, dock) = Harbor();
        int boat = sim.SpawnUnit(0, "fishing_boat", LakeX0 + 1, 22);
        sim.Schedule(0, new FishCommand(0, new[] { boat }));
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Fish, sim.Units.Job[Index(sim, boat)]);
        Assert.Equal(dock.Id, sim.Units.JobTarget[Index(sim, boat)]);

        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { boat }, 50, 50));
        sim.Run(Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, boat)]);
        RunUntil(sim, () => sim.Units.Order[Index(sim, boat)] == UnitOrder.Idle, 30 * Tps, "the boat to get where it was sent");
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, boat)]);

        int food = store.Stock[(int)Good.Food];
        sim.Schedule(sim.Tick, new FishCommand(0, new[] { boat }));
        RunUntil(sim, () => store.Stock[(int)Good.Food] > food, 90 * Tps, "a catch after going fishing again");
    }

    [Fact]
    public void ANarrowRiverHasNoFishingGround()
    {
        // Four columns of water: none of it is open water, so the boat waits at the dock and catches nothing.
        var (sim, store, dock) = Harbor(lakeX1: LakeX0 + 3);
        int boat = sim.SpawnUnit(0, "fishing_boat", LakeX0 + 1, 22);
        Assert.False(sim.Economy.FishingGround(Index(sim, boat), dock, out _, out _));
        int food = store.Stock[(int)Good.Food];
        sim.Schedule(0, new FishCommand(0, new[] { boat }));
        sim.Run(60 * Tps);
        Assert.Equal(food, store.Stock[(int)Good.Food]);
        Assert.Equal(UnitJob.Fish, sim.Units.Job[Index(sim, boat)]);
    }

    [Fact]
    public void ARivalsDockAndUnitsThatAreNotFishingBoatsIgnoreGoFishing()
    {
        var (sim, _, dock) = Harbor();
        int boat = sim.SpawnUnit(1, "fishing_boat", LakeX0 + 1, 22);
        int ship = sim.SpawnUnit(0, "ship", LakeX0 + 1, 30);
        sim.Schedule(0, new FishCommand(1, new[] { boat }, dock.Id)); // not theirs
        sim.Schedule(0, new FishCommand(0, new[] { ship }));
        sim.Run(Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, boat)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, ship)]);
    }

    [Fact]
    public void WithItsDockGoneItStopsAndKeepsItsCatch()
    {
        var (sim, store, dock) = Harbor();
        sim.TestMode = true;
        int boat = sim.SpawnUnit(0, "fishing_boat", LakeX0 + 1, 22);
        sim.Schedule(0, new FishCommand(0, new[] { boat }));
        RunUntil(sim, () => sim.Units.Phase[Index(sim, boat)] == JobPhase.ToStore, 60 * Tps, "a catch aboard");
        // Out on the water, its dock is taken away.
        sim.Schedule(sim.Tick, new SandboxClearCommand(0, dock.X + 2, dock.Z + 1, 2));
        sim.Run(Tps);
        Assert.Null(sim.Buildings.Get(dock.Id));
        int i = Index(sim, boat);
        Assert.Equal(UnitJob.None, sim.Units.Job[i]);
        Assert.Equal(12, sim.Units.CarryAmount[i]);
    }

    [Fact]
    public void GoFishingIsKeptInTheCommandLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 fish 0 7 3,4\n");
        Assert.Equal(new FishCommand(0, new[] { 3, 4 }, 7), log.Commands[0].Command);
        Assert.Equal(log.Commands, CommandLog.Parse(log.ToString()).Commands);
    }

    [Fact]
    public void SameInputsSameHashWithFishingBoats()
    {
        ulong Play()
        {
            var (sim, _, dock) = Harbor();
            sim.Schedule(0, new TrainCommand(0, dock.Id, "fishing_boat"));
            sim.Schedule(0, new TrainCommand(0, dock.Id, "fishing_boat"));
            sim.Run(120 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
