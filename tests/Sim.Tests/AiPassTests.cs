using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players mining the mountain passes: once no stone giant guards a pass they have seen, and while they
/// need the ore, they keep a few miners on the iron (and gold) seamed into its walls, with a storehouse in the pass.
/// </summary>
public class AiPassTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int PassX = 110, PassZ = 128, Half = 4, RidgeZ0 = 125, RidgeZ1 = 131, Top = Ground + 10;

    /// <summary>
    /// One town in the west of a flat map with ten workers, a finished smelter and storehouse, and 70 blocks east of
    /// it a stone ridge running west to east with a pass 9 wide through it; the pass's walls seamed with iron ore,
    /// a little gold, and the pass itself on the map's list. A stone giant in the pass unless <paramref name="giant"/>
    /// is false.
    /// </summary>
    private static (Simulation Sim, Building Hall, Lair? Giant) Town(bool giant = true, bool seen = true, bool smelter = true)
    {
        var w = TestWorlds.Flat(256, 24, 256, Ground);
        TestWorlds.Box(w, PassX - 30, Ground + 1, RidgeZ0, PassX - Half - 1, Top, RidgeZ1, BlockType.Stone);
        TestWorlds.Box(w, PassX + Half + 1, Ground + 1, RidgeZ0, PassX + 30, Top, RidgeZ1, BlockType.Stone);
        // Seams in both walls of the pass: iron ore in the wall faces and a block in, gold here and there.
        foreach (int side in new[] { -1, 1 })
        for (int k = 0; k < 6; k++)
        for (int z = RidgeZ0 + 1; z <= RidgeZ1 - 1; z++)
        for (int y = Ground + 1; y <= Ground + 3; y++)
        {
            int x = PassX + side * (Half + 1 + k);
            w.Set(x, y, z, (k + z + y) % 7 == 0 ? BlockType.GoldOre : BlockType.IronOre);
        }
        var sim = TestWorlds.Sim(w);
        sim.Fog = true;
        sim.Map.Passes.Add((PassX, Ground, PassZ));
        sim.Map.Starts.Add(new StartArea(0, 40, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        if (smelter) Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("smelter"), 40, 108));
        for (int k = 0; k < 10; k++) sim.SpawnUnit(0, "worker", 30 + k, 146);
        Lair? lair = giant ? sim.Monsters.Place(sim.Content.Unit("stone_giant"), PassX, PassZ - 10) : null;
        if (seen)
        {
            int scout = sim.SpawnUnit(0, "worker", PassX, PassZ - 6);
            sim.Run(2);
            sim.Units.Remove(scout);
        }
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Wood] = 400;
        hall.Stock[(int)Good.Stone] = 200;
        return (sim, hall, lair);
    }

    private static void Play(Simulation sim, AiPlayer ai, int ticks, Func<bool>? done = null)
    {
        for (int t = 0; t < ticks && (done == null || !done()); t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    private static bool Mining(Simulation sim, int id) =>
        sim.Units.IndexOf(id) is var i and >= 0 && sim.Units.Job[i] == UnitJob.Gather
        && sim.Units.JobBlock[i] is BlockType.IronOre or BlockType.GoldOre;

    private static int OreLeft(Simulation sim)
    {
        int n = 0;
        for (int x = PassX - 20; x <= PassX + 20; x++)
        for (int z = RidgeZ0; z <= RidgeZ1; z++)
        for (int y = Ground + 1; y <= Top; y++)
            if (sim.World.Get(x, y, z) is BlockType.IronOre or BlockType.GoldOre) n++;
        return n;
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ItMinesThePassOnceTheGiantIsGone(AiLevel level)
    {
        var (sim, hall, giant) = Town();
        var ai = new AiPlayer(0, 1, level);
        // While the giant stands in the pass it sends nobody there.
        Play(sim, ai, 30 * Tps);
        Assert.Empty(ai.PassMinerIds);
        Assert.Null(ai.PassToMine(sim, hall));

        foreach (int id in giant!.Pack.ToArray()) sim.Units.Remove(id);
        sim.Run(1);
        Assert.True(giant.Cleared);
        Assert.Equal((PassX, Ground, PassZ), ai.PassToMine(sim, hall));

        int ore = OreLeft(sim);
        Play(sim, ai, 3 * 60 * Tps, () => sim.Economy.Stock(0, Good.IronOre) >= 20);
        Assert.Equal(AiPlayer.PassMiners, ai.PassMinerIds.Count);
        Assert.All(ai.PassMinerIds, id => Assert.True(Mining(sim, id), $"miner {id} at its seam"));
        Assert.True(OreLeft(sim) < ore, "ore dug out of the pass's walls");
        Assert.True(sim.Economy.Stock(0, Good.IronOre) >= 20, $"iron ore brought in: {sim.Economy.Stock(0, Good.IronOre)}");
        // Storage is 70 columns off: a storehouse in the pass.
        Play(sim, ai, 60 * Tps, () => sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Storehouse
            && b.ColumnDistance(PassX, PassZ) < AiPlayer.PassStoreGap));
        Assert.Contains(sim.Buildings.All, b => b.Owner == 0 && b.Def.Kind == BuildingKind.Storehouse
            && b.ColumnDistance(PassX, PassZ) < AiPlayer.PassStoreGap);
    }

    [Fact]
    public void ItLetsItsMinersGoOnceItHasTheOre()
    {
        var (sim, hall, _) = Town(giant: false);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Play(sim, ai, 60 * Tps, () => ai.PassMinerIds.Count == AiPlayer.PassMiners);
        var miners = ai.PassMinerIds.ToArray();
        Assert.Equal(AiPlayer.PassMiners, miners.Length);

        hall.Stock[(int)Good.IronOre] = AiPlayer.PassOreWanted;
        Play(sim, ai, 10 * Tps);
        Assert.Empty(ai.PassMinerIds);
        Assert.All(miners, id => Assert.True(sim.Units.Auto[sim.Units.IndexOf(id)], "back to automatic work"));
    }

    [Fact]
    public void NoSmelterOrAnUnseenPassMeansNoMiners()
    {
        // A pass it has never seen (its scouts may find it later).
        var (sim, hall, _) = Town(giant: false, seen: false);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.Null(ai.PassToMine(sim, hall));
        Play(sim, ai, 5 * Tps);
        Assert.Empty(ai.PassMinerIds);

        // A pass it has seen, but no smelter to feed (and no gold wanted).
        (sim, hall, _) = Town(giant: false, smelter: false);
        ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.Equal((PassX, Ground, PassZ), ai.PassToMine(sim, hall));
        Play(sim, ai, 30 * Tps);
        Assert.Empty(ai.PassMinerIds);
    }

    [Fact]
    public void TheClassicLevelNeverMinesAPass()
    {
        var (sim, _, _) = Town(giant: false);
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Play(sim, ai, 60 * Tps);
        Assert.Empty(ai.PassMinerIds);
    }
}
