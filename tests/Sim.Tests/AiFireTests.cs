using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players setting woods alight by their rivals in dry weather (see <c>AiPlayer.Fire.cs</c>).</summary>
public class AiFireTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>
    /// A computer player's town far off; a grove along z = 70 from x = 56 to 76; a rival's house beside it; and a
    /// spearman of the computer player's by the grove's west end, seeing the house.
    /// </summary>
    private static (Simulation Sim, int Spearman, Building? House) Scene(Weather? weather = null, int wet = 0, bool house = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        if (weather is { } w)
        {
            sim.DayNight = true;
            sim.Conditions.SetWeather(w, 2L * Conditions.DayTicks, w);
            sim.Conditions.SetWetness(wet);
        }
        sim.Map.Starts.Add(new StartArea(0, 10, Ground + 1, 10));
        sim.PlaceStartingBuildings();
        for (int x = 56; x <= 76; x += 4) TestWorlds.Tree(sim.World, x, 70, 5);
        var rivalHouse = house ? sim.Economy.PlaceFinished(1, sim.Content.Building("house"), 62, 73) : null;
        int spearman = sim.SpawnUnit(0, "spearman", 56, 74);
        sim.Run(5); // its units' sight
        return (sim, spearman, rivalHouse);
    }

    private static List<Command> Play(Simulation sim, AiPlayer ai, int ticks, Func<bool>? until = null)
    {
        var log = new List<Command>();
        for (int t = 0; t < ticks && !(until?.Invoke() ?? false); t++)
        {
            ai.Think(sim, c => { log.Add(c); sim.Schedule(sim.Tick, c); });
            sim.Step();
        }
        return log;
    }

    [Fact]
    public void ComputerPlayersSetTheWoodByARivalsWoodenBuildingAlight()
    {
        foreach (var level in new[] { AiLevel.Normal, AiLevel.Hard })
        {
            var (sim, spearman, rivalHouse) = Scene();
            var house = rivalHouse!;
            var ai = new AiPlayer(0, 1, level);
            var log = Play(sim, ai, 3 * Tps);
            var kindle = Assert.Single(log.OfType<KindleCommand>());
            Assert.Equal(new[] { spearman }, kindle.Units);
            Assert.Equal((56, 70), (kindle.X, kindle.Z)); // the grove's tree nearest to it
            Assert.Equal(spearman, ai.Kindler);
            Play(sim, ai, 60 * Tps, () => house.Damaged);
            Assert.True(house.Damaged, $"{level}: the house was scorched");
            Assert.True(sim.Fire.BurntOut > 0);
            Assert.Equal(1, ai.Kindlings);
            Assert.Equal(0, ai.Kindler); // done, and back with the army
            Assert.True(sim.Units.Exists(spearman), $"{level}: the spearman got clear of the fire");
        }
    }

    [Fact]
    public void ComputerPlayersSetRivalWorkersInAWoodAlight()
    {
        var (sim, _, _) = Scene(house: false);
        var rivals = new[] { sim.SpawnUnit(1, "worker", 64, 71), sim.SpawnUnit(1, "worker", 68, 69), sim.SpawnUnit(1, "worker", 72, 71) };
        sim.Schedule(sim.Tick, new StanceCommand(1, rivals, Units.UnitStance.Hold));
        sim.Run(5);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.Single(Play(sim, ai, 3 * Tps).OfType<KindleCommand>());

        // Two are not worth a fire.
        (sim, _, _) = Scene(house: false);
        sim.SpawnUnit(1, "worker", 64, 71);
        sim.SpawnUnit(1, "worker", 68, 69);
        sim.Run(5);
        Assert.Empty(Play(sim, new AiPlayer(0, 1, AiLevel.Normal), 5 * Tps).OfType<KindleCommand>());
    }

    [Fact]
    public void ComputerPlayersLightNoFireNearTheirOwnWoodOrTheirOwnUnits()
    {
        // Its own house by the far end of the grove.
        var (sim, _, _) = Scene();
        sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 80, 66);
        Assert.Empty(Play(sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());

        // A storehouse of its own (wooden or not) within FireHome of the wood: the woods by its town are its wood.
        (sim, _, _) = Scene();
        sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 40, 50);
        Assert.Empty(Play(sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());

        // Enemy soldiers at its town: not while its defenders are out fighting them.
        (sim, _, _) = Scene();
        sim.SpawnUnit(1, "spearman", 14, 22);
        sim.Run(2);
        Assert.Empty(Play(sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());

        // A rival soldier by its spearman: it has a fight on its hands, not a fire to light.
        (sim, _, _) = Scene();
        sim.SpawnUnit(1, "spearman", 60, 76);
        sim.Run(2);
        Assert.Empty(Play(sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());

        // A worker of its own in the wood.
        (sim, _, _) = Scene();
        sim.SpawnUnit(0, "worker", 68, 71);
        sim.Run(2);
        Assert.Empty(Play(sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());
    }

    [Fact]
    public void ComputerPlayersLightNoFireInWetWeatherNorAtClassicOrEasy()
    {
        Assert.Empty(Play(Scene(Weather.Rain).Sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());
        Assert.Empty(Play(Scene(Weather.Clear, wet: 70).Sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());
        Assert.Single(Play(Scene(Weather.Cloudy, wet: 20).Sim, new AiPlayer(0, 1, AiLevel.Hard), 5 * Tps).OfType<KindleCommand>());
        foreach (var level in new[] { AiLevel.Classic, AiLevel.Easy })
            Assert.Empty(Play(Scene().Sim, new AiPlayer(0, 1, level), 5 * Tps).OfType<KindleCommand>());
    }

    [Fact]
    public void OneFireAtATimeAndNotTooOften()
    {
        var (sim, _, _) = Scene();
        TestWorlds.Tree(sim.World, 52, 74, 5); // the grove runs on to the spearman
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = Play(sim, ai, AiPlayer.FireEveryTicks - Tps);
        Assert.Single(log.OfType<KindleCommand>());
    }
}
