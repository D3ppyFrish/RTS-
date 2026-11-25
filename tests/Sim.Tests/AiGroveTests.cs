using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players growing the woods back by their town with a mage's Grow Forest once the trees near it are cut down.</summary>
public class AiGroveTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    /// <summary>A computer player's town on bare flat ground (no tree anywhere), with a mage by its town hall.</summary>
    private static (Simulation Sim, int Mage) Town()
    {
        var sim = AiTests.WithStarts(128, (64, 64));
        var hall = sim.Economy.TownHall(0)!;
        int mage = sim.SpawnUnit(0, "mage", hall.X - 3, hall.Z - 3);
        return (sim, mage);
    }

    private static List<(int X, int Z)> Trunks(Simulation sim, int x, int z, int reach)
    {
        var w = sim.World;
        var found = new List<(int, int)>();
        for (int cz = z - reach; cz <= z + reach; cz++)
        for (int cx = x - reach; cx <= x + reach; cx++)
            if (BlockInfo.IsTrunk(w.Get(cx, Ground + 1, cz))) found.Add((cx, cz));
        return found;
    }

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void AMageGrowsAGroveWhenTheTreesByTheTownAreGone(AiLevel level)
    {
        var (sim, mage) = Town();
        var hall = sim.Economy.TownHall(0)!;
        int hx = (hall.X + hall.MaxX) / 2, hz = (hall.Z + hall.MaxZ) / 2;
        var ai = new AiPlayer(0, 1, level);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => sim.Spells.GroveTrees > 0, 30 * Tps);
        var cast = log.OfType<GrowForestCommand>().FirstOrDefault();
        Assert.True(cast != null, $"{level}: a grove by its town");
        Assert.Equal(new[] { mage }, cast!.Units);
        Assert.Equal(1, ai.GrovesGrown);
        Assert.True(sim.Spells.GroveTrees >= AiPlayer.GroveTreesLeast, $"{level}: {sim.Spells.GroveTrees} trees grew");
        Assert.Equal(EconomySystem.StartingMana - SpellSystem.ManaCost(Spell.GrowForest), sim.Economy.Player(0).Mana);
        // The trees stand where its wood cutters reach them, but off the town hall's doorstep.
        var trunks = Trunks(sim, hx, hz, AiPlayer.GroveNear);
        Assert.NotEmpty(trunks);
        Assert.All(trunks, t =>
        {
            long d2 = (long)(t.X - hx) * (t.X - hx) + (long)(t.Z - hz) * (t.Z - hz);
            Assert.True(d2 >= (long)(AiPlayer.GroveFrom - 2) * (AiPlayer.GroveFrom - 2), $"{level}: a tree at {t.X},{t.Z} is too close to the hall");
        });

        // With trees near again it grows no more, even when it could.
        log.Clear();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, AiPlayer.GroveEveryTicks + 10 * Tps);
        Assert.DoesNotContain(log, c => c is GrowForestCommand);
    }

    [Fact]
    public void NoGroveWhileTreesStandNearTheTown()
    {
        var (sim, _) = Town();
        var hall = sim.Economy.TownHall(0)!;
        Assert.True(Trees.Grow(sim.World, hall.MaxX + 12, Ground, hall.MaxZ + 6, 5, 0, TreeKind.Broadleaf, 1));
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 15 * Tps);
        Assert.DoesNotContain(log, c => c is GrowForestCommand);
    }

    [Fact]
    public void NoGroveWithoutManaToSpareForAChasm()
    {
        var (sim, _) = Town();
        sim.Economy.Player(0).Mana = SpellSystem.ManaCost(Spell.GrowForest) + SpellSystem.ManaCost(Spell.Chasm) - 1;
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 15 * Tps);
        Assert.DoesNotContain(log, c => c is GrowForestCommand);
    }

    [Fact]
    public void NoGroveWithEnemiesByTheTown()
    {
        var (sim, _) = Town();
        var hall = sim.Economy.TownHall(0)!;
        int foe = sim.SpawnUnit(1, "spearman", hall.MaxX + 16, hall.MaxZ + 16);
        sim.Schedule(0, new StanceCommand(1, new[] { foe }, UnitStance.Hold));
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 15 * Tps);
        Assert.DoesNotContain(log, c => c is GrowForestCommand);
    }

    [Fact]
    public void EasyGrowsNoGroves()
    {
        var (sim, _) = Town();
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 15 * Tps);
        Assert.DoesNotContain(log, c => c is GrowForestCommand);
    }
}
