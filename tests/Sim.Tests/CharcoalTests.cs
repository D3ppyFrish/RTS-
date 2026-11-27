using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Automation;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Automatic workers and computer players digging a forest fire's charred stumps for charcoal (see
/// <c>AutomationSystem.KindOf</c>, <c>AiPlayer.Charcoal.cs</c>, <see cref="FireSystem.Stumps"/>).
/// </summary>
public class CharcoalTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.True(done(), $"timed out waiting for {what}");
    }

    /// <summary>Lone trees at the given columns, each set alight at its foot and burnt down to a charred stump.</summary>
    private static void BurnTrees(Simulation sim, params (int X, int Z)[] at)
    {
        foreach (var (x, z) in at) TestWorlds.Tree(sim.World, x, z, 4);
        foreach (var (x, z) in at) Assert.True(sim.Fire.Ignite(x, Ground + 1, z, -1));
        RunUntil(sim, () => sim.Fire.Count == 0, 120 * Tps, "the trees to burn out");
        foreach (var (x, z) in at) Assert.Equal(BlockType.CharredWood, sim.World.Get(x, Ground + 1, z));
    }

    [Fact]
    public void TheFireKeepsTrackOfTheStumpsItLeavesUntilTheyAreDug()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        BurnTrees(sim, (20, 20), (40, 40));
        Assert.Equal(new[] { (20, Ground + 1, 20), (40, Ground + 1, 40) }, sim.Fire.Stumps().OrderBy(s => s.X).ToArray());
        sim.World.Set(20, Ground + 1, 20, BlockType.Air); // dug
        Assert.Equal(new[] { (40, Ground + 1, 40) }, sim.Fire.Stumps().ToArray());
        Assert.Equal(1, sim.Fire.StumpCount);
    }

    [Fact]
    public void AutomaticWorkersDigCharredStumpsForCharcoalOnceThereIsASmelter()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        var store = Place(sim, 0, "storehouse", 10, 10);
        BurnTrees(sim, (24, 24), (27, 24));
        int w = sim.SpawnUnit(0, "worker", 20, 20);
        sim.Schedule(sim.Tick, new AutomateCommand(0, new[] { w }, true));
        sim.Run(5 * Tps);
        // No smelter: coal is no use, and the stumps are left.
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(w)]);
        Assert.Equal(BlockType.CharredWood, sim.World.Get(24, Ground + 1, 24));

        Place(sim, 0, "smelter", 10, 30);
        RunUntil(sim, () => store.Stock[(int)Good.Coal] >= 2 * FireSystem.CharcoalPerStump, 120 * Tps, "both stumps' charcoal in store");
        Assert.Empty(sim.Fire.Stumps());
    }

    [Fact]
    public void AWorkerSetToGatherCoalDigsStumpsEvenWithoutASmelter()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        var store = Place(sim, 0, "storehouse", 10, 10);
        BurnTrees(sim, (24, 24));
        int w = sim.SpawnUnit(0, "worker", 20, 20);
        sim.Schedule(sim.Tick, new PriorityCommand(0, new[] { w }, WorkPriorities.Pack(WorkKind.Coal)));
        sim.Schedule(sim.Tick, new AutomateCommand(0, new[] { w }, true));
        RunUntil(sim, () => store.Stock[(int)Good.Coal] >= FireSystem.CharcoalPerStump, 60 * Tps, "the stump's charcoal in store");
    }

    /// <summary>
    /// A computer player's town at (10, 10) with a smelter; two burnt trees 44 columns out, seen by a rider of its; and
    /// a spare worker by its town hall, with stone beside it (which its automatic workers want more than coal, and find
    /// nearer).
    /// </summary>
    private static (Simulation Sim, int Worker) Town(int coal = 0)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        sim.Map.Starts.Add(new StartArea(0, 10, Ground + 1, 10));
        sim.PlaceStartingBuildings();
        Place(sim, 0, "smelter", 24, 12);
        BurnTrees(sim, (54, 24), (57, 24));
        sim.SpawnUnit(0, "rider", 55, 26);
        for (int x = 12; x <= 16; x++) sim.World.Set(x, Ground + 1, 23, BlockType.Stone);
        int w = sim.SpawnUnit(0, "worker", 10, 20);
        sim.Economy.Stockpile(0)[(int)Good.Coal] = 0;
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Coal] = coal;
        sim.Run(5);
        return (sim, w);
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

    private static bool ToStump(Simulation sim, Command c) =>
        c is GatherCommand g && sim.World.Get(g.X, g.Y, g.Z) == BlockType.CharredWood;

    [Theory]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ComputerPlayersSendWorkersToDigBurntStumpsForTheirSmelter(AiLevel level)
    {
        var (sim, worker) = Town();
        var ai = new AiPlayer(0, 1, level);
        var stump = ai.Stump(sim);
        Assert.NotNull(stump);
        var log = new List<Command>();
        log.AddRange(Play(sim, ai, 3 * Tps));
        var send = (GatherCommand)Assert.Single(log, c => c is GatherCommand g && (g.X, g.Z) == (stump!.Value.X, stump.Value.Z));
        Assert.True(send.Units.Length <= AiPlayer.CharcoalDiggers);
        Assert.Contains(worker, send.Units); // taken off the stone
        Assert.Equal(1, ai.CharcoalSends);
        Play(sim, ai, 120 * Tps, () => sim.Economy.Stock(0, Good.Coal) >= FireSystem.CharcoalPerStump);
        Assert.True(sim.Economy.Stock(0, Good.Coal) >= FireSystem.CharcoalPerStump, $"{level}: charcoal brought in");
    }

    [Fact]
    public void ComputerPlayersLeaveTheStumpsWhileTheyHaveCoalEnough()
    {
        var (sim, _) = Town(coal: AiPlayer.CharcoalWanted);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.DoesNotContain(Play(sim, ai, 5 * Tps), c => ToStump(sim, c));
        Assert.Equal(0, ai.CharcoalSends);
    }

    [Theory]
    [InlineData(AiLevel.Classic)]
    [InlineData(AiLevel.Easy)]
    public void TheClassicAndEasyPlayersDoNotSendWorkersToStumps(AiLevel level)
    {
        var (sim, _) = Town();
        var ai = new AiPlayer(0, 1, level);
        Play(sim, ai, 5 * Tps);
        Assert.Equal(0, ai.CharcoalSends);
    }

    [Fact]
    public void ComputerPlayersSendNoWorkersToStumpsByRivalSoldiersOrOutOfReach()
    {
        var (sim, _) = Town();
        sim.SpawnUnit(1, "spearman", 56, 20);
        sim.Run(5);
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        Assert.Null(ai.Stump(sim)); // a rival soldier stands by them

        var far = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
        far.Map.Starts.Add(new StartArea(0, 10, Ground + 1, 10));
        far.PlaceStartingBuildings();
        BurnTrees(far, (90, 90));
        far.SpawnUnit(0, "rider", 90, 92);
        far.Run(5);
        Assert.Null(new AiPlayer(0, 1, AiLevel.Normal).Stump(far)); // too far from its storage
    }
}
