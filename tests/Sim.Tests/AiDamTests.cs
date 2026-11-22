using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and dams: a smart computer player opens its own sluice on rivals in the dry ground below it, and
/// shuts it again later; it breaches a rival's dam near its town when the water would flood rivals there; never
/// when its own units are in the way, and the classic level leaves dams be.
/// </summary>
public class AiDamTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int RiverZ0 = 30, RiverZ1 = 34; // a river right across the map, 3 deep with water 2 deep in it
    private const int DamX = 20;

    /// <summary>
    /// A river across a flat map, with player 0's town hall north of it, a dam across it by <paramref name="owner"/>
    /// at x = 20, and the river east of the dam dug out dry: a channel the dam holds the water back from.
    /// </summary>
    private static (Simulation Sim, Building Hall, Dam Dam) Town(int owner)
    {
        var w = TestWorlds.Flat(96, 16, 96, Ground);
        TestWorlds.Box(w, 0, 1, RiverZ0, 95, Ground, RiverZ1, BlockType.Air);
        TestWorlds.Box(w, 0, 1, RiverZ0, 95, 2, RiverZ1, BlockType.Water);
        TestWorlds.Box(w, 2, Ground + 1, 2, 6, Ground + 2, 6, BlockType.Stone); // stone for its workers by its town, nearer than the river banks
        var sim = TestWorlds.Sim(w);
        sim.Map.Starts.Add(new StartArea(0, 16, Ground + 1, 12));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(0)!;
        var store = sim.Economy.PlaceFinished(owner, sim.Content.Building("storehouse"), 30, owner == 0 ? 20 : 44)!;
        store.Stock[(int)Good.Stone] = 100;
        int side = owner == 0 ? RiverZ0 - 2 : RiverZ1 + 2;
        var builders = new[] { sim.SpawnUnit(owner, "worker", DamX - 1, side), sim.SpawnUnit(owner, "worker", DamX + 1, side) };
        // Drawn from the builders' bank.
        sim.Schedule(sim.Tick, owner == 0 ? new DamCommand(owner, builders, DamX, RiverZ0 - 1, DamX, RiverZ1 + 1)
            : new DamCommand(owner, builders, DamX, RiverZ1 + 1, DamX, RiverZ0 - 1));
        sim.Step();
        var dam = Assert.Single(sim.Economy.Dams);
        for (int t = 0; t < 120 * Tps && !dam.Done; t++) sim.Step();
        Assert.True(dam.Done, "the dam is built");
        foreach (int id in builders) sim.Units.Remove(id);
        TestWorlds.Box(sim.World, DamX + 1, 1, RiverZ0, 95, Ground, RiverZ1, BlockType.Air);
        // Its workers by its town, to breach a rival's dam (with a dam of its own, none: they would go and dig the
        // channel's bed, and it does not flood its own).
        if (owner != 0)
            for (int k = 0; k < 3; k++) sim.SpawnUnit(0, "worker", 3 + 2 * k, 8);
        return (sim, hall, dam);
    }

    /// <summary>Rival spearmen standing in the dry channel, 8 columns below the dam.</summary>
    private static int[] InChannel(Simulation sim, int owner, int n = 2) =>
        Enumerable.Range(0, n).Select(k => sim.SpawnUnit(owner, "spearman", DamX + 8 + k, RiverZ0 + 2, 1)).ToArray();

    private static void Play(Simulation sim, AiPlayer ai, int seconds)
    {
        for (int t = 0; t < seconds * Tps; t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
    }

    private static int WaterInChannel(Simulation sim)
    {
        int n = 0;
        for (int x = DamX + 1; x < 96; x++)
            for (int z = RiverZ0; z <= RiverZ1; z++)
                for (int y = 1; y <= Ground; y++)
                    if (sim.World.Get(x, y, z) == BlockType.Water) n++;
        return n;
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ItOpensItsSluiceOnRivalsInTheChannelAndShutsItLater(AiLevel level)
    {
        var (sim, _, dam) = Town(owner: 0);
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 5);
        Assert.False(dam.Open); // nobody below: it keeps the water back

        var foes = InChannel(sim, 1);
        Play(sim, ai, 3);
        Assert.True(dam.Open, "it opens the sluice on the spearmen below");
        Assert.Equal(1, ai.SluicesOpened);
        Play(sim, ai, 20);
        Assert.True(WaterInChannel(sim) > 0);
        foreach (int id in foes)
            Assert.True(!sim.Units.Exists(id) || sim.Units.Y[sim.Units.IndexOf(id)] > 1, "the spearmen are swept out or drowned");

        Play(sim, ai, AiPlayer.SluiceOpenTicks / Tps + 2);
        Assert.False(dam.Open); // shut again
    }

    [Fact]
    public void ItKeepsTheSluiceShutWhileItsOwnUnitsAreBelowOrWithNobodyToFlood()
    {
        var (sim, _, dam) = Town(owner: 0);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        InChannel(sim, 1);
        sim.SpawnUnit(0, "spearman", DamX + 14, RiverZ0 + 2, 1); // one of its own down there too
        Play(sim, ai, 5);
        Assert.False(dam.Open);
        Assert.Equal(0, ai.SluicesOpened);

        // The classic level never opens it.
        var (sim2, _, dam2) = Town(owner: 0);
        InChannel(sim2, 1);
        Play(sim2, new AiPlayer(0, 1, AiLevel.Classic), 5);
        Assert.False(dam2.Open);
    }

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ItBreachesARivalsDamToFloodRivalsBelowIt(AiLevel level)
    {
        var (sim, _, dam) = Town(owner: 1);
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 5);
        Assert.Equal(0, ai.Breaching); // nobody below to flood

        InChannel(sim, 1);
        Play(sim, ai, 3);
        Assert.Equal(dam.Id, ai.Breaching);
        Assert.Equal(1, ai.BreachesStarted);
        for (int s = 0; s < 120 && WaterInChannel(sim) == 0; s++) Play(sim, ai, 1);
        Assert.True(WaterInChannel(sim) > 0, "its workers dig through the dam and the water pours into the channel");
    }

    [Fact]
    public void ItLeavesARivalsDamBeWhenItsOwnUnitsWouldDrownOrForTheClassicLevel()
    {
        var (sim, _, _) = Town(owner: 1);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        InChannel(sim, 1);
        sim.SpawnUnit(0, "spearman", DamX + 14, RiverZ0 + 2, 1);
        Play(sim, ai, 5);
        Assert.Equal(0, ai.BreachesStarted);

        var (sim2, _, _) = Town(owner: 1);
        var classic = new AiPlayer(0, 1, AiLevel.Classic);
        InChannel(sim2, 1);
        Play(sim2, classic, 5);
        Assert.Equal(0, classic.BreachesStarted);
    }
}
