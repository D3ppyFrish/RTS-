using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Thick walls, gates and towers (issue #53): blows struck at them close up get little done, and a blast that
/// knocks a block out of one topples what stands above and beside it away from the blast, onto whoever is behind.
/// </summary>
public class ToppleTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // buildings stand on 4
    private const int Base = Ground + 1;
    private const int Front = 29, Core = 30, Back = 31; // a stone wall three columns thick along x, facing -z

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"no room for a {def} at {x}, {z}");

    /// <summary>A finished stone wall of player 0 from x0 to x1, with its facings in front (z 29) and behind (z 31).</summary>
    private static void StoneWall(Simulation sim, int x0, int x1)
    {
        for (int x = x0; x <= x1; x++)
        {
            Place(sim, 0, "stone_facing", x, Front);
            Place(sim, 0, "stone_wall", x, Core);
            Place(sim, 0, "stone_facing", x, Back);
        }
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static bool Solid(Simulation sim, int x, int y, int z) => BlockInfo.IsSolid(sim.World.Get(x, y, z));

    [Fact]
    public void CloseUpBlowsOnThickWallsGetLittleDoneAndBlastsDealFullDamage()
    {
        var db = Flat().Combat;
        var content = Rts.Sim.Content.ContentDb.Default;
        Fix64 M(string unit, string building) => db.StructureMultiplier(content.Unit(unit), content.Building(building));
        // Thick: wooden and stone walls, gates and their facings, and towers; a barrier is not.
        foreach (string id in new[] { "wooden_wall", "stone_wall", "stone_facing", "wooden_gate", "stone_gate", "stone_gate_facing", "watchtower", "stone_tower" })
            Assert.True(content.Building(id).Thick, id);
        foreach (string id in new[] { "barrier", "barrier_gate", "house", "barracks" })
            Assert.False(content.Building(id).Thick, id);
        // Melee hacking at a thick wall does much less than at a house or a barrier.
        Assert.Equal(Fix64.FromRatio(1, 4), M("spearman", "house"));
        Assert.Equal(Fix64.FromRatio(1, 4), M("spearman", "barrier"));
        Assert.Equal(Fix64.FromRatio(1, 10), M("spearman", "stone_wall"));
        Assert.Equal(Fix64.FromRatio(1, 10), M("spearman", "stone_tower"));
        // Arrows are not hacking: a quarter, as before. Sappers know how to bring a wall down.
        Assert.Equal(Fix64.FromRatio(1, 4), M("archer", "stone_wall"));
        Assert.Equal(Fix64.One, M("sapper", "stone_wall"));
        // Blasts (siege, boulders, strong magic) deal full damage to thick walls; a battle mage still does a
        // quarter to a house.
        foreach (string unit in new[] { "siege_engineer", "battle_mage", "archmage", "ogre_hurler" })
        {
            Assert.True(content.Unit(unit).Blast, unit);
            Assert.Equal(Fix64.One, M(unit, "stone_wall"));
        }
        Assert.False(content.Unit("mage").Blast);
        Assert.Equal(Fix64.FromRatio(1, 4), M("battle_mage", "house"));
    }

    [Fact]
    public void ASiegeEngineerBreaksAThickWallFarSoonerThanSoldiersHackingAtIt()
    {
        int Stands(string unit, int count)
        {
            var sim = Flat();
            var wall = Place(sim, 0, "wooden_wall", 30, 30);
            var units = Enumerable.Range(0, count).Select(k => sim.SpawnUnit(1, unit, 28 + k, 24)).ToArray();
            sim.Schedule(0, new AttackBuildingCommand(1, units, wall.Id));
            for (int t = 0; t < 900 * Tps; t++)
            {
                if (sim.Buildings.Get(wall.Id) == null) return t;
                sim.Step();
            }
            return int.MaxValue;
        }
        int siege = Stands("siege_engineer", 1), spears = Stands("spearman", 4);
        Assert.True(siege < 30 * Tps, $"one siege engineer brings down a column of wooden wall in under 30 seconds: {siege} ticks");
        Assert.True(spears > 2 * siege, $"four spearmen take more than twice as long: {spears} against {siege} ticks");
    }

    [Fact]
    public void ABlastTopplesTheWallAboveAndBesideItAwayFromTheBlastOntoThoseBehind()
    {
        var sim = Flat();
        StoneWall(sim, 18, 32);
        int top = Base + 5;
        Assert.True(Solid(sim, 25, top, Front) && Solid(sim, 25, top, Back));
        // Defenders stand right behind the wall, the siege engineer out in front of it.
        var defenders = Enumerable.Range(0, 3).SelectMany(dx => new[] { sim.SpawnUnit(0, "spearman", 24 + dx, Back + 1), sim.SpawnUnit(0, "spearman", 24 + dx, Back + 2) }).ToArray();
        sim.Schedule(0, new StanceCommand(0, defenders, UnitStance.Hold));
        int siege = sim.SpawnUnit(1, "siege_engineer", 25, Front - 6);
        var facing = sim.Buildings.AtColumn(25, Front)!;
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { siege }, facing.Id));

        // The first block it knocks out is the one under the top of the facing in front of it.
        for (int t = 0; t < 60 * Tps && Solid(sim, 25, top - 1, Front); t++) sim.Step();
        Assert.False(Solid(sim, 25, top - 1, Front), "the blast never knocked the block under the facing's top out");
        // What stood above it and beside it, through all three columns of the wall, comes away at once ...
        for (int dx = -1; dx <= 1; dx++)
        foreach (int z in new[] { Front, Core, Back })
        {
            Assert.False(Solid(sim, 25 + dx, top, z), $"({25 + dx}, {top}, {z}) still stands");
            if (dx != 0 || z != Front) Assert.False(Solid(sim, 25 + dx, top - 1, z), $"({25 + dx}, {top - 1}, {z}) still stands");
        }
        // ... and only that: the wall further along stands to its top, and below the blast it stands too.
        foreach (int z in new[] { Front, Core, Back })
        {
            Assert.True(Solid(sim, 23, top, z) && Solid(sim, 27, top, z));
            Assert.True(Solid(sim, 25, top - 2, z));
        }
        Assert.Contains(sim.Physics.Falling, s => s.Toppled && s.VelZ > 0); // thrown away from the blast
        Assert.Equal(sim.Economy.MaxHp(1, sim.Content.Unit("siege_engineer")), sim.Units.Hp[Index(sim, siege)]);

        // It comes down behind the wall, on the defenders, and none of it falls out in front.
        for (int t = 0; t < 5 * Tps && sim.Physics.Falling.Count > 0; t++) sim.Step();
        int crushed = defenders.Count(id => !sim.Units.Exists(id));
        Assert.True(crushed >= 3, $"the falling wall crushed {crushed} of the 6 defenders behind it");
        int Rubble(int z0, int z1) =>
            Enumerable.Range(18, 15).Sum(x => Enumerable.Range(z0, z1 - z0 + 1).Sum(z => Enumerable.Range(Base, 6).Count(y => BlockInfo.IsRubble(sim.World.Get(x, y, z)))));
        int behind = Rubble(Back + 1, Back + 10), before = Rubble(Front - 10, Front - 1);
        Assert.True(behind >= 15 && before <= 2, $"{behind} blocks of rubble behind the wall and {before} in front (only the block knocked out, rolling off)");
    }

    [Fact]
    public void BlowsCloseUpAndBlastsOnABarrierOnlyWearTheWallDownWhereTheyHit()
    {
        // Spearmen at a stone wall knock its top off, a block at a time, and nothing topples.
        var sim = Flat();
        StoneWall(sim, 18, 32);
        int top = Base + 5;
        var spears = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(1, "spearman", 24 + k, Front - 2)).ToArray();
        sim.Schedule(0, new AttackBuildingCommand(1, spears, sim.Buildings.AtColumn(25, Front)!.Id));
        for (int t = 0; t < 600 * Tps && Solid(sim, 25, top, Front); t++) sim.Step();
        Assert.False(Solid(sim, 25, top, Front));
        Assert.True(Solid(sim, 25, top - 1, Front));
        Assert.True(Solid(sim, 25, top, Core) && Solid(sim, 24, top, Front) && Solid(sim, 26, top, Front));
        Assert.DoesNotContain(sim.Physics.Falling, s => s.Toppled);

        // A barrier is not thick: a blast knocks its top off like any blow.
        sim = Flat();
        for (int x = 20; x <= 30; x++) Place(sim, 0, "barrier", x, 30);
        int siege = sim.SpawnUnit(1, "siege_engineer", 25, 23);
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { siege }, sim.Buildings.AtColumn(25, 30)!.Id));
        for (int t = 0; t < 60 * Tps && Solid(sim, 25, Base + 1, 30); t++) sim.Step();
        Assert.False(Solid(sim, 25, Base + 1, 30));
        Assert.True(Solid(sim, 24, Base + 1, 30) && Solid(sim, 26, Base + 1, 30));
    }
}
