using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class WallTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int Lo = 30, Hi = 40; // the ring of wall round [31, 39] x [31, 39]
    private const int GateX = 35; // a gate in the ring's south side, at (35, 30)

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

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

    private static bool Inside(Simulation sim, int id)
    {
        int i = Index(sim, id);
        int x = sim.Units.X[i].FloorToInt(), z = sim.Units.Z[i].FloorToInt();
        return x > Lo && x < Hi && z > Lo && z < Hi;
    }

    private static Building Wall(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"no room for a {def} at {x}, {z}");

    /// <summary>A finished ring of wall for the player round [31, 39] x [31, 39], with a gate in its south side if asked.</summary>
    private static void Ring(Simulation sim, int owner, bool gate)
    {
        if (gate) Wall(sim, owner, "gate", GateX, Lo);
        for (int k = Lo; k <= Hi; k++)
        foreach (var (x, z) in new[] { (k, Lo), (k, Hi), (Lo, k), (Hi, k) })
            if (sim.Buildings.AtColumn(x, z) == null) Wall(sim, owner, "wall", x, z);
    }

    private static Building Store(Simulation sim, int owner, int stone, int wood = 0)
    {
        var store = sim.Economy.PlaceFinished(owner, sim.Content.Building("storehouse"), 10, 10)!;
        store.Stock[(int)Good.Stone] = stone;
        store.Stock[(int)Good.Wood] = wood;
        return store;
    }

    [Fact]
    public void WorkersBuildAWallAlongTheLineAColumnAtATime()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 100);
        var workers = new[] { sim.SpawnUnit(0, "worker", 22, 26), sim.SpawnUnit(0, "worker", 26, 26) };
        var def = sim.Content.Building("wall");
        sim.Schedule(0, new WallCommand(0, workers, "wall", 20, 30, 30, 36));
        sim.Step();

        var line = EconomySystem.WallLine(20, 30, 30, 36);
        Assert.Equal(11, line.Count);
        var walls = sim.Buildings.All.Where(b => b.Def == def).ToList();
        Assert.Equal(line.Count, walls.Count);
        Assert.All(line, c => Assert.Equal(def, sim.Buildings.AtColumn(c.X, c.Z)!.Def));
        Assert.Equal(100 - 2 * line.Count, store.Stock[(int)Good.Stone]); // paid for column by column
        Assert.All(walls, w => Assert.Equal(BuildingState.Site, w.State));
        // The workers start on their own stretches of it.
        Assert.NotEqual(sim.Units.JobTarget[Index(sim, workers[0])], sim.Units.JobTarget[Index(sim, workers[1])]);

        int lastFinished = -1;
        RunUntil(sim, () =>
        {
            int finished = walls.Count(w => w.Active);
            Assert.True(finished >= lastFinished);
            lastFinished = finished;
            return finished == walls.Count;
        }, 120 * Tps, "the whole wall to go up");
        foreach (var w in walls)
            for (int y = 0; y < def.WallHeight; y++)
                Assert.Equal(BlockType.StoneBrick, sim.World.Get(w.X, w.Y + y, w.Z));
        // Done: the workers stop once there is no more wall near them.
        sim.Run(Tps);
        Assert.All(workers, id => Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, id)]));
    }

    [Fact]
    public void AWallStopsWhereTheStoneRunsOutAndKeepsAGateItCrosses()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 9, wood: 4);
        int worker = sim.SpawnUnit(0, "worker", 22, 26);
        sim.Schedule(0, new WallCommand(0, new[] { worker }, "gate", 24, 30, 24, 30));
        sim.Schedule(0, new WallCommand(0, new[] { worker }, "wall", 20, 30, 30, 30));
        sim.Step();
        // The gate cost wood 4 and stone 2; the wall got stone for three more columns, from its start.
        Assert.Equal("gate", sim.Buildings.AtColumn(24, 30)!.Def.Id);
        for (int x = 20; x <= 22; x++) Assert.Equal("wall", sim.Buildings.AtColumn(x, 30)!.Def.Id);
        Assert.Null(sim.Buildings.AtColumn(23, 30));
        Assert.Equal(1, store.Stock[(int)Good.Stone]);
        Assert.Equal(0, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void WallsJoinUpButKeepTheirDistanceFromOtherBuildings()
    {
        var sim = Flat();
        var wall = sim.Content.Building("wall");
        var house = Wall(sim, 0, "house", 20, 20); // [20, 23] x [20, 23]
        Wall(sim, 0, "wall", 30, 30);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, wall, 31, 30, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, wall, 31, 31, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, wall, 30, 30, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, wall, 24, 21, out _, checkPlayer: false));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, wall, 25, 21, out _, checkPlayer: false));
        Assert.NotNull(house);
    }

    [Fact]
    public void AWallShutsTheEnemyOut()
    {
        var sim = Flat();
        // Before: the way in is open.
        int scout = sim.SpawnUnit(1, "spearman", 35, 20);
        sim.Schedule(0, new MoveCommand(1, new[] { scout }, 35, 35));
        RunUntil(sim, () => Inside(sim, scout), 30 * Tps, "the scout to walk in while there is no wall");

        sim = Flat();
        Ring(sim, 0, gate: false);
        int enemy = sim.SpawnUnit(1, "spearman", 35, 20);
        sim.Schedule(0, new MoveCommand(1, new[] { enemy }, 35, 35));
        sim.Step();
        RunUntil(sim, () => sim.Units.Order[Index(sim, enemy)] == UnitOrder.Idle, 60 * Tps, "the enemy to find no way in");
        sim.Run(5 * Tps);
        Assert.False(Inside(sim, enemy));
    }

    [Fact]
    public void AGateLetsOnlyItsOwnersUnitsThrough()
    {
        var sim = Flat();
        Ring(sim, 0, gate: true);
        var gate = sim.Buildings.AtColumn(GateX, Lo)!;
        Assert.Equal(BuildingKind.Gate, gate.Def.Kind);
        Assert.Equal(BlockType.Gate, sim.World.Get(GateX, gate.Y, Lo));
        Assert.Equal(BlockType.Gate, sim.World.Get(GateX, gate.Y + 1, Lo));
        Assert.Equal(BlockType.StoneBrick, sim.World.Get(GateX, gate.Y + 2, Lo));

        int own = sim.SpawnUnit(0, "spearman", 25, 20);
        sim.Schedule(0, new MoveCommand(0, new[] { own }, 35, 35));
        bool throughGate = false;
        RunUntil(sim, () =>
        {
            int i = Index(sim, own);
            throughGate |= sim.Units.X[i].FloorToInt() == GateX && sim.Units.Z[i].FloorToInt() == Lo;
            return Inside(sim, own);
        }, 40 * Tps, "the owner's unit to come in through the gate");
        Assert.True(throughGate);
        // And back out again.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { own }, 35, 20));
        RunUntil(sim, () => sim.Units.Z[Index(sim, own)].FloorToInt() < Lo, 40 * Tps, "the owner's unit to go out through the gate");

        sim = Flat();
        Ring(sim, 0, gate: true);
        int enemy = sim.SpawnUnit(1, "spearman", 35, 20);
        sim.Schedule(0, new MoveCommand(1, new[] { enemy }, 35, 35));
        for (int t = 0; t < 40 * Tps; t++)
        {
            sim.Step();
            int i = Index(sim, enemy);
            Assert.False(sim.Units.X[i].FloorToInt() == GateX && sim.Units.Z[i].FloorToInt() == Lo, "an enemy in the gate");
        }
        Assert.False(Inside(sim, enemy));
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, enemy)]);
    }

    [Fact]
    public void ABreachedWallFallsInPiecesAndLetsTheEnemyIn()
    {
        var sim = Flat();
        Ring(sim, 0, gate: false);
        var target = sim.Buildings.AtColumn(35, Hi)!;
        int y = target.Y;
        int sapper = sim.SpawnUnit(1, "sapper", 35, 46);
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { sapper }, target.Id));
        // The sapper knocks the bottom block out: what stood on it comes down as a piece, falling.
        RunUntil(sim, () => target.Lost > 0, 30 * Tps, "the first block to come out");
        Assert.Equal(BlockType.Air, sim.World.Get(35, y, Hi));
        Assert.NotEmpty(sim.Physics.Falling);
        RunUntil(sim, () => sim.Physics.Falling.Count == 0, 10 * Tps, "the pieces to land");
        // It is down: no wall left standing in the gap, its pieces are rubble on the ground.
        Assert.Null(sim.Buildings.Get(target.Id));
        for (int k = 0; k < 3; k++) Assert.NotEqual(BlockType.StoneBrick, sim.World.Get(35, y + k, Hi));
        int rubble = 0;
        for (int x = 33; x <= 37; x++)
        for (int z = Hi - 2; z <= Hi + 2; z++)
        for (int k = 0; k < 3; k++)
            if (sim.World.Get(x, y + k, z) == BlockType.StoneRubble) rubble++;
        Assert.True(rubble > 0, "rubble where it fell");
        // The walls beside it still stand.
        Assert.NotNull(sim.Buildings.AtColumn(34, Hi));
        Assert.NotNull(sim.Buildings.AtColumn(36, Hi));

        int spear = sim.SpawnUnit(1, "spearman", 35, 50);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spear }, 35, 35));
        RunUntil(sim, () => Inside(sim, spear), 40 * Tps, "the enemy to come in through the breach");
    }

    [Fact]
    public void UnitsSentAtAWalledInBuildingBreakThroughTheWall()
    {
        var sim = Flat();
        Ring(sim, 0, gate: false);
        var house = Wall(sim, 0, "house", 33, 33);
        var sappers = new[] { sim.SpawnUnit(1, "sapper", 34, 22), sim.SpawnUnit(1, "sapper", 36, 22) };
        int walls = sim.Buildings.All.Count(b => b.Def.IsWall);
        sim.Schedule(0, new AttackBuildingCommand(1, sappers, house.Id));
        RunUntil(sim, () => sappers.Any(id => sim.Buildings.Get(sim.Units.TargetBuilding[Index(sim, id)]) is { Def.IsWall: true }),
            60 * Tps, "the sappers to go for the wall instead");
        RunUntil(sim, () => sim.Buildings.All.Count(b => b.Def.IsWall) < walls, 60 * Tps, "a breach");
    }

    [Fact]
    public void WallsAreLoggedAndReplayTheSame()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64 };
        var cmd = new WallCommand(0, new[] { 3, 4 }, "wall", 20, 30, 30, 36);
        log.Add(5, cmd);
        log.Ticks = 10;
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(cmd, copy.Commands[0].Command);

        ulong Play()
        {
            var sim = Flat();
            Store(sim, 0, stone: 100, wood: 10);
            var ids = new[] { sim.SpawnUnit(0, "worker", 22, 26), sim.SpawnUnit(0, "worker", 26, 26) };
            sim.Schedule(0, new WallCommand(0, ids, "gate", 25, 33, 25, 33));
            sim.Schedule(0, new WallCommand(0, ids, "wall", 20, 30, 30, 36));
            sim.Run(10 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
