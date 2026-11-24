using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Rts.Sim.Tests;

/// <summary>
/// The Deepfolk, the insect hive (see "The Deepfolk" in docs/rules.md): their own units and brood chamber in place of
/// everyone's (<see cref="FactionDef.UnitReplacements"/>, <see cref="FactionDef.BuildingReplacements"/>), swarms
/// hatching in batches (<see cref="UnitDef.Batch"/>), burrowers that tunnel fast, and units that strike harder for a
/// few seconds after they burst up out of the ground (<see cref="UnitDef.Burst"/>, <see cref="CombatSystem.StepBurst"/>).
/// </summary>
public class DeepfolkTests
{
    private readonly ITestOutputHelper _out;
    public DeepfolkTests(ITestOutputHelper output) => _out = output;

    private const int Ground = 10; // grass at 10, units on the surface stand at 11
    private const int Tunnel = 4;  // a tunnel's floor: units in it stand at 4, under rock from 6 to 10
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 24, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Count(Simulation sim, int owner, string unit)
    {
        int def = sim.Content.Unit(unit).Index, n = 0;
        for (int i = 0; i < sim.Units.Count; i++)
            if (sim.Units.Owner[i] == owner && sim.Units.Def[i] == def) n++;
        return n;
    }

    [Fact]
    public void TheDeepfolkAreTheThirdFaction()
    {
        var content = ContentDb.Default;
        var deep = content.Faction("deepfolk");
        Assert.Equal(2, deep.Index);
        Assert.Equal("Deepfolk", deep.Name);
        Assert.Null(deep.Experience);
        Assert.False(deep.TamesMonsters);
        Assert.Contains(("barracks", "brood_chamber"), deep.BuildingReplacements.Select(r => (r.From.Id, r.To.Id)));
        Assert.Contains(("sapper", "burrower"), deep.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        // The other factions replace nothing.
        Assert.Empty(content.Faction("settlers").UnitReplacements);
        Assert.Empty(content.Faction("orcs").BuildingReplacements);
    }

    [Fact]
    public void EachFactionStartsWithItsOwnUnits()
    {
        var content = ContentDb.Default;
        // Everyone's start units are those of no faction; the Settlers and Orcs start with just those, as before.
        Assert.DoesNotContain(content.StartUnits, u => u.Faction != null);
        Assert.Equal(content.StartUnits, content.Faction("settlers").StartUnits);
        Assert.Equal(content.StartUnits, content.Faction("orcs").StartUnits);
        // The Deepfolk start with as many, their own in place of those they replace.
        var deep = content.Faction("deepfolk").StartUnits;
        Assert.Equal(content.StartUnits.Count, deep.Count);
        Assert.Equal(new[] { "worker", "skitterling", "spitter", "skitterling", "burrower", "spitter" }, deep.Select(u => u.Id));

        var sim = Flat(96);
        sim.SetFaction(1, "deepfolk");
        sim.Map.Starts.Add(new StartArea(0, 20, Ground + 1, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Ground + 1, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(2, Count(sim, 0, "spearman"));
        Assert.Equal(0, Count(sim, 0, "skitterling"));
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "sapper") + Count(sim, 1, "mage") + Count(sim, 1, "rider"));
        Assert.Equal(2, Count(sim, 1, "worker"));
        Assert.Equal(4, Count(sim, 1, "skitterling"));
        Assert.Equal(4, Count(sim, 1, "spitter"));
        Assert.Equal(2, Count(sim, 1, "burrower"));
    }

    [Fact]
    public void TheDeepfolkBuildBroodChambersInsteadOfBarracks()
    {
        var sim = Flat();
        sim.SetFaction(1, "deepfolk");
        var econ = sim.Economy;
        var barracks = sim.Content.Building("barracks");
        var great = sim.Content.Building("great_barracks");
        var brood = sim.Content.Building("brood_chamber");
        Assert.False(econ.Unlocked(1, barracks));
        Assert.True(econ.Unlocked(1, brood));
        Assert.True(econ.Unlocked(0, barracks));
        Assert.False(econ.Unlocked(0, brood)); // another faction's
        Assert.True(sim.FactionOf(1).Replaces(great)); // an upgrade of what they replace
        Assert.Equal(new[] { "skitterling", "spitter", "burrower" }, brood.Trains);
        foreach (var id in new[] { "spearman", "archer", "rider", "sapper", "mage" })
        {
            Assert.False(econ.Unlocked(1, sim.Content.Unit(id)), id);
            Assert.True(econ.Unlocked(0, sim.Content.Unit(id)) || sim.Content.Unit(id).Requires.Count > 0, id);
        }
        Assert.True(econ.Unlocked(1, sim.Content.Unit("worker")));
        Assert.False(econ.Unlocked(0, sim.Content.Unit("skitterling")));
    }

    [Fact]
    public void ACapturedBarracksTrainsNothingForTheDeepfolk()
    {
        var sim = Flat();
        sim.SetFaction(1, "deepfolk");
        Place(sim, 1, "town_hall", 10, 10);
        var barracks = Place(sim, 1, "barracks", 30, 10); // as if taken from the Settlers
        var hall = sim.Economy.TownHall(1)!;
        hall.Stock[(int)Good.Food] = 500;
        hall.Stock[(int)Good.Wood] = 500;
        sim.Schedule(0, new TrainCommand(1, barracks.Id, "spearman"));
        sim.Schedule(0, new TrainCommand(1, barracks.Id, "healer"));
        sim.Step();
        Assert.Empty(barracks.Queue);
        Assert.Equal(500, hall.Stock[(int)Good.Food]);
    }

    [Fact]
    public void SkitterlingsHatchFourAtATimeForOneCost()
    {
        var sim = Flat();
        sim.SetFaction(0, "deepfolk");
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var brood = Place(sim, 0, "brood_chamber", 30, 10);
        var skitter = sim.Content.Unit("skitterling");
        Assert.Equal(4, skitter.Batch);
        hall.Stock[(int)Good.Food] = skitter.Cost.Single(c => c.Good == Good.Food).Amount;
        sim.Schedule(0, new TrainCommand(0, brood.Id, "skitterling"));
        sim.Run(skitter.TrainTicks + 2);
        Assert.Equal(4, Count(sim, 0, "skitterling"));
        Assert.Equal(0, hall.Stock[(int)Good.Food]);
        Assert.Empty(brood.Queue);
        // They step out side by side at the door, not on top of each other.
        var spots = Enumerable.Range(0, sim.Units.Count)
            .Where(i => sim.Units.Def[i] == skitter.Index)
            .Select(i => (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt())).Distinct().Count();
        Assert.True(spots >= 3, $"{spots} spots");
        Assert.All(Enumerable.Range(0, sim.Units.Count).Where(i => sim.Units.Def[i] == skitter.Index),
            i => Assert.True(brood.ColumnDistance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()) <= 3));
    }

