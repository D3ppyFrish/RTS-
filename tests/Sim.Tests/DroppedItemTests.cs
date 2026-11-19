using System;
using System.Linq;
using Rts.Sim.Sandbox;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Units drop the unique items they carry (issue #73): a relic or the Dusk Staff lies where its bearer fell,
/// and a unit of any player that can take it picks it up by walking over it.
/// </summary>
public class DroppedItemTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;

    private static Simulation Flat()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        sim.TestMode = true;
        return sim;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void Bear(Simulation sim, int id, string relic)
    {
        int i = Index(sim, id);
        var def = sim.Content.Relic(relic);
        sim.Units.Relic[i] = (short)(def.Index + 1);
        sim.Units.Hp[i] += def.Hp;
    }

    private static void Kill(Simulation sim, int id)
    {
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { id }, SandboxAction.Kill));
        for (int t = 0; t < 2 * Tps && sim.Units.Exists(id); t++) sim.Step();
        Assert.False(sim.Units.Exists(id));
    }

    [Fact]
    public void ARelicBearerThatDiesDropsItWhereItFell()
    {
        var sim = Flat();
        int bearer = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        Bear(sim, bearer, "giant_heart");
        sim.Step();
        int i = Index(sim, bearer);
        int x = sim.Units.X[i].FloorToInt(), y = sim.Units.Y[i], z = sim.Units.Z[i].FloorToInt();
        ulong before = sim.StateHash();

        Kill(sim, bearer);
        var item = Assert.Single(sim.Items.Items);
        Assert.Same(sim.Content.Relic("giant_heart"), item.Relic);
        Assert.False(item.Staff);
        Assert.Equal((x, y, z), (item.X, item.Y, item.Z));
        Assert.Equal(Settlers, item.DroppedBy);
        Assert.NotEqual(before, sim.StateHash());

        // It lasts: still there a long while later with nobody near.
        sim.Run(60 * Tps);
        Assert.Single(sim.Items.Items);
    }

    [Fact]
    public void AnEnemyFighterWalkingOverItPicksItUpButNotAWorkerOrABearer()
    {
        var sim = Flat();
        int bearer = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        Bear(sim, bearer, "bog_lantern");
        Kill(sim, bearer);
        var item = Assert.Single(sim.Items.Items);

        // A worker and a fighter that already bears a relic walk over it and leave it.
        int worker = sim.SpawnUnit(Orcs, "worker", 30, 30);
        int holder = sim.SpawnUnit(Orcs, "spearman", 30, 32);
        Bear(sim, holder, "crab_shell");
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { worker }, item.X, item.Z));
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { holder }, item.X, item.Z));
        sim.Run(15 * Tps);
        Assert.Single(sim.Items.Items);
        Assert.Equal(0, sim.Units.Relic[Index(sim, worker)]);
        Assert.Same(sim.Content.Relic("crab_shell"), sim.RelicOf(Index(sim, holder)));
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { worker, holder }, 40, 40));
        sim.Run(15 * Tps);

        // An enemy fighter without one takes it, with the lantern's damage.
        int orc = sim.SpawnUnit(Orcs, "spearman", 34, 20);
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { orc }, item.X, item.Z));
        for (int t = 0; t < 20 * Tps && sim.Items.Items.Count > 0; t++) sim.Step();
        Assert.Empty(sim.Items.Items);
        int o = Index(sim, orc);
        Assert.Same(sim.Content.Relic("bog_lantern"), sim.RelicOf(o));
        var def = sim.Content.Unit("spearman");
        Assert.Equal(def.Damage + sim.Content.Relic("bog_lantern").Damage, sim.Combat.AttackOf(o, def));
    }

    [Fact]
    public void APickedUpRelicGivesItsHitPoints()
    {
        var sim = Flat();
        int bearer = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        Bear(sim, bearer, "giant_heart");
        Kill(sim, bearer);
        var item = sim.Items.Items[0];
        int taker = sim.SpawnUnit(Settlers, "spearman", item.X, item.Z);
        int hp = sim.Units.Hp[Index(sim, taker)];
        sim.Step();
        Assert.Empty(sim.Items.Items);
        int j = Index(sim, taker);
        Assert.Equal(sim.MaxHpOf(j), sim.Units.Hp[j]);
        Assert.Equal(hp + sim.Content.Relic("giant_heart").Hp, sim.Units.Hp[j]);
    }

    [Fact]
    public void TheNearestOfTwoTakesItAndTheOtherNothing()
    {
        var sim = Flat();
        int bearer = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        Bear(sim, bearer, "crab_shell");
        Kill(sim, bearer);
        var item = sim.Items.Items[0];
        int a = sim.SpawnUnit(Settlers, "spearman", item.X, item.Z);
        int b = sim.SpawnUnit(Orcs, "spearman", item.X, item.Z);
        sim.Step();
        Assert.Empty(sim.Items.Items);
        int bearers = new[] { a, b }.Count(id => sim.Units.Relic[Index(sim, id)] != 0);
        Assert.Equal(1, bearers);
    }

    [Fact]
    public void ADroppedItemFallsWithTheGroundUnderIt()
    {
        var sim = Flat();
        int bearer = sim.SpawnUnit(Settlers, "spearman", 20, 20);
        Bear(sim, bearer, "giant_heart");
        Kill(sim, bearer);
        var item = sim.Items.Items[0];
        Assert.Equal(Ground + 1, item.Y);
        // Dig the ground out from under it, two blocks down.
        sim.World.Set(item.X, Ground, item.Z, BlockType.Air);
        sim.World.Set(item.X, Ground - 1, item.Z, BlockType.Air);
        sim.Step();
        Assert.Equal(Ground - 1, item.Y);
        // Rubble falls in on it: it comes up on top.
        sim.World.Set(item.X, Ground - 1, item.Z, BlockType.Stone);
        sim.Step();
        Assert.Equal(Ground, item.Y);
    }

    [Fact]
    public void TheSameDeathsAndPickupsGiveTheSameHash()
    {
        ulong Play()
        {
            var sim = Flat();
            int bearer = sim.SpawnUnit(Settlers, "spearman", 20, 20);
            Bear(sim, bearer, "wyrm_crystal");
            Kill(sim, bearer);
            int orc = sim.SpawnUnit(Orcs, "spearman", 30, 26);
            sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { orc }, 20, 20));
            sim.Run(10 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void TheDuskStaffIsDroppedAndOnlyAUnitThatHealsPicksItUp()
    {
        var w = TestWorlds.Flat(96, 32, 96, Ground);
        RegionGenerator.GrowDuskTree(w, 48, Ground, 48, 5);
        var sim = TestWorlds.Sim(w);
        sim.TestMode = true;
        sim.DuskTree.Place();
        sim.Units.RemoveAt(Index(sim, sim.Monsters.GetLair(sim.DuskTree.Lair)!.Pack[0]));
        sim.Step();
        int priest = sim.SpawnUnit(Settlers, "battle_priest", 40, 48, Ground + 1);
        sim.Schedule(sim.Tick, new DuskTreeCommand(Settlers, new[] { priest }));
        for (int t = 0; t < 40 * Tps && !sim.DuskTree.Healed; t++) sim.Step();
        Assert.Equal(priest, sim.DuskTree.StaffBearer);

        // The priest walks off into the open and dies there: the staff lies where it fell.
        sim.Schedule(sim.Tick, new MoveCommand(Settlers, new[] { priest }, 20, 20));
        sim.Run(20 * Tps);
        Kill(sim, priest);
        Assert.Equal(0, sim.DuskTree.StaffBearer);
        var item = Assert.Single(sim.Items.Items);
        Assert.True(item.Staff);

        // A spearman stands on it and leaves it; a priest takes it up, with the staff's hit points and Dusk Bloom.
        int spearman = sim.SpawnUnit(Orcs, "spearman", item.X, item.Z, item.Y);
        sim.Step();
        Assert.Single(sim.Items.Items);
        Assert.Equal(0, sim.DuskTree.StaffBearer);
        sim.Units.RemoveAt(Index(sim, spearman));
        int other = sim.SpawnUnit(Settlers, "battle_priest", item.X, item.Z, item.Y);
        sim.Step();
        Assert.Empty(sim.Items.Items);
        Assert.Equal(other, sim.DuskTree.StaffBearer);
        int o = Index(sim, other);
        Assert.Equal(sim.Economy.MaxHp(Settlers, sim.Content.Unit("battle_priest")) + DuskTreeSystem.StaffHp, sim.MaxHpOf(o));
        Assert.Equal(sim.MaxHpOf(o), sim.Units.Hp[o]);
    }
}
