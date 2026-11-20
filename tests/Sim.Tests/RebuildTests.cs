using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Rebuild orders: destroyed buildings are cleared and put back by automatic workers.</summary>
public class RebuildTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    private static int RubbleOn(Simulation sim, int x0, int z0, int x1, int z1)
    {
        int n = 0;
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        for (int y = 1; y < sim.World.SizeY - 1; y++)
            if (BlockInfo.IsRubble(sim.World.Get(x, y, z))) n++;
        return n;
    }

    /// <summary>
    /// Player 0's town with a house at (20, 20), two automatic workers who only build, rebuild orders as
    /// given, and four of player 1's spearmen sent to knock the house down.
    /// </summary>
    private static (Simulation Sim, Building House, int[] Spears) Raid(bool rebuild, List<(long, Command)>? record = null,
        bool automation = true, List<(long Tick, Command Command)>? replay = null)
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 300;
        hall.Stock[(int)Good.Food] = 300;
        hall.Stock[(int)Good.Stone] = 100;
        var house = Place(sim, 0, "house", 20, 20);
        sim.SpawnUnit(0, "worker", 10, 16);
        sim.SpawnUnit(0, "worker", 11, 16);
        var spears = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(1, "spearman", 18 + k, 27)).ToArray();
        sim.Schedule(0, new AutomateAllCommand(0, true));
        sim.Schedule(0, new DefaultPriorityCommand(0, WorkPriorities.Pack(WorkKind.Build)));
        if (rebuild) sim.Schedule(0, new RebuildCommand(0, true));
        sim.Schedule(0, new AttackBuildingCommand(1, spears, house.Id));
        sim.Automation.Enabled = automation;
        sim.Automation.Record = record;
        if (replay != null)
            foreach (var (tick, cmd) in replay) sim.Schedule(tick, cmd);
        return (sim, house, spears);
    }

    /// <summary>Steps until the house is down, then sends the spearmen away to the far corner.</summary>
    private static void KnockDownAndLeave(Simulation sim, Building house, int[] spears)
    {
        RunUntil(sim, () => sim.Buildings.Get(house.Id) == null, 120 * Tps, "the house to fall");
        sim.Schedule(sim.Tick, new MoveCommand(1, spears, 60, 60));
    }

    private static Building? HouseAt(Simulation sim, int x, int z) =>
        sim.Buildings.All.FirstOrDefault(b => b.Owner == 0 && b.Def.Id == "house" && b.X == x && b.Z == z);

    [Fact]
    public void ADestroyedHouseIsClearedAndBuiltAgainWhereItStood()
    {
        var (sim, house, spears) = Raid(rebuild: true);
        KnockDownAndLeave(sim, house, spears);
        Assert.Contains(sim.Automation.Ruins, r => r.Owner == 0 && r.Def.Id == "house" && r.X == 20 && r.Z == 20);
        Assert.True(RubbleOn(sim, 20, 20, 23, 23) > 0, "the house leaves rubble on its footprint");

        RunUntil(sim, () => HouseAt(sim, 20, 20) != null, 5 * 60 * Tps, "the house to be placed again");
        Assert.Equal(0, RubbleOn(sim, 20, 20, 23, 23));
        Assert.Empty(sim.Automation.Ruins);
        var again = HouseAt(sim, 20, 20)!;
        RunUntil(sim, () => again.Active, 3 * 60 * Tps, "the house to be built again");
    }

    [Fact]
    public void WithoutRebuildOrdersNothingComesBack()
    {
        var (sim, house, spears) = Raid(rebuild: false);
        KnockDownAndLeave(sim, house, spears);
        Assert.Empty(sim.Automation.Ruins);
        sim.Run(3 * 60 * Tps);
        Assert.Null(HouseAt(sim, 20, 20));
        Assert.True(RubbleOn(sim, 20, 20, 23, 23) > 0, "nobody clears it");
    }

    [Fact]
    public void NothingIsClearedOrPlacedWhileAnEnemyIsNear()
    {
        var (sim, house, spears) = Raid(rebuild: true);
        RunUntil(sim, () => sim.Buildings.Get(house.Id) == null, 120 * Tps, "the house to fall");
        sim.Run(10 * Tps); // the rubble settles
        int rubble = RubbleOn(sim, 20, 20, 23, 23);
        sim.Run(60 * Tps); // the spearmen stay where the house was
        Assert.Single(sim.Automation.Ruins);
        Assert.Null(HouseAt(sim, 20, 20));
        Assert.Equal(rubble, RubbleOn(sim, 20, 20, 23, 23));

        sim.Schedule(sim.Tick, new MoveCommand(1, spears, 60, 60));
        RunUntil(sim, () => HouseAt(sim, 20, 20) != null, 5 * 60 * Tps, "the house to be placed once they left");
    }

    [Fact]
    public void ARuinWaitsUntilThePlayerCanPay()
    {
        var (sim, house, spears) = Raid(rebuild: true);
        var hall = sim.Buildings.All.First(b => b.Def.Id == "town_hall");
        KnockDownAndLeave(sim, house, spears);
        int wood = sim.Content.Building("house").Cost.First(c => c.Good == Good.Wood).Amount;
        RunUntil(sim, () => RubbleOn(sim, 20, 20, 23, 23) == 0, 5 * 60 * Tps, "the rubble to be cleared");
        hall.Stock[(int)Good.Wood] = 0;
        sim.Run(10 * Tps);
        Assert.Null(HouseAt(sim, 20, 20));
        Assert.Single(sim.Automation.Ruins);
        hall.Stock[(int)Good.Wood] = wood;
        RunUntil(sim, () => HouseAt(sim, 20, 20) != null, 5 * Tps, "the house once there is wood");
    }

    [Fact]
    public void TurningRebuildOrdersOffForgetsTheRuins()
    {
        var (sim, house, spears) = Raid(rebuild: true);
        KnockDownAndLeave(sim, house, spears);
        Assert.Single(sim.Automation.Ruins);
        sim.Schedule(sim.Tick, new RebuildCommand(0, false));
        sim.Step();
        Assert.False(sim.Economy.Rebuilds(0));
        Assert.Empty(sim.Automation.Ruins);
    }

    [Fact]
    public void ABurnedStorehouseIsRebuiltButACapturedOneIsNot()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 300;
        var burned = Place(sim, 0, "storehouse", 30, 10);
        var taken = Place(sim, 0, "storehouse", 30, 30);
        sim.Schedule(0, new RebuildCommand(0, true));
        sim.Schedule(0, new BurnCommand(1, new[] { sim.SpawnUnit(1, "spearman", 40, 10) }, burned.Id));
        sim.Schedule(0, new CaptureCommand(1, new[] { sim.SpawnUnit(1, "spearman", 40, 30) }, taken.Id));
        RunUntil(sim, () => sim.Buildings.Get(burned.Id) == null && taken.Owner == 1, 60 * Tps, "the raids");
        var ruin = Assert.Single(sim.Automation.Ruins);
        Assert.Equal(("storehouse", 30, 10), (ruin.Def.Id, ruin.X, ruin.Z));
    }

    [Fact]
    public void AWallComesBackAtItsFirstTier()
    {
        var sim = Flat();
        var wall = Place(sim, 0, "stone_wall", 20, 20);
        Assert.NotNull(wall.Def.UpgradeOf);
        sim.Schedule(0, new RebuildCommand(0, true));
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { sim.SpawnUnit(1, "sapper", 22, 22) }, wall.Id));
        RunUntil(sim, () => sim.Buildings.Get(wall.Id) == null, 120 * Tps, "the wall to fall");
        var ruin = Assert.Single(sim.Automation.Ruins);
        var first = wall.Def;
        while (first.UpgradeOf != null) first = first.UpgradeOf;
        Assert.Equal(first, ruin.Def);
    }

    [Fact]
    public void ARebuildReplayedFromItsCommandsWithAutomationOffEndsTheSame()
    {
        var given = new List<(long Tick, Command Command)>();
        var (sim, house, spears) = Raid(rebuild: true, record: given);
        KnockDownAndLeave(sim, house, spears);
        long leave = sim.Tick;
        RunUntil(sim, () => HouseAt(sim, 20, 20) is { Active: true }, 8 * 60 * Tps, "the house to stand again");
        long end = sim.Tick;
        Assert.Contains(given, g => g.Command is BuildCommand { Building: "house", X: 20, Z: 20 });

        var replay = given.ToList();
        replay.Add((leave, new MoveCommand(1, spears, 60, 60)));
        var (again, _, _) = Raid(rebuild: true, automation: false, replay: replay);
        again.Run((int)end);
        Assert.Equal(sim.StateHash(), again.StateHash());
    }

    [Fact]
    public void RebuildOrdersRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100 };
        log.Add(3, new RebuildCommand(0, true));
        log.Add(9, new RebuildCommand(1, false));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, back.Commands);
    }
}
