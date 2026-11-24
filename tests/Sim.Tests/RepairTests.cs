using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Craft guilds' repair (issue #150): Settler workers mend their damaged buildings, walls, gates and towers a block at a
/// time, putting back lost hit points and knocked-out blocks for half the cost over all of the hit points, slowly under
/// fire. Without the tech, and for the Orcs, the order does nothing.
/// </summary>
public class RepairTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int WallX = 30, WallZ = 30;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit at {x}, {z}");

    /// <summary>A finished town hall of the player's holding plenty of everything.</summary>
    private static Building Hall(Simulation sim, int owner, int x, int z, int amount = 500)
    {
        var hall = Place(sim, owner, "town_hall", x, z);
        for (int g = 0; g < hall.Stock.Length; g++) hall.Stock[g] = amount;
        return hall;
    }

    /// <summary>Player 0's stone wall at (WallX, WallZ), with its top <paramref name="knocked"/> blocks knocked out.</summary>
    private static Building DamagedWall(Simulation sim, int knocked = 2, int owner = 0, int x = WallX, int z = WallZ)
    {
        var wall = Place(sim, owner, "stone_wall", x, z);
        int top = wall.Y + wall.Shape.Height - 1;
        for (int k = 0; k < knocked; k++) sim.World.Set(x, top - k, z, BlockType.Air);
        sim.Step(); // the blocks gone count against it
        Assert.True(wall.Damaged);
        Assert.True(wall.Health < wall.MaxHealth);
        return wall;
    }

    private static void Grant(Simulation sim, int player, params string[] techs)
    {
        foreach (var t in techs) sim.Economy.Grant(player, sim.Content.Tech(t));
    }

    private static int Run(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.True(done(), $"timed out waiting for {what}");
        return maxTicks;
    }

    private static int Stone(Simulation sim, int player) => sim.Economy.Stockpile(player)[(int)Good.Stone];

    [Fact]
    public void CraftGuildsGivesRepairNotFasterGathering()
    {
        var guilds = ContentDb.Default.Tech("settler_guilds");
        Assert.Equal("settlers", guilds.Faction?.Id);
        Assert.Contains(guilds.Effects, e => e.Type == TechEffectType.Repair);
        // Only the Craft guilds give repair.
        Assert.All(ContentDb.Default.Techs.Where(t => t.Id != "settler_guilds"),
            t => Assert.DoesNotContain(t.Effects, e => e.Type == TechEffectType.Repair));
    }

    [Fact]
    public void WorkersRepairADamagedWallToFullHitPointsAndPayItsStone()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        Grant(sim, 0, "tools", "settler_guilds");
        var wall = DamagedWall(sim);
        int top = wall.Y + wall.Shape.Height - 1;
        int lost = wall.MaxHealth - wall.Health;
        int stone = Stone(sim, 0);
        var workers = new[] { sim.SpawnUnit(0, "worker", WallX + 2, WallZ), sim.SpawnUnit(0, "worker", WallX - 2, WallZ) };
        sim.Schedule(sim.Tick, new RepairCommand(0, workers, wall.Id));

        Run(sim, () => !wall.Damaged, 60 * Tps, "the wall to be repaired");
        Assert.Equal(wall.MaxHealth, wall.Health);
        Assert.Equal(0, wall.Lost);
        // The knocked-out blocks are back where they stood.
        Assert.Equal(wall.Shape.Blocks[^1].Block, sim.World.Get(WallX, top, WallZ));
        Assert.Equal(wall.Shape.Blocks[^1].Block, sim.World.Get(WallX, top - 1, WallZ));
        // Half the wall's 10 stone for all of its hit points: its share for what was mended.
        int cost = sim.Content.Building("stone_wall").Cost.Single(c => c.Good == Good.Stone).Amount;
        int paid = stone - Stone(sim, 0);
        Assert.Equal(cost * lost / (2 * wall.MaxHealth), paid);
        Assert.True(paid > 0);
        // Done: the workers stop.
        sim.Step();
        Assert.All(workers, id => Assert.NotEqual(UnitJob.Repair, sim.Units.Job[sim.Units.IndexOf(id)]));
    }

    [Fact]
    public void WorkersSentToOneDamagedStretchOfWallRepairTheRestAlongIt()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        Grant(sim, 0, "tools", "settler_guilds");
        var walls = Enumerable.Range(0, 5).Select(k => DamagedWall(sim, knocked: 1 + k % 2, z: WallZ + k)).ToArray();
        var worker = sim.SpawnUnit(0, "worker", WallX + 2, WallZ);
        sim.Schedule(sim.Tick, new RepairCommand(0, new[] { worker }, walls[0].Id));
        Run(sim, () => walls.All(w => !w.Damaged), 2 * 60 * Tps, "the whole wall to be repaired");
        Assert.All(walls, w => Assert.Equal(w.MaxHealth, w.Health));
    }

    [Fact]
    public void WithoutCraftGuildsTheOrderIsRefused()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        var wall = DamagedWall(sim);
        int health = wall.Health, stone = Stone(sim, 0);
        int worker = sim.SpawnUnit(0, "worker", WallX + 2, WallZ);
        sim.Schedule(sim.Tick, new RepairCommand(0, new[] { worker }, wall.Id));
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
        Assert.Equal(health, wall.Health);
        Assert.Equal(stone, Stone(sim, 0));
        Assert.False(sim.Economy.Repairable(wall));
    }

    [Fact]
    public void AnOrcPlayerCannotRepair()
    {
        var sim = Flat();
        sim.SetFaction(0, "orcs");
        Hall(sim, 0, 4, 4);
        Grant(sim, 0, "tools");
        Assert.Equal(ResearchResult.OtherFaction, sim.Economy.CanResearch(0, sim.Content.Tech("settler_guilds")));
        Assert.DoesNotContain(sim.Content.Techs, t => (t.Faction == null || t.Faction.Id == "orcs") && t.Effects.Any(e => e.Type == TechEffectType.Repair));
        var wall = DamagedWall(sim);
        int health = wall.Health;
        int worker = sim.SpawnUnit(0, "worker", WallX + 2, WallZ);
        sim.Schedule(sim.Tick, new RepairCommand(0, new[] { worker }, wall.Id));
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.False(sim.Economy.CanRepair(0));
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
        Assert.Equal(health, wall.Health);
    }

    [Fact]
    public void RepairWithNothingToPayWithStops()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4, amount: 0);
        Grant(sim, 0, "tools", "settler_guilds");
        var wall = DamagedWall(sim);
        int health = wall.Health;
        int worker = sim.SpawnUnit(0, "worker", WallX + 2, WallZ);
        sim.Schedule(sim.Tick, new RepairCommand(0, new[] { worker }, wall.Id));
        Run(sim, () => sim.Units.Job[sim.Units.IndexOf(worker)] == UnitJob.None, 20 * Tps, "the worker to give up");
        Assert.Equal(health, wall.Health);
        Assert.False(sim.Economy.WantsRepair(wall)); // and automatic workers are not sent
    }

    [Fact]
    public void RepairsGoSlowlyUnderFire()
    {
        // The same wall, the same block knocked out of it, the same worker: once with nobody about, once with an
        // enemy spearman hacking at the far side of it. The block goes back later under fire.
        int TicksToPutBack(bool underFire)
        {
            var sim = Flat();
            Hall(sim, 0, 4, 4);
            Grant(sim, 0, "tools", "settler_guilds");
            var wall = DamagedWall(sim, knocked: 1);
            int top = wall.Y + wall.Shape.Height - 1;
            if (underFire)
            {
                int enemy = sim.SpawnUnit(1, "spearman", WallX - 1, WallZ);
                sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { enemy }, wall.Id));
                Run(sim, () => sim.Economy.UnderFire(wall), 10 * Tps, "the first blow");
            }
            int worker = sim.SpawnUnit(0, "worker", WallX + 1, WallZ);
            sim.Schedule(sim.Tick, new RepairCommand(0, new[] { worker }, wall.Id));
            return Run(sim, () => BlockInfo.IsBuilt(sim.World.Get(WallX, top, WallZ)), 60 * Tps, "the block to go back");
        }
        int calm = TicksToPutBack(false), fire = TicksToPutBack(true);
        Assert.True(fire >= calm * 3, $"under fire {fire} ticks, calm {calm}");
    }

    [Fact]
    public void AutomaticWorkersRepairOnceNoEnemyIsNear()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        Grant(sim, 0, "tools", "settler_guilds");
        var wall = DamagedWall(sim);
        // An enemy soldier standing near it, doing nothing: nobody goes to repair it.
        int enemy = sim.SpawnUnit(1, "spearman", WallX + 6, WallZ + 6);
        sim.Schedule(sim.Tick, new StanceCommand(1, new[] { enemy }, UnitStance.Hold));
        int worker = sim.SpawnUnit(0, "worker", WallX + 3, WallZ - 3);
        sim.Schedule(sim.Tick, new StanceCommand(0, new[] { worker }, UnitStance.Hold));
        sim.Schedule(sim.Tick, new AutomateCommand(0, new[] { worker }, true));
        for (int t = 0; t < 10 * Tps; t++) sim.Step();
        Assert.True(sim.Economy.EnemyNear(wall, EconomySystem.RepairSafeRadius));
        Assert.False(sim.Economy.WantsRepair(wall));
        Assert.NotEqual(UnitJob.Repair, sim.Units.Job[sim.Units.IndexOf(worker)]);
        Assert.True(wall.Damaged);

        // It goes: the automatic worker repairs the wall.
        sim.Units.Remove(enemy);
        Run(sim, () => !wall.Damaged, 60 * Tps, "the automatic worker to repair the wall");
        Assert.Equal(wall.MaxHealth, wall.Health);
    }

    [Fact]
    public void ASettlerComputerPlayerRepairsItsDamagedWall()
    {
        var sim = AiTests.WithStarts(96, (30, 48));
        var hall = sim.Economy.TownHall(0)!;
        foreach (var g in new[] { Good.Food, Good.Wood, Good.Stone }) hall.Stock[(int)g] = 500;
        Grant(sim, 0, "tools", "settler_guilds");
        var wall = DamagedWall(sim, x: hall.MaxX + 6, z: hall.Z);
        for (int k = 0; k < 3; k++) sim.SpawnUnit(0, "worker", hall.MaxX + 2, hall.MaxZ + 2 + k);
        var players = new[] { new AiPlayer(0, 1) };
        bool repairing = false;
        for (int t = 0; t < 2 * 60 * Tps && wall.Damaged; t++)
        {
            foreach (var ai in players) ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
            repairing |= Enumerable.Range(0, sim.Units.Count).Any(i => sim.Units.Job[i] == UnitJob.Repair && sim.Units.JobTarget[i] == wall.Id);
        }
        Assert.True(repairing);
        Assert.False(wall.Damaged);
    }

    [Fact]
    public void ARepairReplaysTheSame()
    {
        ulong Hash()
        {
            var sim = Flat();
            Hall(sim, 0, 4, 4);
            Grant(sim, 0, "tools", "settler_guilds");
            var wall = DamagedWall(sim);
            int worker = sim.SpawnUnit(0, "worker", WallX + 2, WallZ);
            sim.Schedule(sim.Tick, new RepairCommand(0, new[] { worker }, wall.Id));
            for (int t = 0; t < 10 * Tps; t++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Hash(), Hash());
    }
}