    [Fact]
    public void ABatchWaitsForHousingForAllOfIt()
    {
        var sim = Flat();
        sim.SetFaction(0, "deepfolk");
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var brood = Place(sim, 0, "brood_chamber", 30, 10);
        hall.Stock[(int)Good.Food] = 500;
        int housing = sim.Economy.Housing(0);
        // Room for three more, not four.
        for (int k = 0; k < housing - 3; k++) Assert.True(sim.SpawnUnit(0, "worker", 4 + k % 40, 30 + k / 40 * 2) > 0);
        var skitter = sim.Content.Unit("skitterling");
        sim.Schedule(0, new TrainCommand(0, brood.Id, "skitterling"));
        sim.Run(skitter.TrainTicks * 2);
        Assert.Equal(0, Count(sim, 0, "skitterling"));
        Assert.Single(brood.Queue);

        // A house makes room, and the whole batch hatches (training starts once there is room, as for any unit).
        Place(sim, 0, "house", 10, 50);
        sim.Run(skitter.TrainTicks + Tps);
        Assert.Equal(4, Count(sim, 0, "skitterling"));
    }

    [Fact]
    public void ABurrowerTunnelsTwiceAsFastAsASapper()
    {
        // Two parallel tunnels through deep rock, one dug by a sapper and one by a burrower, for the same time.
        var w = TestWorlds.Flat(64, 24, 64, Ground);
        TestWorlds.Box(w, 10, Tunnel, 20, 12, Tunnel + 1, 20, BlockType.Air);
        TestWorlds.Box(w, 10, Tunnel, 40, 12, Tunnel + 1, 40, BlockType.Air);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(1, "deepfolk");
        int sapper = sim.SpawnUnit(0, "sapper", 11, 20, Tunnel);
        int burrower = sim.SpawnUnit(1, "burrower", 11, 40, Tunnel);
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 50, Tunnel, 20));
        sim.Schedule(0, new TunnelCommand(1, new[] { burrower }, 50, Tunnel, 40));
        sim.Run(40 * Tps);
        int dugBySapper = sim.Units.X[sim.Units.IndexOf(sapper)].FloorToInt() - 11;
        int dugByBurrower = sim.Units.X[sim.Units.IndexOf(burrower)].FloorToInt() - 11;
        Assert.True(dugBySapper >= 2, $"the sapper dug {dugBySapper}");
        Assert.True(dugByBurrower >= dugBySapper * 3 / 2, $"the burrower dug {dugByBurrower}, the sapper {dugBySapper}");
    }

    [Fact]
    public void BurstingUpOutOfTheGroundMakesTheirBlowsHarderForAFewSeconds()
    {
        // A tunnel through the rock that comes up a ramp into the open at x = 30.
        var w = TestWorlds.Flat(64, 24, 64, Ground);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(0, "deepfolk");
        var combat = sim.Combat;
        var skitter = sim.Content.Unit("skitterling");
        Assert.Equal(50, skitter.Burst);
        TestWorlds.Box(w, 10, Tunnel, 30, 20, Tunnel + 1, 30, BlockType.Air);
        int bug = sim.SpawnUnit(0, "skitterling", 15, 30, Tunnel);
        int surface = sim.SpawnUnit(0, "skitterling", 40, 40);
        sim.Run(CombatSystem.BurstLook * 2);
        int b = sim.Units.IndexOf(bug), s = sim.Units.IndexOf(surface);
        Assert.True(sim.Units.Burrowed[b]);
        Assert.False(sim.Units.Burrowed[s]);
        Assert.False(combat.Bursting(b) || combat.Bursting(s));
        Assert.Equal(skitter.Damage, combat.AttackOf(b, skitter));

        // The ground over it is dug away: it is out in the open, and strikes half as hard again for a few seconds.
        TestWorlds.Box(w, 13, Tunnel + 2, 30, 17, Ground, 30, BlockType.Air);
        sim.Run(CombatSystem.BurstLook);
        b = sim.Units.IndexOf(bug);
        Assert.True(combat.Bursting(b));
        Assert.False(sim.Units.Burrowed[b]);
        Assert.Equal(skitter.Damage * 150 / 100, combat.AttackOf(b, skitter));
        Assert.Equal(skitter.Damage, combat.AttackOf(sim.Units.IndexOf(surface), skitter)); // never went under
        sim.Run(CombatSystem.BurstSeconds * Tps);
        b = sim.Units.IndexOf(bug);
        Assert.False(combat.Bursting(b));
        Assert.Equal(skitter.Damage, combat.AttackOf(b, skitter));

        // Units without burst never get it, wherever they come up from.
        Assert.Equal(0, sim.Content.Unit("sapper").Burst);
    }

    /// <summary>
    /// A computer player goes by what its faction can build and train, so one playing the Deepfolk builds a brood
    /// chamber and hatches hive units, never a barracks or spearmen; and the match replays the same.
    /// </summary>
    [Fact]
    public void AComputerPlayerPlaysTheDeepfolkWithTheirOwnUnits()
    {
        // Hive units it ever had, by id: the ones it started with and every one hatched since (some may die in a raid).
        var hive = new SortedSet<int>();
        int everyones = 0;
        bool brood = false, replaced = false;
        int skitter = -1, spitter = -1;
        var result = AiMatch.Play(3, 256, 64, 256, 4 * 60 * Tps, levels: new[] { AiLevel.Normal }, fog: true,
            factions: new[] { "deepfolk", "settlers" }, watch: (sim, _) =>
            {
                if (sim.Tick % Tps != 0) return;
                if (skitter < 0) (skitter, spitter) = (sim.Content.Unit("skitterling").Index, sim.Content.Unit("spitter").Index);
                for (int i = 0; i < sim.Units.Count; i++)
                    if (sim.Units.Owner[i] == 0 && (sim.Units.Def[i] == skitter || sim.Units.Def[i] == spitter)) hive.Add(sim.Units.Id[i]);
                everyones += Count(sim, 0, "spearman") + Count(sim, 0, "archer") + Count(sim, 0, "sapper");
                brood |= sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Id == "brood_chamber");
                replaced |= sim.Buildings.All.Any(b => b.Owner == 0 && sim.FactionOf(0).Replaces(b.Def));
            });
        Assert.True(brood, "it built a brood chamber");
        Assert.False(replaced, "it built a barracks");
        Assert.Equal(0, everyones);
        Assert.True(hive.Count > 16, $"it hatched hive units beyond the 16 it started with ({hive.Count} in all)");
        var samples = MatchReplay.Run(CommandLog.Parse(result.Log.ToString()), 1200, out var replayed);
        var again = MatchReplay.Run(CommandLog.Parse(result.Log.ToString()), 1200, out _);
        Assert.Equal(samples.Select(h => h.Hash), again.Select(h => h.Hash));
        Assert.Equal("deepfolk", replayed.FactionOf(0).Id);
    }

    [Fact]
    public void ADeepfolkMatchPlaysTheSameTwice()
    {
        static ulong Play()
        {
            var w = TestWorlds.Flat(64, 24, 64, Ground);
            TestWorlds.Box(w, 10, Tunnel, 30, 20, Tunnel + 1, 30, BlockType.Air);
            var sim = TestWorlds.Sim(w);
            sim.SetFaction(0, "deepfolk");
            var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 2, 2)!;
            var brood = sim.Economy.PlaceFinished(0, sim.Content.Building("brood_chamber"), 30, 2)!;
            hall.Stock[(int)Good.Food] = 500;
            hall.Stock[(int)Good.Wood] = 500;
            int digger = sim.SpawnUnit(0, "burrower", 12, 30, Tunnel);
            sim.SpawnUnit(1, "spearman", 40, 30);
            sim.Schedule(0, new TrainCommand(0, brood.Id, "skitterling"));
            sim.Schedule(0, new TrainCommand(0, brood.Id, "spitter"));
            sim.Schedule(0, new TunnelCommand(0, new[] { digger }, 40, Ground + 1, 30));
            sim.Run(60 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Theory]
    [InlineData("brood_chamber")]
    [InlineData("queens_chamber")]
    public void TheirBuildingsAreMoundsOfHiveEarthLikeAntHills(string id)
    {
        var def = ContentLoader.LoadEmbedded().Building(id);
        Assert.True(def.Mound);
        var blocks = def.Blocks;
        for (int y = 0; y <= blocks.Max(b => b.Y); y++)
        {
            _out.WriteLine($"level {y}");
            for (int z = 0; z < def.SizeZ; z++)
                _out.WriteLine(string.Concat(Enumerable.Range(0, def.SizeX).Select(x =>
                    blocks.FirstOrDefault(b => b.X == x && b.Y == y && b.Z == z).Block switch
                    {
                        BlockType.Hive => '#', BlockType.Door => 'D', BlockType.Floor => '_', _ => '.',
                    })));
        }
        // Hive earth all through, no planks, thatch or tiles; a door in the middle of the front, a floor inside.
        Assert.All(blocks, b => Assert.Contains(b.Block, new[] { BlockType.Hive, BlockType.Door, BlockType.Floor }));
        Assert.Contains(blocks, b => b.Block == BlockType.Door && b.X == def.SizeX / 2 && b.Z == def.SizeZ - 1 && b.Y == 0);
        Assert.Contains(blocks, b => b.Block == BlockType.Floor);
        // Round at the foot: the footprint's corners are left open.
        Assert.DoesNotContain(blocks, b => b.Y == 0 && b.X == 0 && b.Z == 0);
        Assert.DoesNotContain(blocks, b => b.Y == 0 && b.X == def.SizeX - 1 && b.Z == def.SizeZ - 1);
        // Straight sides to the top of its walls, then narrowing level by level to a rounded top.
        int Width(int y) => blocks.Where(b => b.Y == y && b.Z == def.SizeZ / 2).Select(b => b.X).DefaultIfEmpty(-1).Max()
            - blocks.Where(b => b.Y == y && b.Z == def.SizeZ / 2).Select(b => b.X).DefaultIfEmpty(0).Min() + 1;
        int top = blocks.Max(b => b.Y);
        Assert.True(top > def.WallHeight, "it rises over its walls");
        Assert.Equal(Width(0), Width(def.WallHeight - 1));
        for (int y = def.WallHeight; y <= top; y++) Assert.True(Width(y) <= Width(y - 1), $"level {y} is no wider than the one under it");
        Assert.True(Width(top) < Width(0) / 2 + 1, "it comes to a narrow top");
        // Hollow: nothing inside the shell but the room, so it comes apart like a building.
        Assert.True(def.Toughness > 0);
    }

    [Fact]
    public void AMoundBreaksIntoEarthRubbleAndIsAsToughAsPlanks()
    {
        Assert.Equal(BlockType.Rubble, BlockInfo.RubbleOf(BlockType.Hive));
        Assert.Equal(BlockInfo.Toughness(BlockType.Planks), BlockInfo.Toughness(BlockType.Hive));
        Assert.True(BlockInfo.IsBuilt(BlockType.Hive));
        Assert.True(Rts.Sim.Meshing.StructureDetail.IsStructure(BlockType.Hive));
    }

    /// <summary>Hollow buildings everyone has: the Deepfolk put up each of them as a mound of hive earth.</summary>
    public static IEnumerable<object[]> HollowBuildings() =>
        ContentLoader.LoadEmbedded().Buildings
            .Where(d => !d.Mound && !d.IsRampart && d.Kind is not (BuildingKind.Farm or BuildingKind.Orchard or BuildingKind.Totem or BuildingKind.Light or BuildingKind.Bridge))
            .Select(d => new object[] { d.Id });

    [Theory]
    [MemberData(nameof(HollowBuildings))]
    public void EveryHollowBuildingOfTheirsIsAMound(string id)
    {
        var def = ContentLoader.LoadEmbedded().Building(id);
        var mound = def.Shaped(BuildingLook.Mound);
        Assert.NotSame(def, mound);
        Assert.Same(mound, def.Shaped(BuildingLook.Mound));
        Assert.Same(def, def.Shaped(BuildingLook.Standard));
        Assert.Same(mound, mound.Shaped(BuildingLook.Mound));
        Assert.False(mound.IsBase);
        // The same building in all but its blocks.
        Assert.Equal((def.Index, def.Id, def.Kind, def.SizeX, def.SizeZ, def.Housing), (mound.Index, mound.Id, mound.Kind, mound.SizeX, mound.SizeZ, mound.Housing));
        var blocks = mound.Blocks;
        Assert.All(blocks, b => Assert.Contains(b.Block, new[] { BlockType.Hive, BlockType.Door, BlockType.Floor }));
        Assert.Contains(blocks, b => b.Block == BlockType.Door && b.X == def.SizeX / 2 && b.Z == def.SizeZ - 1 && b.Y == 0);
        if (def.SizeX >= 3 && def.SizeZ >= 3) Assert.DoesNotContain(blocks, b => b.Y == 0 && b.X == 0 && b.Z == 0); // round at the foot
        // As tall as the house with its roof (a small one may come out a little taller), and as hard to bring down.
        Assert.InRange(mound.Height, def.Height, def.Height + 2);
        Assert.InRange(mound.Toughness, def.Toughness * 95 / 100, def.Toughness * 103 / 100);
    }

    [Theory]
    [InlineData("barrier")]
    [InlineData("stone_wall")]
    [InlineData("stone_gate")]
    [InlineData("watchtower")]
    public void TheirWallsGatesAndTowersAreHiveEarth(string id)
    {
        var def = ContentLoader.LoadEmbedded().Building(id);
        var hive = def.Shaped(BuildingLook.Mound);
        Assert.Equal(def.Blocks.Count, hive.Blocks.Count);
        for (int k = 0; k < def.Blocks.Count; k++)
        {
            var (a, b) = (def.Blocks[k], hive.Blocks[k]);
            Assert.Equal((a.X, a.Y, a.Z), (b.X, b.Y, b.Z));
            Assert.Equal(a.Block == BlockType.Gate ? BlockType.Gate : BlockType.Hive, b.Block);
        }
        Assert.Equal(def.Height, hive.Height);
        Assert.InRange(hive.Toughness, def.Toughness * 97 / 100, def.Toughness * 103 / 100);
    }

    [Fact]
    public void FieldsBridgesLightsAndTheirOwnMoundsKeepTheirShape()
    {
        var db = ContentLoader.LoadEmbedded();
        foreach (var id in new[] { "farm", "brood_chamber", "queens_chamber" })
            Assert.Same(db.Building(id), db.Building(id).Shaped(BuildingLook.Mound));
        foreach (var d in db.Buildings.Where(d => d.Kind is BuildingKind.Light or BuildingKind.Bridge or BuildingKind.Totem))
            Assert.Same(d, d.Shaped(BuildingLook.Mound));
    }

    [Fact]
    public void ABuildingTakesItsOwnersShape()
    {
        var w = TestWorlds.Flat(64, 24, 64, Ground);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(0, "deepfolk");
        sim.SetFaction(1, "settlers");
        var def = sim.Content.Building("town_hall");
        var hive = sim.Economy.PlaceFinished(0, def, 4, 4)!;
        var town = sim.Economy.PlaceFinished(1, def, 30, 4)!;
        Assert.Equal(BuildingLook.Mound, hive.Look);
        Assert.Equal(BuildingLook.Standard, town.Look);
        Assert.Same(def, hive.Def);
        Assert.Same(def.Shaped(BuildingLook.Mound), hive.Shape);
        Assert.Same(def, town.Shape);
        // The world has the shape's blocks: hive earth for the Deepfolk, planks and a roof for the Settlers.
        foreach (var t in hive.Shape.Blocks) Assert.Equal(t.Block, w.Get(hive.X + t.X, hive.Y + t.Y, hive.Z + t.Z));
        foreach (var t in town.Shape.Blocks) Assert.Equal(t.Block, w.Get(town.X + t.X, town.Y + t.Y, town.Z + t.Z));
        Assert.Contains(hive.Shape.Blocks, t => t.Block == BlockType.Hive);
        Assert.DoesNotContain(town.Shape.Blocks, t => t.Block == BlockType.Hive);
        Assert.Equal(hive.Shape.Toughness, hive.Built);
        // Plans show the owner's shape too.
        sim.Schedule(0, new PlanCommand(0, "house", 4, 30));
        sim.Run(1);
        Assert.Contains(sim.Buildings.All, b => b.Planned && b.Owner == 0 && b.Look == BuildingLook.Mound);
    }
}
