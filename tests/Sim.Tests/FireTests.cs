using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Forest fires (see <see cref="FireSystem"/>): trees set alight burn, spread to the trees beside them, hurt units and wooden buildings, and go out in the rain.</summary>
public class FireTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, Weather? weather = null, int wet = 0)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 20, size, Ground));
        if (weather is { } w)
        {
            sim.DayNight = true;
            sim.Conditions.SetWeather(w, 2L * Conditions.DayTicks, w);
            sim.Conditions.SetWetness(wet);
        }
        return sim;
    }

    private static int Trees(Simulation sim, int x0, int z0, int x1, int z1)
    {
        int n = 0;
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            if (Enumerable.Range(Ground + 1, 4).Any(y => BlockInfo.IsTrunk(sim.World.Get(x, y, z)))) n++;
        return n;
    }

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[sim.Units.IndexOf(id)] : 0;

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.True(done(), $"timed out waiting for {what}");
    }

    /// <summary>A grove: a row of trees every four columns, their crowns touching.</summary>
    private static void Grove(Simulation sim, int x0, int z, int count)
    {
        for (int k = 0; k < count; k++) TestWorlds.Tree(sim.World, x0 + 4 * k, z, 5);
    }

    [Fact]
    public void ABurningTreeSpreadsToTheGroveAndLeavesCharredStumps()
    {
        var sim = Flat();
        Grove(sim, 10, 20, 6);
        Assert.True(sim.Fire.Ignite(10, Ground + 2, 20, 0));
        Assert.False(sim.Fire.Ignite(10, Ground + 2, 20, 0)); // burning already
        Assert.False(sim.Fire.Ignite(30, Ground + 1, 30, 0)); // grass does not burn
        RunUntil(sim, () => sim.Fire.Count == 0, 200 * Tps, "the fire to burn out");
        Assert.Equal(0, Trees(sim, 0, 0, 63, 63)); // the whole grove burned
        for (int k = 0; k < 6; k++)
        {
            int x = 10 + 4 * k;
            Assert.Equal(BlockType.CharredWood, sim.World.Get(x, Ground + 1, 20)); // a stump at each foot
            Assert.Equal(BlockType.Air, sim.World.Get(x, Ground + 2, 20));
            Assert.Equal(BlockType.Air, sim.World.Get(x, Ground + 5, 20 + 1)); // the crown is gone
        }
        Assert.True(sim.Fire.BurntOut > 6 * 20);
    }

    [Fact]
    public void ALoneTreeFarFromTheFireDoesNotCatch()
    {
        var sim = Flat();
        TestWorlds.Tree(sim.World, 10, 20, 5);
        TestWorlds.Tree(sim.World, 20, 20, 5); // crowns 6 columns apart
        sim.Fire.Ignite(10, Ground + 2, 20, -1);
        RunUntil(sim, () => sim.Fire.Count == 0, 100 * Tps, "the fire to burn out");
        Assert.Equal(1, Trees(sim, 15, 15, 25, 25));
    }

    [Fact]
    public void RainPutsFiresOutAndStopsThemSpreading()
    {
        var sim = Flat(weather: Weather.Rain);
        Grove(sim, 10, 20, 6);
        Assert.False(sim.Fire.CanKindle);
        Assert.Equal(0, sim.Fire.IgniteAround(10, Ground + 5, 20, 0)); // a fire pot lights nothing in the rain
        sim.Fire.Ignite(10, Ground + 5, 20, 0); // lit before the rain, say
        RunUntil(sim, () => sim.Fire.Count == 0, 30 * Tps, "the rain to put it out");
        Assert.True(sim.Fire.Doused >= 1);
        Assert.Equal(6, Trees(sim, 0, 0, 63, 63));
    }

    [Fact]
    public void WetGroundSlowsTheSpread()
    {
        int Burnt(int wet)
        {
            var sim = Flat(weather: Weather.Clear, wet: wet);
            Grove(sim, 10, 20, 10);
            sim.Fire.Ignite(10, Ground + 5, 20, -1);
            sim.Run(20 * Tps);
            return 10 - Trees(sim, 0, 0, 63, 63) + sim.Fire.Count / 10;
        }
        Assert.True(Burnt(0) > Burnt(80), $"dry {Burnt(0)}, wet {Burnt(80)}");
    }

    [Fact]
    public void UnitsBesideTheFireAreBurned()
    {
        var sim = Flat();
        TestWorlds.Tree(sim.World, 20, 20, 5);
        int near = sim.SpawnUnit(1, "spearman", 21, 20);
        int far = sim.SpawnUnit(1, "spearman", 30, 20);
        sim.Schedule(0, new StanceCommand(1, new[] { near, far }, UnitStance.Hold));
        for (int y = Ground + 1; y <= Ground + 5; y++) sim.Fire.Ignite(20, y, 20, -1);
        sim.Run(1);
        int i = sim.Units.IndexOf(near);
        Assert.Equal(FireSystem.HeatDamage, sim.Hazards.DamageAt(sim.Units.X[i].FloorToInt(), sim.Units.Y[i], sim.Units.Z[i].FloorToInt()));
        int hp = Hp(sim, near);
        sim.Run(5 * Tps);
        Assert.True(hp - Hp(sim, near) >= 4 * FireSystem.HeatDamage, $"lost {hp - Hp(sim, near)}");
        Assert.Equal(sim.Content.Unit("spearman").MaxHp, Hp(sim, far));
    }

    [Fact]
    public void FireScorchesWoodenBuildingsButNotStone()
    {
        var sim = Flat();
        var house = sim.Economy.PlaceFinished(1, sim.Content.Building("house"), 22, 18)!;
        var wall = sim.Economy.PlaceFinished(1, sim.Content.Building("stone_tower"), 22, 40);
        // Trees right against them.
        TestWorlds.Tree(sim.World, 20, 20, 5);
        TestWorlds.Tree(sim.World, 20, 42, 5);
        int health = house.Health;
        int towerHealth = wall?.Health ?? 0;
        for (int y = Ground + 1; y <= Ground + 7; y++)
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            sim.Fire.Ignite(20 + dx, y, 20 + dz, 0);
            sim.Fire.Ignite(20 + dx, y, 42 + dz, 0);
        }
        sim.Run(8 * Tps);
        Assert.True(house.Health < health || house.Damage > 0 || !sim.Buildings.All.Contains(house), "the house was scorched");
        if (wall != null) Assert.Equal(towerHealth, wall.Health);
    }

    [Fact]
    public void AWorkerSetsATreeAlightButNotInTheRain()
    {
        var sim = Flat();
        TestWorlds.Tree(sim.World, 20, 20, 5);
        int worker = sim.SpawnUnit(0, "worker", 14, 20);
        int cart = sim.SpawnUnit(0, "cart", 14, 22);
        Assert.True(sim.Economy.CanKindle(sim.Units.IndexOf(worker)));
        Assert.False(sim.Economy.CanKindle(sim.Units.IndexOf(cart)));
        sim.Schedule(0, new KindleCommand(0, new[] { worker, cart }, 21, 21)); // a click on the crown finds the tree
        sim.Run(1);
        Assert.Equal(UnitJob.Kindle, sim.Units.Job[sim.Units.IndexOf(worker)]);
        Assert.NotEqual(UnitJob.Kindle, sim.Units.Job[sim.Units.IndexOf(cart)]);
        RunUntil(sim, () => sim.Fire.Count > 0, 15 * Tps, "the tree to catch");
        Assert.True(sim.Fire.Burning(20, Ground + 2, 20));
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
        // It gets clear of the fire it set.
        sim.Run(8 * Tps);
        Assert.Equal(sim.Content.Unit("worker").MaxHp, Hp(sim, worker));
        Assert.True(Math.Abs(sim.Units.X[sim.Units.IndexOf(worker)].FloorToInt() - 20) >= 4, "the worker walked away from the fire");

        var wet = Flat(weather: Weather.Storm);
        TestWorlds.Tree(wet.World, 20, 20, 5);
        int w2 = wet.SpawnUnit(0, "worker", 14, 20);
        wet.Schedule(0, new KindleCommand(0, new[] { w2 }, 20, 20));
        wet.Run(15 * Tps);
        Assert.Equal(0, wet.Fire.Count);
    }

    [Fact]
    public void FirePotsSetTheTreesTheyLandInAlight()
    {
        var sim = Flat();
        Assert.True(sim.Content.Unit("siege_engineer").Ignites);
        Assert.False(sim.Content.Unit("catapult").Ignites);
        TestWorlds.Tree(sim.World, 30, 20, 3); // a low crown the enemy stands by
        int engineer = sim.SpawnUnit(0, "siege_engineer", 28, 25);
        int enemy = sim.SpawnUnit(1, "spearman", 33, 20); // beside the crown
        sim.Schedule(0, new StanceCommand(1, new[] { enemy }, UnitStance.Hold));
        sim.Schedule(0, new AttackCommand(0, new[] { engineer }, enemy));
        int hp = Hp(sim, enemy);
        RunUntil(sim, () => sim.Fire.Count > 0, 20 * Tps, $"a fire pot to set the tree alight (enemy lost {hp - Hp(sim, enemy)}, order {sim.Units.Order[sim.Units.IndexOf(engineer)]})");
    }

    [Fact]
    public void CharredStumpsAreDugForCharcoal()
    {
        Assert.True(Harvest.Yields(BlockType.CharredWood, out var good));
        Assert.Equal(Good.Coal, good);
        Assert.Equal(FireSystem.CharcoalPerStump, Harvest.Amount(BlockType.CharredWood));
        Assert.True(Harvest.CanGather(BlockType.CharredWood));
        Assert.False(FireSystem.CanBurn(BlockType.CharredWood));
        Assert.False(FireSystem.CanBurn(BlockType.DuskLeaves));
        Assert.False(FireSystem.CanBurn(BlockType.MushroomCap));
    }

    [Fact]
    public void FiresAreTheSameOnEveryMachineAndInTheReplay()
    {
        Simulation Run()
        {
            var sim = Flat();
            Grove(sim, 10, 20, 6);
            int worker = sim.SpawnUnit(0, "worker", 6, 20);
            sim.Schedule(0, new KindleCommand(0, new[] { worker }, 10, 20));
            sim.Run(40 * Tps);
            return sim;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.True(a.Fire.BurntOut > 0);

        var c = new KindleCommand(0, new[] { 5, 7 }, 12, 34);
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n9 kindle 0 12 34 5,7\n");
        Assert.Equal(c, log.Commands.Single().Command);
        Assert.Contains("9 kindle 0 12 34 5,7", log.ToString());
    }
}
