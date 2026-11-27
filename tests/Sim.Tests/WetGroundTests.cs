using System;
using System.Collections.Generic;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Wet ground (see <see cref="Conditions.Wetness"/>): rain soaks it, mud slows units off the roads, wet fields grow more, storms slow felling.</summary>
public class WetGroundTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3;

    private static Simulation Flat(Weather weather = Weather.Clear, int wet = 0, int size = 64)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.DayNight = true;
        sim.Conditions.SetWeather(weather, 2L * Conditions.DayTicks, weather);
        sim.Conditions.SetWetness(wet);
        return sim;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    [Fact]
    public void RainSoaksTheGroundAndClearSkiesDryIt()
    {
        var sim = Flat(Weather.Rain);
        Assert.Equal(0, sim.Conditions.Wetness);
        sim.Run(2 * Conditions.HourTicks);
        Assert.InRange(sim.Conditions.Wetness, 49, 51); // two hours of rain: half soaked
        Assert.True(sim.Conditions.Damp);

        var storm = Flat(Weather.Storm);
        storm.Run(2 * Conditions.HourTicks);
        Assert.True(storm.Conditions.Wetness > sim.Conditions.Wetness, "a storm soaks it faster");
        storm.Run(2 * Conditions.HourTicks);
        Assert.Equal(100, storm.Conditions.Wetness); // soaked through, and no wetter

        sim.Conditions.SetWeather(Weather.Clear, Conditions.DayTicks);
        int before = sim.Conditions.Wetness;
        sim.Run(2 * Conditions.HourTicks);
        Assert.InRange(sim.Conditions.Wetness, before - 25, before - 23); // drying under a clear sky
        sim.Run(5 * Conditions.HourTicks);
        Assert.Equal(0, sim.Conditions.Wetness);
    }

    [Fact]
    public void WithDayNightOffTheGroundIsAlwaysDry()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(32, 16, 32, Ground));
        sim.Conditions.SetWeather(Weather.Storm, Conditions.DayTicks);
        sim.Run(3 * Conditions.HourTicks);
        Assert.Equal(0, sim.Conditions.Wetness);
        Assert.Equal(100, sim.Conditions.MudPace(BlockType.Grass, true));
        Assert.Equal(100, sim.Conditions.FieldPercent);
    }

    [Fact]
    public void MudSlowsUnitsOnSoftGroundButNotOnRoadsOrStone()
    {
        var sim = Flat(Weather.Rain, 100);
        Assert.Equal(100 - Conditions.MudSlow, sim.Conditions.MudPace(BlockType.Grass, false));
        Assert.Equal(100 - Conditions.WheelMudSlow, sim.Conditions.MudPace(BlockType.Soil, true));
        Assert.Equal(100, sim.Conditions.MudPace(BlockType.Road, false));
        Assert.Equal(100, sim.Conditions.MudPace(BlockType.Stone, true));
        sim.Conditions.SetWetness(Conditions.DampFrom - 1);
        Assert.Equal(100, sim.Conditions.MudPace(BlockType.Grass, false)); // barely damp: no mud yet
    }

    /// <summary>Ticks a unit of <paramref name="def"/> takes to walk 24 columns east, on <paramref name="ground"/>, with the ground <paramref name="wet"/>.</summary>
    private static long Walk(string def, int wet, BlockType ground = BlockType.Grass)
    {
        var sim = Flat(wet > 0 ? Weather.Rain : Weather.Clear, wet);
        for (int x = 0; x < 64; x++)
        for (int z = 28; z <= 36; z++)
            sim.World.Set(x, Ground, z, ground);
        int id = sim.SpawnUnit(0, def, 10, 32);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { id }, 34, 32));
        long start = sim.Tick;
        sim.Step();
        while (sim.Units.Order[Index(sim, id)] == UnitOrder.Move && sim.Tick - start < 120 * Tps) sim.Step();
        Assert.InRange(sim.Units.X[Index(sim, id)].FloorToInt(), 33, 35);
        return sim.Tick - start;
    }

    [Fact]
    public void UnitsWadeThroughMudSlowerAndCartsSlowerStill()
    {
        long dry = Walk("spearman", 0), muddy = Walk("spearman", 100);
        Assert.True(muddy * 100 > dry * 130, $"in mud {muddy} ticks, on dry grass {dry}");
        long dryCart = Walk("cart", 0), muddyCart = Walk("cart", 100);
        Assert.True(muddyCart * 100 > dryCart * 170, $"a cart in mud {muddyCart} ticks, on dry grass {dryCart}");
        // A road keeps its pace in the rain.
        Assert.Equal(Walk("spearman", 0, BlockType.Road), Walk("spearman", 100, BlockType.Road));
    }

    /// <summary>Ticks until the first harvest at a farm, with the ground <paramref name="wet"/>.</summary>
    private static long FirstHarvest(int wet)
    {
        var sim = Flat(wet > 0 ? Weather.Rain : Weather.Clear, wet);
        sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 10);
        var farm = sim.Economy.PlaceFinished(0, sim.Content.Building("farm"), 24, 10)!;
        int w = sim.SpawnUnit(0, "worker", 24, 17);
        sim.Schedule(0, new WorkCommand(0, new[] { w }, farm.Id));
        while (sim.Units.CarryAmount[Index(sim, w)] == 0 && sim.Tick < 60 * Tps) sim.Step();
        Assert.True(sim.Units.CarryAmount[Index(sim, w)] > 0);
        return sim.Tick;
    }

    [Fact]
    public void WetFieldsGrowMore()
    {
        long dry = FirstHarvest(0), wet = FirstHarvest(100);
        long work = EconomySystem.FarmTicks - EconomySystem.FarmTicks * 100 / (100 + Conditions.FieldBonus);
        Assert.InRange(dry - wet, work - Tps, work + Tps);
    }

    /// <summary>Ticks until a worker fells a tree, in <paramref name="weather"/>.</summary>
    private static long Felling(Weather weather)
    {
        var sim = Flat(weather);
        TestWorlds.Tree(sim.World, 30, 30, 4, Ground);
        int w = sim.SpawnUnit(0, "worker", 29, 31);
        sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 1, 30));
        while (sim.World.Get(30, Ground + 1, 30) == BlockType.Wood && sim.Tick < 120 * Tps) sim.Step();
        Assert.NotEqual(BlockType.Wood, sim.World.Get(30, Ground + 1, 30));
        return sim.Tick;
    }

    [Fact]
    public void FellingTreesInAStormTakesLonger()
    {
        long calm = Felling(Weather.Rain), storm = Felling(Weather.Storm);
        Assert.True(storm * 100 > calm * 115, $"in a storm {storm} ticks, in the rain {calm}");
    }

    [Fact]
    public void TheWetnessIsPartOfTheStateHash()
    {
        var a = Flat(Weather.Rain);
        var b = Flat(Weather.Rain);
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Conditions.SetWetness(40);
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    /// <summary>Two towns, player 0 with sixteen spearmen and player 1 with eight, in rain on muddy ground.</summary>
    private static (Simulation, AiPlayer[]) Muddy(AiLevel level)
    {
        var sim = AiTests.WithStarts(96, (20, 48), (76, 48));
        sim.DayNight = true;
        sim.Conditions.SetWeather(Weather.Rain, 2L * Conditions.DayTicks, Weather.Rain);
        sim.Conditions.SetWetness(100);
        for (int k = 0; k < 16; k++) sim.SpawnUnit(0, "spearman", 8 + k, 56);
        for (int k = 0; k < 8; k++) sim.SpawnUnit(1, "spearman", 70 + k, 56);
        return (sim, new[] { new AiPlayer(0, 1, level), new AiPlayer(1, 1, AiLevel.Classic) });
    }

    private static void Play(Simulation sim, AiPlayer[] players, Func<bool> done, long ticks)
    {
        for (long t = 0; t < ticks && !done(); t++)
        {
            foreach (var ai in players) ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    [Fact]
    public void TheComputerHoldsItsWaveAtHomeWhileTheRainTurnsTheGroundToMud()
    {
        var (sim, players) = Muddy(AiLevel.Normal);
        var normal = players[0];
        Play(sim, players, () => normal.Waves > 0, AiPlayer.SmartFirstWaveTicks + 60 * Tps);
        Assert.Equal(0, normal.Waves);
        // The rain stops and the ground dries: the wave sets out.
        sim.Conditions.SetWeather(Weather.Clear, Conditions.DayTicks);
        sim.Conditions.SetWetness(0);
        Play(sim, players, () => normal.Waves > 0, 60 * Tps);
        Assert.True(normal.Waves > 0, "a wave once the ground is dry");
    }

    [Fact]
    public void TheComputerWaitsForTheMudOnlySoLong()
    {
        var (sim, players) = Muddy(AiLevel.Normal);
        Play(sim, players, () => players[0].Waves > 0, AiPlayer.SmartFirstWaveTicks + AiPlayer.MudHoldTicks + 60 * Tps);
        Assert.True(players[0].Waves > 0, "it gives up waiting for the rain to stop");
    }

    [Fact]
    public void TheClassicPlayerMarchesWhateverTheWeather()
    {
        var (sim, players) = Muddy(AiLevel.Classic);
        Play(sim, players, () => players[0].Waves > 0, AiPlayer.FirstWaveTicks + 60 * Tps);
        Assert.True(players[0].Waves > 0, "a wave out in the rain");
    }
}
