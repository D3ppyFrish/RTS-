using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players bring their battered catapults and rams home to the siege workshop to be mended whole before they
/// go out again (see AiPlayer.Yards.cs).
/// </summary>
public class AiYardTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>
    /// A flat map with player 0's Settlers in the west and player 1's Orcs in the east, each with its starting units, and
    /// player 0's stores full; with <paramref name="workshop"/>, player 0 also has a finished siege workshop well away
    /// from its town hall.
    /// </summary>
    private static (Simulation Sim, Building? Shop) Map(bool workshop = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 32, 256, Ground));
        sim.SetFaction(0, "settlers");
        sim.SetFaction(1, "orcs");
        sim.Map.Starts.Add(new StartArea(0, 60, Ground + 1, 128));
        sim.Map.Starts.Add(new StartArea(1, 230, Ground + 1, 128));
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(24);
        var hall = sim.Economy.TownHall(0)!;
        for (int g = 0; g < hall.Stock.Length; g++) hall.Stock[g] = 500;
        var shop = workshop ? sim.Economy.PlaceFinished(0, sim.Content.Building("siege_workshop"), 40, 90) : null;
        if (workshop) Assert.NotNull(shop);
        return (sim, shop);
    }

    /// <summary>A machine of player 0's at (x, z) with this share (percent) of its hit points.</summary>
    private static int Battered(Simulation sim, string unit, int x, int z, int percent)
    {
        int id = sim.SpawnUnit(0, unit, x, z);
        Assert.True(id >= 0, $"{unit} spawned");
        int i = sim.Units.IndexOf(id);
        sim.Units.Hp[i] = sim.Economy.MaxHp(0, sim.Content.Unit(unit)) * percent / 100;
        return id;
    }

    private static bool Play(Simulation sim, AiPlayer ai, int ticks, Func<bool> done)
    {
        for (int t = 0; t < ticks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[sim.Units.IndexOf(id)];
    private static int Max(Simulation sim, int id) => sim.Economy.MaxHp(0, sim.Content.Units[sim.Units.Def[sim.Units.IndexOf(id)]]);

    [Theory]
    [InlineData(AiLevel.Easy, "catapult")]
    [InlineData(AiLevel.Normal, "battering_ram")]
    [InlineData(AiLevel.Hard, "catapult")]
    public void ABatteredMachineAtHomeIsMendedWholeAtTheSiegeWorkshop(AiLevel level, string unit)
    {
        var (sim, shop) = Map();
        int id = Battered(sim, unit, 80, 140, 40);
        var ai = new AiPlayer(0, 1, level);

        Assert.True(Play(sim, ai, 30 * Tps, () => ai.Mending.Contains(id)), "the machine to be taken out of the army");
        Assert.Equal(1, ai.MachinesToYard);
        Assert.True(Play(sim, ai, 60 * Tps, () => sim.Economy.YardFor(sim.Units.IndexOf(id)) == shop), "the machine to wait at the workshop");
        // It stays out of the army past the point a wounded soldier would rejoin, until it is whole.
        Assert.True(Play(sim, ai, 120 * Tps, () => Hp(sim, id) * 100 >= Max(sim, id) * AiPlayer.HealedPercent), "it to mend");
        Assert.Contains(id, ai.Mending);
        Assert.True(Play(sim, ai, 60 * Tps, () => !ai.Mending.Contains(id)), "it to be let back");
        Assert.Equal(Max(sim, id), Hp(sim, id));
    }

    [Fact]
    public void AMachineThatIsHardlyScratchedStaysWithTheArmy()
    {
        var (sim, _) = Map();
        int id = Battered(sim, "catapult", 80, 140, AiPlayer.MachineMendPercent + 2);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Play(sim, ai, 30 * Tps, () => false);
        Assert.DoesNotContain(id, ai.Mending);
        Assert.Equal(0, ai.MachinesToYard);
    }

    [Fact]
    public void WithNoWorkshopABatteredMachineIsNotSentToAYard()
    {
        var (sim, _) = Map(workshop: false);
        int id = Battered(sim, "catapult", 80, 140, 90);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Play(sim, ai, 30 * Tps, () => false);
        Assert.DoesNotContain(id, ai.Mending);
        Assert.Equal(0, ai.MachinesToYard);
    }

    [Fact]
    public void TheClassicPlayerLeavesItsMachinesBe()
    {
        var (sim, _) = Map();
        int id = Battered(sim, "catapult", 80, 140, 40);
        var ai = new AiPlayer(0, 1, AiLevel.Classic);
        Play(sim, ai, 30 * Tps, () => false);
        Assert.Equal(0, ai.MachinesToYard);
        Assert.Null(sim.Economy.YardFor(sim.Units.IndexOf(id)));
    }
}
