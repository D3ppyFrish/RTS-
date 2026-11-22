using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class DamTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int Top = Ground + 1;
    private const int RiverZ0 = 30, RiverZ1 = 34; // a river right across the map, 3 deep with water 2 deep in it
    private const int DamX = 20;
    private const int Middle = 32;

    /// <summary>Flat ground with a river across it from west to east: nobody wades it, and there is no way round.</summary>
    private static Simulation River()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 0, 1, RiverZ0, 63, Ground, RiverZ1, BlockType.Air);
        TestWorlds.Box(w, 0, 1, RiverZ0, 63, 2, RiverZ1, BlockType.Water);
        return TestWorlds.Sim(w);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Z(Simulation sim, int id) => sim.Units.Z[Index(sim, id)].FloorToInt();

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

    private static Building Store(Simulation sim, int owner, int stone, int x = 10, int z = 10)
    {
        var store = sim.Economy.PlaceFinished(owner, sim.Content.Building("storehouse"), x, z)!;
        store.Stock[(int)Good.Stone] = stone;
        return store;
    }

    /// <summary>Builds the dam across the river at x = 20 with two workers, and returns it.</summary>
    private static Dam Build(Simulation sim)
    {
        var workers = new[] { sim.SpawnUnit(0, "worker", DamX - 1, RiverZ0 - 2), sim.SpawnUnit(0, "worker", DamX + 1, RiverZ0 - 2) };
        sim.Schedule(sim.Tick, new DamCommand(0, workers, DamX, RiverZ0 - 1, DamX, RiverZ1 + 1));
        sim.Step();
        var dam = Assert.Single(sim.Economy.Dams);
        RunUntil(sim, () => dam.Done, 120 * Tps, "the dam to be built");
        return dam;
    }

    /// <summary>Water blocks in the river east of the dam.</summary>
    private static int WaterBelow(Simulation sim)
    {
        int n = 0;
        for (int x = DamX + 1; x < 64; x++)
            for (int z = RiverZ0; z <= RiverZ1; z++)
                for (int y = 1; y <= Ground; y++)
                    if (sim.World.Get(x, y, z) == BlockType.Water) n++;
        return n;
    }

    /// <summary>The river east of the dam is drained, as if dug out dry.</summary>
    private static void Drain(Simulation sim) =>
        TestWorlds.Box(sim.World, DamX + 1, 1, RiverZ0, 63, Ground, RiverZ1, BlockType.Air);

    [Fact]
    public void WorkersDamARiverAndUnitsWalkAcrossIt()
    {
        var sim = River();
        var store = Store(sim, 0, stone: 100);

        // Before: a spearman cannot get over the river.
        int spearman = sim.SpawnUnit(0, "spearman", DamX, 24);
        sim.Schedule(0, new MoveCommand(0, new[] { spearman }, DamX, 40));
        sim.Run(20 * Tps);
        Assert.True(Z(sim, spearman) < RiverZ0, "nobody wades a river two deep");

        // The plan: each column of the river filled from its bed to a level under the banks; the middle one the sluice.
        var plan = new List<DamSpan>();
        Assert.Equal(Top, sim.Economy.PlanDam(DamX, RiverZ0 - 1, DamX, RiverZ1 + 1, plan, out int sluice));
        Assert.Equal(Enumerable.Range(RiverZ0, RiverZ1 - RiverZ0 + 1), plan.Select(s => s.Z));
        Assert.All(plan, s => Assert.Equal((1, Top, true), (s.Bed, s.Top, s.Room)));
        Assert.Equal(Middle, plan[sluice].Z);
        int blocks = plan.Sum(s => s.Blocks);
        Assert.Equal(5 * 3, blocks);

        var dam = Build(sim);
        Assert.Equal(sluice, dam.Sluice);
        Assert.False(dam.Open);
        for (int z = RiverZ0; z <= RiverZ1; z++)
            for (int y = 1; y < Top; y++)
                Assert.Equal(z == Middle ? BlockType.Planks : BlockType.Soil, sim.World.Get(DamX, y, z));
        Assert.Equal(100 - blocks, store.Stock[(int)Good.Stone]);
        Assert.Equal(blocks, sim.Economy.Player(0).Spent[(int)Good.Stone]);
        sim.Run(Tps);
        Assert.Single(sim.Economy.Dams); // a finished dam stays, for its sluice

        // After: the spearman walks across on top of it.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { spearman }, DamX, 40));
        RunUntil(sim, () => Z(sim, spearman) >= 40, 30 * Tps, "the spearman to walk across the dam");
    }

    [Fact]
    public void TheDamHoldsTheWaterBackUntilItsSluiceIsOpened()
    {
        var sim = River();
        Store(sim, 0, stone: 100);
        var dam = Build(sim);
        Drain(sim);
        sim.Run(10 * Tps);
        Assert.Equal(0, WaterBelow(sim)); // held back

        // Only its owner opens it.
        sim.Schedule(sim.Tick, new SluiceCommand(1, dam.Id, true));
        sim.Run(5 * Tps);
        Assert.False(dam.Open);
        Assert.Equal(0, WaterBelow(sim));

        sim.Schedule(sim.Tick, new SluiceCommand(0, dam.Id, true));
        sim.Step();
        Assert.True(dam.Open);
        Assert.Equal(BlockType.Air, sim.World.Get(DamX, 1, Middle));
        Assert.Equal(BlockType.Planks, sim.World.Get(DamX, Top - 1, Middle)); // the top board stays: the way across is open
        RunUntil(sim, () => sim.World.Get(40, 1, Middle) == BlockType.Water, 60 * Tps, "the water to pour through the sluice");

        // Shut again: the boards go back in, through the water.
        sim.Schedule(sim.Tick, new SluiceCommand(0, dam.Id, false));
        sim.Step();
        Assert.False(dam.Open);
        for (int y = 1; y < Top; y++) Assert.Equal(BlockType.Planks, sim.World.Get(DamX, y, Middle));
    }

    [Fact]
    public void TheSluiceDoesNotShutOnSomeoneInTheGap()
    {
        var sim = River();
        Store(sim, 0, stone: 100);
        var dam = Build(sim);
        sim.Schedule(sim.Tick, new SluiceCommand(0, dam.Id, true));
        sim.Step();
        // Someone wades into the gap (the water there is two deep: it stands on the bed with its feet in it).
        sim.World.Set(DamX, 2, Middle, BlockType.Air);
        int spearman = sim.SpawnUnit(1, "spearman", DamX, Middle);
        sim.Units.Y[Index(sim, spearman)] = 1;
        sim.Schedule(sim.Tick, new SluiceCommand(0, dam.Id, false));
        sim.Step();
        Assert.True(dam.Open);
        Assert.NotEqual(BlockType.Planks, sim.World.Get(DamX, 1, Middle));
    }

    [Fact]
    public void DiggingTheDamAwayBreachesIt()
    {
        var sim = River();
        Store(sim, 0, stone: 100);
        Build(sim);
        Drain(sim);
        sim.Run(5 * Tps);
        Assert.Equal(0, WaterBelow(sim));
        // An enemy sapper digs into the dam, and the water breaks through.
        int digger = sim.SpawnUnit(1, "sapper", DamX + 2, RiverZ0 - 2);
        sim.Schedule(sim.Tick, new DigAreaCommand(1, new[] { digger }, DamX, RiverZ0, DamX + 1, RiverZ0 + 1, 1));
        RunUntil(sim, () => WaterBelow(sim) > 0, 120 * Tps,
            $"the water to break through the dug dam: digger job {sim.Units.Job[Index(sim, digger)]} at {sim.Units.X[Index(sim, digger)].FloorToInt()},{sim.Units.Y[Index(sim, digger)]},{Z(sim, digger)}, dam column " +
            string.Join(",", Enumerable.Range(0, Top + 1).Select(y => sim.World.Get(DamX, y, RiverZ0))) + $" areas {sim.Economy.DigAreas.Count}");
    }

    [Fact]
    public void ADamNeedsBanksWaterAndRoom()
    {
        var sim = River();
        var plan = new List<DamSpan>();
        // Flat dry ground: nothing to hold back.
        Assert.Equal(-1, sim.Economy.PlanDam(DamX, 10, DamX, 20, plan, out _));
        // An end in the water is not a bank.
        Assert.Equal(-1, sim.Economy.PlanDam(DamX, RiverZ0 - 1, DamX, Middle, plan, out _));
        // A building on the line.
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("torch"), DamX + 10, RiverZ0 - 1));
        Assert.Equal(Top, sim.Economy.PlanDam(DamX + 10, RiverZ0 - 2, DamX + 10, RiverZ1 + 1, plan, out _)); // the torch is on the bank: left as it is
        sim.World.Set(DamX + 5, Top, Middle, BlockType.Stone); // something over the water where the dam's top would be
        Assert.Equal(-1, sim.Economy.PlanDam(DamX + 5, RiverZ0 - 1, DamX + 5, RiverZ1 + 1, plan, out _));
        Assert.Contains(plan, s => !s.Room);
        // A command for a plan that does not work does nothing, and soldiers do not build dams.
        int worker = sim.SpawnUnit(0, "worker", DamX, 24);
        sim.Schedule(0, new DamCommand(0, new[] { worker }, DamX, 10, DamX, 20));
        int spearman = sim.SpawnUnit(0, "spearman", DamX, 26);
        sim.Schedule(0, new DamCommand(0, new[] { spearman }, DamX, RiverZ0 - 1, DamX, RiverZ1 + 1));
        sim.Step();
        Assert.Empty(sim.Economy.Dams);
        // A dam nobody builds any more is dropped, and its sluice cannot be opened before it is done.
        Store(sim, 0, stone: 100);
        sim.Schedule(sim.Tick, new DamCommand(0, new[] { worker }, DamX, RiverZ0 - 1, DamX, RiverZ1 + 1));
        sim.Step();
        var dam = Assert.Single(sim.Economy.Dams);
        sim.Schedule(sim.Tick, new SluiceCommand(0, dam.Id, true));
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { worker }, DamX, 10));
        sim.Run(2 * Tps);
        Assert.False(dam.Open);
        Assert.Empty(sim.Economy.Dams);
    }

    [Fact]
    public void DamCommandsRoundTripAndTheSameInputsGiveTheSameState()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 64 16 64\nticks 10\n7 dam 1 20 29 20 35 4,9\n8 sluice 1 3 on\n");
        Assert.Contains("7 dam 1 20 29 20 35 4,9", log.ToString());
        Assert.Contains("8 sluice 1 3 on", log.ToString());
        Assert.Equal(new DamCommand(1, new[] { 4, 9 }, 20, 29, 20, 35), log.Commands[0].Command);
        Assert.Equal(new SluiceCommand(1, 3, true), log.Commands[1].Command);

        ulong Run()
        {
            var sim = River();
            Store(sim, 0, stone: 100);
            var dam = Build(sim);
            Drain(sim);
            sim.Schedule(sim.Tick + 5, new SluiceCommand(0, dam.Id, true));
            sim.Run(20 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
