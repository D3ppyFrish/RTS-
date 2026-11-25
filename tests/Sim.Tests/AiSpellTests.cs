using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Computer players and terrain magic: mages opening chasms under bunched enemies, and a shrine for mana.</summary>
public class AiSpellTests
{
    private const int Tps = Simulation.TicksPerSecond;

    /// <summary>A computer player's town with two mages before it, and enemy spearmen standing bunched in a row ahead.</summary>
    private static (Simulation Sim, int[] Mages, int[] Enemies) Field(int enemies)
    {
        var sim = AiTests.WithStarts(96, (30, 48));
        var hall = sim.Economy.TownHall(0)!;
        var mages = new[] { sim.SpawnUnit(0, "mage", hall.X + 2, hall.MaxZ + 4), sim.SpawnUnit(0, "mage", hall.X + 4, hall.MaxZ + 4) };
        var foes = Enumerable.Range(0, enemies).Select(k => sim.SpawnUnit(1, "spearman", hall.X + 1 + k, hall.MaxZ + 13)).ToArray();
        sim.Schedule(0, new StanceCommand(1, foes, UnitStance.Hold));
        return (sim, mages, foes);
    }

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void MagesOpenAChasmUnderBunchedEnemies(AiLevel level)
    {
        var (sim, mages, foes) = Field(4);
        var ai = new AiPlayer(0, 1, level);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => sim.Spells.Chasms.Count > 0, 10 * Tps);
        var cast = log.OfType<ChasmCommand>().FirstOrDefault();
        Assert.True(cast != null, $"{level}: a chasm under the enemy");
        Assert.All(cast!.Units, id => Assert.Contains(id, mages));
        Assert.Single(sim.Spells.Chasms);
        Assert.Equal(0, sim.Spells.Chasms[0].Owner);
        // Those it caught are down in it, below the ground they stood on.
        var u = sim.Units;
        int down = foes.Count(id => u.Exists(id) && u.Y[u.IndexOf(id)] < 4);
        Assert.True(down >= AiPlayer.ChasmLeast, $"{level}: {down} enemies in the chasm");
        Assert.All(mages.Where(u.Exists), id => Assert.True(u.Y[u.IndexOf(id)] >= 4, "its own mages stay out of it"));
        Assert.True(sim.Economy.Player(0).Mana < EconomySystem.StartingMana, "it paid mana");
    }

    [Fact]
    public void NoChasmForTooFewEnemies()
    {
        var (sim, _, _) = Field(AiPlayer.ChasmLeast - 1);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 6 * Tps);
        Assert.DoesNotContain(log, c => c is ChasmCommand);
        Assert.Empty(sim.Spells.Chasms);
    }

    [Fact]
    public void NoChasmWithoutTheMana()
    {
        var (sim, _, _) = Field(4);
        sim.Economy.Player(0).Mana = SpellSystem.ManaCost(Spell.Chasm) - 1;
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 6 * Tps);
        Assert.DoesNotContain(log, c => c is ChasmCommand);
    }

    [Fact]
    public void EasyMagesCastNothing()
    {
        var (sim, _, _) = Field(4);
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var log = new List<Command>();
        AiTests.PlayLogged(sim, new[] { ai }, log, () => false, 6 * Tps);
        Assert.DoesNotContain(log, c => c is ChasmCommand);
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void WithCrystalInStorageItLaysOutAShrine(AiLevel level)
    {
        var sim = AiTests.WithStarts(96, (30, 48));
        sim.Economy.Grant(0, sim.Content.Tech("basic_magic"));
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Stone] += 300;
        hall.Stock[(int)Good.Wood] += 300;
        hall.Stock[(int)Good.Food] += 300;
        var ai = new AiPlayer(0, 1, level);
        bool Shrine() => sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Shrine);
        AiTests.PlayLogged(sim, new[] { ai }, new List<Command>(), Shrine, 30 * Tps);
        Assert.False(Shrine(), $"{level}: no crystal, no shrine");
        hall.Stock[(int)Good.Crystal] += AiPlayer.ShrineCrystal;
        Assert.True(AiTests.PlayLogged(sim, new[] { ai }, new List<Command>(), Shrine, 60 * Tps), $"{level}: a shrine for its crystal");
    }
}
