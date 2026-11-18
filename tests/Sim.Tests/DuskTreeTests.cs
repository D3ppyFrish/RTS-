using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>The map's one dusk tree, its warden, healing it for the Dusk Staff, and the Orcs hacking it down.</summary>
public class DuskTreeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    // Players are given the factions in data order: the Settlers heal the tree, the Orcs raze it.
    private const int Settlers = 0, Orcs = 1, Wild = Simulation.WildPlayer;
    private const int TreeX = 48, TreeZ = 48;

    /// <summary>A flat world with the dusk tree in the middle and its warden beside it.</summary>
    private static Simulation WithTree(int size = 96)
    {
        var w = TestWorlds.Flat(size, 32, size, Ground);
        RegionGenerator.GrowDuskTree(w, TreeX, Ground, TreeZ, 5);
        var sim = TestWorlds.Sim(w);
        sim.DuskTree.Place();
        return sim;
    }

    /// <summary>A unit on the ground (not up in the tree's branches).</summary>
    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Ground + 1);

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int Warden(Simulation sim) => sim.Monsters.GetLair(sim.DuskTree.Lair)!.Pack[0];

    private static void KillWarden(Simulation sim)
    {
        sim.Units.RemoveAt(Index(sim, Warden(sim)));
        sim.Step();
        Assert.True(sim.DuskTree.Unguarded);
    }

    private static int Count(Simulation sim, BlockType t)
    {
        int n = 0;
        var w = sim.World;
        for (int y = 0; y < w.SizeY; y++)
        for (int z = TreeZ - 10; z <= TreeZ + 10; z++)
        for (int x = TreeX - 10; x <= TreeX + 10; x++)
            if (w.Get(x, y, z) == t) n++;
        return n;
    }

    private static int RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
        return maxTicks;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(1234UL)]
    public void EveryMapHasOneLargeBareDuskTreeInTheHillsWithItsWarden(ulong seed)
    {
        var sim = new Simulation(seed, 512, 64, 512);
        sim.PlaceStartingBuildings();
        var lairs = sim.SpawnLairs();
        var tree = sim.DuskTree;
        Assert.True(tree.Exists);
        Assert.Equal(Biome.Hills, sim.Map.BiomeAt(tree.X, tree.Z));
        // Roughly halfway between the starts.
        Assert.InRange(tree.X, 512 / 2 - 512 / 5, 512 / 2 + 512 / 5);

        // One tree: every bit of dusk wood on the map is within reach of its trunk, which is ten blocks tall.
        var w = sim.World;
        int wood = 0;
        for (int z = 0; z < 512; z++)
        for (int x = 0; x < 512; x++)
        for (int y = 1; y < 64; y++)
        {
            var b = w.Get(x, y, z);
            if (BlockInfo.IsDuskTree(b))
            {
                wood++;
                Assert.InRange(x, tree.X - 6, tree.X + 6);
                Assert.InRange(z, tree.Z - 6, tree.Z + 6);
            }
            Assert.NotEqual(BlockType.DuskLeaves, b); // bare until it is healed
        }
        Assert.True(wood >= 30, $"only {wood} blocks of dusk wood");
        int top = tree.Y;
        while (w.Get(tree.X, top + 1, tree.Z) == BlockType.DuskWood) top++;
        Assert.Equal(10, top - tree.Y + 1);
        // Its clearing: no other tree near.
        for (int z = tree.Z - 8; z <= tree.Z + 8; z++)
        for (int x = tree.X - 8; x <= tree.X + 8; x++)
        for (int y = 1; y < 64; y++)
            Assert.False(BlockInfo.IsTrunk(w.Get(x, y, z)));

        // The warden: a lair of its own beside the tree, apart from the starts' lairs.
        Assert.Equal(sim.Map.Starts.Count, lairs.Count);
        Assert.DoesNotContain(lairs, l => l.Id == tree.Lair);
        var lair = sim.Monsters.GetLair(tree.Lair)!;
        Assert.Equal("dusk_warden", lair.Monster.Id);
        Assert.Single(lair.Pack);
        Assert.InRange(Math.Abs(lair.X - tree.X) + Math.Abs(lair.Z - tree.Z), 1, 3);
        Assert.DoesNotContain(sim.Content.DuskWarden!, sim.Content.LairMonsters);
        Assert.False(tree.Unguarded);
    }

    [Fact]
    public void UnitsStandOnTheGroundUnderTheBranchesNotOnThem()
    {
        var sim = WithTree();
        var w = sim.World;
        int under = 0;
        for (int z = TreeZ - 6; z <= TreeZ + 6; z++)
        for (int x = TreeX - 6; x <= TreeX + 6; x++)
        {
            bool branch = false;
            for (int y = Ground + 2; y < 32; y++)
                if (BlockInfo.IsDuskTree(w.Get(x, y, z))) branch = true;
            int g = Walk.GroundLevel(w, x, z);
            // Spawning, and clicking under the tree, find the ground, not a branch's top.
            Assert.True(g == -1 || g == Ground + 1 || (g == Ground + 2 && BlockInfo.IsDuskTree(w.Get(x, Ground + 1, z))),
                $"column {x},{z} stands at {g}");
            if (branch && g == Ground + 1) under++;
        }
        Assert.True(under > 0, "no column under a branch");
    }

    [Fact]
    public void TheDuskTreeHasAWideFootThatLeavesRoomToStandRoundIt()
    {
        var sim = WithTree();
        var w = sim.World;
        // Roots all round the trunk at the ground, buttresses up its four sides.
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            Assert.Equal(BlockType.DuskWood, w.Get(TreeX + dx, Ground + 1, TreeZ + dz));
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            Assert.Equal(BlockType.DuskWood, w.Get(TreeX + dx, Ground + 2, TreeZ + dz));
        // The columns two from the trunk, where tenders stand, stay open below the branches.
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) == 2)
                for (int y = Ground + 1; y <= Ground + 4; y++)
                    Assert.Equal(BlockType.Air, w.Get(TreeX + dx, y, TreeZ + dz));
        // The trunk is still the one found, not a root.
        Assert.True(DuskTreeSystem.Find(w, out int x, out _, out int z));
        Assert.Equal((TreeX, TreeZ), (x, z));
    }

    [Fact]
    public void TheDuskTreeCannotBeCutOrDug()
    {
        var sim = WithTree();
        Assert.False(BlockInfo.IsDiggable(BlockType.DuskWood));
        Assert.False(BlockInfo.IsDiggable(BlockType.DuskBranch));
        Assert.False(Economy.Harvest.CanGather(BlockType.DuskWood));
        int worker = Spawn(sim, Settlers, "worker", TreeX - 3, TreeZ);
        sim.Schedule(0, new GatherCommand(Settlers, new[] { worker }, TreeX, Ground + 1, TreeZ));
        sim.Schedule(0, new DigCommand(Settlers, TreeX, Ground + 1, TreeZ));
        sim.Run(5 * Tps);
        Assert.Equal(BlockType.DuskWood, sim.World.Get(TreeX, Ground + 1, TreeZ));
    }

    [Fact]
    public void TheWardenGuardsTheTreeAndNobodyCanHealItUntilItFalls()
    {
        var sim = WithTree();
        int warden = Warden(sim);
        int priest = Spawn(sim, Settlers, "battle_priest", TreeX - 6, TreeZ);
        Assert.False(sim.DuskTree.CanUse(Settlers, new[] { Index(sim, priest) }));
        sim.Schedule(0, new DuskTreeCommand(Settlers, new[] { priest }));
        sim.Step();
        Assert.Empty(sim.DuskTree.Tending);
        // The warden goes after the priest.
        RunUntil(sim, () => sim.Units.Target[Index(sim, warden)] == priest, 2 * Tps, "the warden to attack");

        sim.Units.RemoveAt(Index(sim, priest));
        KillWarden(sim);
        priest = Spawn(sim, Settlers, "battle_priest", TreeX - 6, TreeZ);
        Assert.True(sim.DuskTree.CanUse(Settlers, new[] { Index(sim, priest) }));
    }

    [Fact]
    public void ABattlePriestHealsTheTreeWhichGrowsItsCanopyAndGivesItTheDuskStaff()
    {
        var sim = WithTree();
        KillWarden(sim);
        int priest = Spawn(sim, Settlers, "battle_priest", TreeX - 8, TreeZ);
        int spearman = Spawn(sim, Settlers, "spearman", TreeX - 8, TreeZ + 2);
        var def = sim.Content.Unit("battle_priest");
        int baseHp = sim.Economy.MaxHp(Settlers, def);
        Assert.False(sim.DuskTree.CanUse(Settlers, new[] { Index(sim, spearman) })); // it does not heal
        sim.Schedule(sim.Tick, new DuskTreeCommand(Settlers, new[] { priest, spearman }));
        sim.Step();
        Assert.Equal(new[] { priest }, sim.DuskTree.Tending);

        int took = RunUntil(sim, () => sim.DuskTree.Healed, 30 * Tps, "the tree to be healed");
        Assert.True(took >= DuskTreeSystem.TendTicks);
        Assert.Equal(Settlers, sim.DuskTree.HealedBy);
        Assert.True(Count(sim, BlockType.DuskLeaves) > 40, "the tree grows a canopy");
        Assert.Empty(sim.DuskTree.Tending);

        // The staff: more hit points, bigger heals.
        Assert.Equal(priest, sim.DuskTree.StaffBearer);
        int i = Index(sim, priest);
        Assert.True(sim.DuskTree.HasStaff(i));
        Assert.Equal(baseHp + DuskTreeSystem.StaffHp, sim.MaxHpOf(i));
        Assert.Equal(def.Heal + DuskTreeSystem.StaffHeal, sim.DuskTree.HealOf(i));

        // Healed once a match: nobody can start on it again.
        int other = Spawn(sim, Settlers, "battle_priest", TreeX - 8, TreeZ - 2);
        Assert.False(sim.DuskTree.CanUse(Settlers, new[] { Index(sim, other) }));
        Assert.Equal(def.Heal, sim.DuskTree.HealOf(Index(sim, other))); // others' heals as they were
    }

    [Fact]
    public void AnotherOrderStopsAPriestTendingTheTree()
    {
        var sim = WithTree();
        KillWarden(sim);
        int priest = Spawn(sim, Settlers, "battle_priest", TreeX - 3, TreeZ - 2);
        sim.Schedule(sim.Tick, new DuskTreeCommand(Settlers, new[] { priest }));
        sim.Run(5 * Tps);
        Assert.True(sim.DuskTree.TendProgress(priest) > 0);
        sim.Schedule(sim.Tick, new StopCommand(Settlers, new[] { priest }));
        sim.Run(DuskTreeSystem.TendTicks);
        Assert.False(sim.DuskTree.Healed);
        Assert.Empty(sim.DuskTree.Tending);
    }

    [Fact]
    public void DuskBloomHealsEveryAllyNearTheBearerThenWaits()
    {
        var sim = WithTree();
        KillWarden(sim);
        int priest = Spawn(sim, Settlers, "battle_priest", TreeX - 3, TreeZ - 2);
        sim.Schedule(sim.Tick, new DuskTreeCommand(Settlers, new[] { priest }));
        RunUntil(sim, () => sim.DuskTree.Healed, 30 * Tps, "the tree to be healed");

        int near = Spawn(sim, Settlers, "spearman", TreeX - 5, TreeZ - 2);
        int far = Spawn(sim, Settlers, "spearman", TreeX - 20, TreeZ - 2);
        int foe = Spawn(sim, Orcs, "spearman", TreeX - 4, TreeZ + 30);
        foreach (int id in new[] { near, far, priest }) sim.Units.Hp[Index(sim, id)] = 10;
        sim.Units.Hp[Index(sim, foe)] = 10;
        sim.Step(); // the new units are counted in the fighting's look-ups from the next tick
        Assert.True(sim.DuskTree.CanBloom(Index(sim, priest)));
        sim.Schedule(sim.Tick, new BloomCommand(Settlers, new[] { priest, near }));
        sim.Step();
        Assert.Equal(10 + DuskTreeSystem.BloomHeal, sim.Units.Hp[Index(sim, near)]);
        Assert.True(sim.Units.Hp[Index(sim, priest)] >= 10 + DuskTreeSystem.BloomHeal); // the bearer too
        Assert.True(sim.Units.Hp[Index(sim, far)] < 10 + DuskTreeSystem.BloomHeal);   // out of reach
        Assert.True(sim.Units.Hp[Index(sim, foe)] < 10 + DuskTreeSystem.BloomHeal);   // not an ally
        Assert.Equal(sim.Tick - 1, sim.DuskTree.LastBloom);

        // Then it waits.
        Assert.False(sim.DuskTree.CanBloom(Index(sim, priest)));
        int was = sim.Units.Hp[Index(sim, near)];
        sim.Schedule(sim.Tick, new BloomCommand(Settlers, new[] { priest }));
        sim.Step();
        Assert.InRange(sim.Units.Hp[Index(sim, near)], was, was + 4);
        sim.Run(DuskTreeSystem.BloomCooldownTicks);
        Assert.True(sim.DuskTree.CanBloom(Index(sim, priest)));

        // A unit without the staff cannot bloom.
        Assert.False(sim.DuskTree.CanBloom(Index(sim, near)));
    }

    [Fact]
    public void OrcsTameTheWardenAndHackTheTreeDownSoNobodyGetsTheStaff()
    {
        var sim = WithTree();
        int warden = Warden(sim);
        // Orcs beat the warden instead of killing it, and can recruit it.
        int spearman = Spawn(sim, Orcs, "spearman", TreeX + 2, TreeZ + 5);
        sim.Units.Hp[Index(sim, warden)] = 1;
        sim.Schedule(0, new AttackCommand(Orcs, new[] { spearman }, warden));
        RunUntil(sim, () => sim.Units.Owner[Index(sim, warden)] == Orcs, 5 * Tps, "the orcs beat the warden");
        Assert.True(sim.Units.Subdued[Index(sim, warden)]);
        sim.Step();
        Assert.True(sim.DuskTree.Unguarded);
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { warden }, TameChoice.Recruit));
        sim.Step();
        Assert.False(sim.Units.Subdued[Index(sim, warden)]);

        // Orcs cannot heal the tree; their fighters hack it down, together.
        var squad = Enumerable.Range(0, 3).Select(k => Spawn(sim, Orcs, "spearman", TreeX + 6, TreeZ - 2 + 2 * k)).ToArray();
        Assert.True(sim.DuskTree.CanUse(Orcs, squad.Select(id => Index(sim, id))));
        sim.Schedule(sim.Tick, new DuskTreeCommand(Orcs, squad));
        int took = RunUntil(sim, () => sim.DuskTree.Razed, 40 * Tps, "the tree to come down");
        Assert.True(took < DuskTreeSystem.RazeTicks, "three hack it down faster than one would");
        Assert.Equal(Orcs, sim.DuskTree.RazedBy);
        Assert.Equal(0, Count(sim, BlockType.DuskWood) + Count(sim, BlockType.DuskBranch));
        Assert.True(Count(sim, BlockType.TreeRubble) >= 10, "it lies as tree rubble");

        // Nobody gets the staff now.
        int priest = Spawn(sim, Settlers, "battle_priest", TreeX - 3, TreeZ);
        Assert.False(sim.DuskTree.CanUse(Settlers, new[] { Index(sim, priest) }));
        sim.Schedule(sim.Tick, new DuskTreeCommand(Settlers, new[] { priest }));
        sim.Run(DuskTreeSystem.TendTicks + Tps);
        Assert.False(sim.DuskTree.Healed);
        Assert.Equal(0, sim.DuskTree.StaffBearer);
    }

    [Fact]
    public void TheNewCommandsRoundTripThroughTheLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 dusk-tree 0 4,7\n9 bloom 1 12\n");
        Assert.Equal(new DuskTreeCommand(0, new[] { 4, 7 }), log.Commands[0].Command);
        Assert.Equal(new BloomCommand(1, new[] { 12 }), log.Commands[1].Command);
        Assert.Equal(log.ToString(), CommandLog.Parse(log.ToString()).ToString());
    }
}
