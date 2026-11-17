using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Town governors: a town hall run to a chosen focus, through the orders a player could give.</summary>
public class GovernorTests
{
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, 3));

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

    /// <summary>Player 0's town hall at (20, 20) on a flat map with goods in it.</summary>
    private static (Simulation Sim, Building Hall) Town(int wood = 300, int food = 300)
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 20, 20);
        hall.Stock[(int)Good.Wood] = wood;
        hall.Stock[(int)Good.Food] = food;
        hall.Stock[(int)Good.Stone] = 100;
        return (sim, hall);
    }

    private static int Count(Simulation sim, int owner, BuildingKind kind, bool built = false) =>
        sim.Buildings.All.Count(b => b.Owner == owner && b.Def.Kind == kind && !b.Planned && (!built || b.Active));

    [Fact]
    public void AGovernorIsSetOnlyOnThePlayersOwnTownHall()
    {
        var (sim, hall) = Town();
        var store = Place(sim, 0, "storehouse", 40, 40);
        sim.Schedule(0, new GovernCommand(1, hall.Id, GovernorFocus.Army)); // not theirs
        sim.Schedule(0, new GovernCommand(0, store.Id, GovernorFocus.Army)); // not a town hall
        sim.Step();
        Assert.Equal(GovernorFocus.None, hall.Governor);
        Assert.Equal(GovernorFocus.None, store.Governor);

        sim.Schedule(sim.Tick, new GovernCommand(0, hall.Id, GovernorFocus.Defence));
        sim.Step();
        Assert.Equal(GovernorFocus.Defence, hall.Governor);
        ulong governed = sim.StateHash();
        sim.Schedule(sim.Tick, new GovernCommand(0, hall.Id, GovernorFocus.None));
        sim.Step();
        Assert.Equal(GovernorFocus.None, hall.Governor);
        Assert.NotEqual(governed, sim.StateHash());
    }

    [Fact]
    public void IdleWorkersInTheSettlementAreMadeAutomaticButHeldOnesAreLeftAlone()
    {
        var (sim, hall) = Town();
        int near = sim.SpawnUnit(0, "worker", 30, 30);
        int held = sim.SpawnUnit(0, "worker", 31, 30);
        int far = sim.SpawnUnit(0, "worker", 60, 60); // beyond the settlement
        sim.Schedule(0, new MoveCommand(0, new[] { held }, 32, 32)); // the player's own order holds it
        sim.Run(5 * Tps);
        Assert.False(sim.Units.Auto[sim.Units.IndexOf(near)]);

        sim.Schedule(sim.Tick, new GovernCommand(0, hall.Id, GovernorFocus.Growth));
        sim.Run(AutomationSystem.GovernTicks + 1);
        Assert.True(sim.Units.Auto[sim.Units.IndexOf(near)]);
        Assert.False(sim.Units.Auto[sim.Units.IndexOf(held)]);
        Assert.False(sim.Units.Auto[sim.Units.IndexOf(far)]);
    }

    [Fact]
    public void TheHallTrainsWorkersUpToTheFocusTarget()
    {
        var (sim, hall) = Town(food: 5000);
        Place(sim, 0, "house", 40, 20);
        Place(sim, 0, "house", 40, 26);
        sim.Schedule(0, new GovernCommand(0, hall.Id, GovernorFocus.Army));
        int target = AutomationSystem.WorkerTarget(GovernorFocus.Army);
        int Workers() => Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Role == UnitRole.Worker);
        RunUntil(sim, () => Workers() >= target, 10 * 60 * Tps, "the workers to be trained");
        sim.Run(60 * Tps);
        Assert.Equal(target, Workers());
    }

    [Fact]
    public void AHouseGoesUpBeforeHousingRunsOut()
    {
        var (sim, hall) = Town();
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "worker", 18 + k, 30);
        int housing = sim.Economy.Housing(0);
        for (int k = 4; k < housing - AutomationSystem.HouseHeadroom + 1; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 40 + k / 10);
        Assert.Equal(0, Count(sim, 0, BuildingKind.House));
        sim.Schedule(0, new GovernCommand(0, hall.Id, GovernorFocus.Growth));
        sim.Run(AutomationSystem.GovernTicks + 1);
        Assert.Equal(1, Count(sim, 0, BuildingKind.House));
        RunUntil(sim, () => Count(sim, 0, BuildingKind.House, built: true) == 1, 3 * 60 * Tps, "the automatic workers to build it");
        Assert.True(sim.Economy.Housing(0) > housing);
    }

    [Fact]
    public void OnlyOneSiteGoesUpAtATime()
    {
        var (sim, hall) = Town(wood: 2000);
        for (int k = 0; k < 2; k++) sim.SpawnUnit(0, "worker", 18 + k, 30);
        sim.Schedule(0, new GovernCommand(0, hall.Id, GovernorFocus.Defence));
        for (int t = 0; t < 60 * Tps; t++)
        {
            sim.Step();
            Assert.True(sim.Buildings.All.Count(b => b.Owner == 0 && b.State == BuildingState.Site) <= 1, $"two sites at tick {sim.Tick}");
        }
    }

    [Fact]
    public void ADefenceGovernorPutsAWatchtowerAtEachCornerAndResearchesAndTrainsArchers()
    {
        var (sim, hall) = Town(wood: 3000, food: 3000);
        for (int k = 0; k < 6; k++) sim.SpawnUnit(0, "worker", 18 + k, 30);
        sim.Schedule(0, new GovernCommand(0, hall.Id, GovernorFocus.Defence));
        RunUntil(sim, () => Count(sim, 0, BuildingKind.Tower, built: true) == 4, 12 * 60 * Tps, "four watchtowers");
        Assert.Equal(1, Count(sim, 0, BuildingKind.Barracks, built: true));
        int cx = hall.X + hall.Def.SizeX / 2, cz = hall.Z + hall.Def.SizeZ / 2;
        var corners = sim.Buildings.All.Where(b => b.Owner == 0 && b.Def.IsTower)
            .Select(b => (Math.Sign(b.X + b.Def.SizeX / 2 - cx), Math.Sign(b.Z + b.Def.SizeZ / 2 - cz))).ToHashSet();
        Assert.Equal(4, corners.Count);
        Assert.DoesNotContain(corners, c => c.Item1 == 0 || c.Item2 == 0);

        sim.Run(2 * 60 * Tps);
        var soldiers = Enumerable.Range(0, sim.Units.Count)
            .Where(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Role != UnitRole.Worker)
            .Select(i => sim.Content.Units[sim.Units.Def[i]].Id).ToList();
        // Spearmen hold the town until archery is researched (the governor researches it); then archers.
        Assert.True(sim.Economy.HasTech(0, sim.Content.Tech("archery")));
        Assert.All(soldiers, id => Assert.Contains(id, new[] { "archer", "spearman" }));
        Assert.True(soldiers.Count(id => id == "archer") > soldiers.Count / 2, string.Join(",", soldiers));
    }

    [Fact]
    public void TakingTheGovernorAwayStopsItsOrders()
    {
        var (sim, hall) = Town();
        sim.SpawnUnit(0, "worker", 30, 30);
        var given = new List<(long Tick, Command Command)>();
        sim.Automation.Record = given;
        sim.Schedule(0, new GovernCommand(0, hall.Id, GovernorFocus.Growth));
        RunUntil(sim, () => given.Any(g => g.Command is TrainCommand), 10 * Tps, "the governor to train a worker");
        sim.Schedule(sim.Tick, new GovernCommand(0, hall.Id, GovernorFocus.None));
        sim.Step();
        given.Clear();
        sim.Run(60 * Tps);
        Assert.DoesNotContain(given, g => g.Command is TrainCommand or BuildCommand or AutomateCommand);
    }

    /// <summary>
    /// From a generated region's real start (a town hall with the starting goods and five workers) the player
    /// gives the hall a governor and researches farming, then gives no more orders for ten minutes.
    /// </summary>
    private static (Simulation Sim, List<(long Tick, Command Command)> Given) RunGoverned(GovernorFocus focus, int minutes,
        bool automation = true, List<(long Tick, Command Command)>? replay = null)
    {
        var sim = new Simulation(1234, 512, 64, 512);
        sim.PlaceStartingBuildings();
        var start = sim.Map.Starts[0];
        var hall = Assert.Single(sim.Buildings.All, b => b.Owner == 0);
        for (int k = 0; k < 5; k++) sim.SpawnUnit(0, "worker", start.X - 2 + k, hall.MaxZ + 2);
        sim.Schedule(0, new GovernCommand(0, hall.Id, focus));
        sim.Schedule(0, new ResearchCommand(0, "farming"));
        var given = new List<(long, Command)>();
        sim.Automation.Enabled = automation;
        sim.Automation.Record = given;
        if (replay != null)
            foreach (var (tick, cmd) in replay) sim.Schedule(tick, cmd);
        sim.Run(minutes * 60 * Tps);
        return (sim, given);
    }

    [Fact]
    public void AGrowthGovernorGrowsATownWithNoOtherOrders()
    {
        var (sim, given) = RunGoverned(GovernorFocus.Growth, 10);
        int people = sim.Economy.Population(0);
        Assert.InRange(people, AutomationSystem.WorkerTarget(GovernorFocus.Growth), 200);
        Assert.True(Count(sim, 0, BuildingKind.House, built: true) >= 2, "houses");
        Assert.True(Count(sim, 0, BuildingKind.Farm, built: true) >= 2, "farms");
        Assert.True(Count(sim, 0, BuildingKind.Storehouse) >= 1, "a storehouse");
        Assert.Equal(0, Count(sim, 0, BuildingKind.Barracks));
        Assert.False(sim.Economy.Starving(0));
        Assert.Contains(given, g => g.Command is AutomateCommand);
        Assert.Contains(given, g => g.Command is TrainCommand { Unit: "worker" });
    }

    [Fact]
    public void AnArmyGovernorRaisesAMixedArmyAndReplaysTheSame()
    {
        var (sim, given) = RunGoverned(GovernorFocus.Army, 10);
        Assert.Equal(1, Count(sim, 0, BuildingKind.Barracks, built: true));
        var kinds = Enumerable.Range(0, sim.Units.Count)
            .Where(i => sim.Units.Owner[i] == 0 && sim.Content.Units[sim.Units.Def[i]].Role != UnitRole.Worker)
            .Select(i => sim.Content.Units[sim.Units.Def[i]].Role).ToList();
        Assert.True(kinds.Count >= 4, $"only {kinds.Count} soldiers");
        Assert.True(kinds.Distinct().Count() >= 2, "one kind of soldier only");
        Assert.Contains(given, g => g.Command is ResearchCommand);
        Assert.False(sim.Economy.Starving(0));

        // The governor acts only through orders: replaying them with automation off ends the same.
        var (again, _) = RunGoverned(GovernorFocus.Army, 10, automation: false, replay: given);
        Assert.Equal(sim.StateHash(), again.StateHash());
    }

    [Fact]
    public void GovernorOrdersRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100 };
        log.Add(3, new GovernCommand(0, 7, GovernorFocus.Growth));
        log.Add(4, new GovernCommand(1, 2, GovernorFocus.Army));
        log.Add(5, new GovernCommand(1, 2, GovernorFocus.Defence));
        log.Add(9, new GovernCommand(0, 7, GovernorFocus.None));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, back.Commands);
        Assert.Contains("governor 1 2 defence", log.ToString());
    }
}
