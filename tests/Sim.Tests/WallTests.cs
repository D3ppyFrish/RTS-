using System;
using System.Collections.Generic;
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

    /// <summary>
    /// A finished ring of wall for the player round [31, 39] x [31, 39], with a gate in its south side if asked:
    /// of the tier named (barrier, wooden or stone).
    /// </summary>
    private static void Ring(Simulation sim, int owner, bool gate, string tier = "barrier")
    {
        if (gate) Wall(sim, owner, tier == "barrier" ? "barrier_gate" : tier + "_gate", GateX, Lo);
        for (int k = Lo; k <= Hi; k++)
        foreach (var (x, z) in new[] { (k, Lo), (k, Hi), (Lo, k), (Hi, k) })
            if (sim.Buildings.AtColumn(x, z) == null) Wall(sim, owner, tier == "barrier" ? "barrier" : tier + "_wall", x, z);
    }

    /// <summary>A finished line of wall of the type along z from x0 to x1.</summary>
    private static Building[] Line(Simulation sim, int owner, string def, int x0, int x1, int z) =>
        Enumerable.Range(x0, x1 - x0 + 1).Select(x => Wall(sim, owner, def, x, z)).ToArray();

    /// <summary>A match in test mode, where a player can be given a tech at once.</summary>
    private static Simulation FlatWithTechs(params string[] techs)
    {
        var sim = Flat();
        sim.TestMode = true;
        foreach (string tech in techs) sim.Schedule(0, new SandboxTechCommand(0, 0, tech));
        return sim;
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
        var store = Store(sim, 0, stone: 0, wood: 100);
        var workers = new[] { sim.SpawnUnit(0, "worker", 22, 26), sim.SpawnUnit(0, "worker", 26, 26) };
        var def = sim.Content.Building("barrier");
        sim.Schedule(0, new WallCommand(0, workers, "barrier", 20, 30, 30, 36));
        sim.Step();

        var line = EconomySystem.WallLine(20, 30, 30, 36);
        Assert.Equal(11, line.Count);
        var walls = sim.Buildings.All.Where(b => b.Def == def).ToList();
        Assert.Equal(line.Count, walls.Count);
        Assert.All(line, c => Assert.Equal(def, sim.Buildings.AtColumn(c.X, c.Z)!.Def));
        Assert.Equal(100 - line.Count, store.Stock[(int)Good.Wood]); // a wood a column, paid for column by column
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
        // A wall starts as a wooden barrier: two blocks of stakes.
        Assert.Equal(2, def.WallHeight);
        foreach (var w in walls)
            for (int y = 0; y < def.WallHeight; y++)
                Assert.Equal(BlockType.Palisade, sim.World.Get(w.X, w.Y + y, w.Z));
        // Done: the workers stop once there is no more wall near them.
        sim.Run(Tps);
        Assert.All(workers, id => Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, id)]));
    }

    [Fact]
    public void ABuilderStandingWhereTheNextColumnGoesStepsAside()
    {
        // Two columns of wall side by side, each with its builder standing on the other: a block waits while
        // a unit stands where it goes, so each would wait on the other for good. A builder at work on
        // another site is sent off this one, and works on from beside its own.
        var sim = Flat();
        Store(sim, 0, stone: 0, wood: 10);
        sim.Schedule(0, new WallCommand(0, Array.Empty<int>(), "barrier", 20, 30, 21, 30));
        sim.Step();
        var left = sim.Buildings.AtColumn(20, 30)!;
        var right = sim.Buildings.AtColumn(21, 30)!;
        int onLeft = sim.SpawnUnit(0, "worker", 20, 30), onRight = sim.SpawnUnit(0, "worker", 21, 30);
        sim.Schedule(sim.Tick, new WorkCommand(0, new[] { onLeft }, right.Id));
        sim.Schedule(sim.Tick, new WorkCommand(0, new[] { onRight }, left.Id));
        sim.Step();
        Assert.Equal((right.Id, left.Id), (sim.Units.JobTarget[Index(sim, onLeft)], sim.Units.JobTarget[Index(sim, onRight)]));
        RunUntil(sim, () => left.Active && right.Active, 30 * Tps, "both columns to go up");
        foreach (int id in new[] { onLeft, onRight })
            Assert.Null(sim.Buildings.AtColumn(sim.Units.X[Index(sim, id)].FloorToInt(), sim.Units.Z[Index(sim, id)].FloorToInt()));
    }

    [Fact]
    public void AWallStopsWhereTheWoodRunsOutAndKeepsAGateItCrosses()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 0, wood: 6);
        int worker = sim.SpawnUnit(0, "worker", 22, 26);
        sim.Schedule(0, new WallCommand(0, new[] { worker }, "barrier_gate", 24, 30, 24, 30));
        sim.Schedule(0, new WallCommand(0, new[] { worker }, "barrier", 20, 30, 30, 30));
        sim.Step();
        // The gate cost wood 3; the wall got wood for three more columns, from its start.
        Assert.Equal("barrier_gate", sim.Buildings.AtColumn(24, 30)!.Def.Id);
        for (int x = 20; x <= 22; x++) Assert.Equal("barrier", sim.Buildings.AtColumn(x, 30)!.Def.Id);
        Assert.Null(sim.Buildings.AtColumn(23, 30));
        Assert.Equal(0, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void WallsJoinUpButKeepTheirDistanceFromOtherBuildings()
    {
        var sim = Flat();
        var wall = sim.Content.Building("barrier");
        var house = Wall(sim, 0, "house", 20, 20); // [20, 23] x [20, 23]
        Wall(sim, 0, "barrier", 30, 30);
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
        Assert.Equal(BlockType.Palisade, sim.World.Get(GateX, gate.Y + 2, Lo)); // the beam over the leaves

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
        for (int k = 0; k < 3; k++) Assert.NotEqual(BlockType.Palisade, sim.World.Get(35, y + k, Hi));
        int rubble = 0;
        for (int x = 33; x <= 37; x++)
        for (int z = Hi - 2; z <= Hi + 2; z++)
        for (int k = 0; k < 3; k++)
            if (sim.World.Get(x, y + k, z) == BlockType.WoodRubble) rubble++;
        Assert.True(rubble > 0, "rubble where it fell");
        // The walls beside it still stand.
        Assert.NotNull(sim.Buildings.AtColumn(34, Hi));
        Assert.NotNull(sim.Buildings.AtColumn(36, Hi));

        int spear = sim.SpawnUnit(1, "spearman", 35, 50);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spear }, 35, 35));
        RunUntil(sim, () => Inside(sim, spear), 40 * Tps, "the enemy to come in through the breach");
    }

    [Theory]
    [InlineData("barrier", false)]
    [InlineData("barrier", true)]
    [InlineData("wooden", false)]
    [InlineData("wooden", true)]
    [InlineData("stone", false)]
    [InlineData("stone", true)]
    public void ABrokenWallOfAnyTierLeavesAGapThatIsWalkedThroughBothWays(string tier, bool fromInside)
    {
        var sim = Flat();
        Ring(sim, 0, gate: false, tier);
        var target = sim.Buildings.AtColumn(35, Hi)!;
        int height = target.Def.Height;
        int sapper = sim.SpawnUnit(1, "sapper", 35, fromInside ? Hi - 3 : Hi + 3);
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { sapper }, target.Id));
        RunUntil(sim, () => sim.Buildings.Get(target.Id) == null && sim.Physics.Falling.Count == 0, 60 * Tps, "the wall to come down");
        // Its rubble spreads out instead of heaping in the gap: all of it is there, none of it out of reach.
        int rubble = 0;
        for (int x = 35 - height; x <= 35 + height; x++)
        for (int z = Hi - height; z <= Hi + height; z++)
        for (int y = Ground + 1; y <= Ground + height; y++)
            if (BlockInfo.IsRubble(sim.World.Get(x, y, z))) rubble++;
        Assert.Equal(height, rubble);
        Assert.False(BlockInfo.IsSolid(sim.World.Get(35, Ground + 3, Hi)));
        if (height <= 3) Assert.False(BlockInfo.IsSolid(sim.World.Get(35, Ground + 2, Hi)), "a heap in the gap");

        int inward = sim.SpawnUnit(1, "spearman", 35, 50), outward = sim.SpawnUnit(1, "spearman", 35, 35);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { inward }, 33, 35));
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { outward }, 33, 50));
        RunUntil(sim, () => Inside(sim, inward) && sim.Units.Z[Index(sim, outward)].FloorToInt() > Hi + 2, 40 * Tps,
            "one to walk in through the gap and one to walk out");
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
        var cmd = new WallCommand(0, new[] { 3, 4 }, "barrier", 20, 30, 30, 36);
        log.Add(5, cmd);
        log.Ticks = 10;
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(cmd, copy.Commands[0].Command);

        ulong Play()
        {
            var sim = Flat();
            Store(sim, 0, stone: 100, wood: 100);
            var ids = new[] { sim.SpawnUnit(0, "worker", 22, 26), sim.SpawnUnit(0, "worker", 26, 26) };
            sim.Schedule(0, new WallCommand(0, ids, "barrier_gate", 25, 33, 25, 33));
            sim.Schedule(0, new WallCommand(0, ids, "barrier", 20, 30, 30, 36));
            sim.Run(10 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    // --- Tiers and upgrades ------------------------------------------------------------------------

    [Fact]
    public void AWallIsLaidAsABarrierAndTheHigherTiersAreOnlyReachedByUpgrading()
    {
        var sim = FlatWithTechs("masonry");
        var store = Store(sim, 0, stone: 100, wood: 100);
        var db = sim.Content;
        Assert.Equal(new[] { 0, 1, 2 }, new[] { "barrier", "wooden_wall", "stone_wall" }.Select(id => db.Building(id).Tier));
        Assert.Equal(new[] { 0, 1, 2 }, new[] { "barrier_gate", "wooden_gate", "stone_gate" }.Select(id => db.Building(id).Tier));
        Assert.Equal(db.Building("wooden_wall"), db.Building("barrier").Upgrade);
        Assert.Equal(db.Building("stone_wall"), db.Building("wooden_wall").Upgrade);
        Assert.Null(db.Building("stone_wall").Upgrade);
        Assert.Equal(db.Building("wooden_gate"), db.Building("stone_gate").UpgradeOf);
        // Each tier is taller or as tall, and only the two upper ones have a walkway.
        Assert.Equal(new[] { 2, 4, 6 }, new[] { "barrier", "wooden_wall", "stone_wall" }.Select(id => db.Building(id).Height));
        Assert.Equal(new[] { 3, 4, 6 }, new[] { "barrier_gate", "wooden_gate", "stone_gate" }.Select(id => db.Building(id).Height));
        Assert.Equal(new[] { false, true, true }, new[] { "barrier", "wooden_wall", "stone_wall" }.Select(id => db.Building(id).Walkway));
        Assert.Equal(new[] { false, true, true }, new[] { "barrier_gate", "wooden_gate", "stone_gate" }.Select(id => db.Building(id).Walkway));

        sim.Step(); // the tech
        // Nor is a facing laid by itself: it goes up beside the wall it faces.
        foreach (string id in new[] { "wooden_wall", "stone_wall", "wooden_gate", "stone_gate", "stone_facing", "stone_gate_facing" })
        {
            Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(0, db.Building(id), 20, 30, out _));
            sim.Schedule(sim.Tick, new WallCommand(0, Array.Empty<int>(), id, 20, 30, 24, 30));
            sim.Schedule(sim.Tick, new BuildCommand(0, Array.Empty<int>(), id, 20, 34));
        }
        sim.Step();
        Assert.DoesNotContain(sim.Buildings.All, b => b.Def.IsWall);
        Assert.Equal(100, store.Stock[(int)Good.Wood]);
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, db.Building("barrier"), 20, 30, out _));
    }

    [Fact]
    public void WorkersUpgradeABarrierToAWoodenWallAndThenToStone()
    {
        var sim = FlatWithTechs();
        var store = Store(sim, 0, stone: 100, wood: 100);
        var walls = Line(sim, 0, "barrier", 20, 23, 30);
        var gate = Wall(sim, 0, "barrier_gate", 24, 30);
        int worker = sim.SpawnUnit(0, "worker", 22, 33);
        var db = sim.Content;

        Assert.All(walls, w => Assert.Equal(PlaceResult.Ok, sim.Economy.CanUpgrade(w)));
        sim.Schedule(0, new UpgradeCommand(0, new[] { worker }, walls[0].Id));
        sim.Step();
        // Paid for at once: 3 wood for each of the four barriers and 5 for the gate, which is of their tier.
        Assert.Equal(100 - 4 * 3 - 5, store.Stock[(int)Good.Wood]);
        Assert.All(walls.Append(gate), w =>
        {
            Assert.Equal(db.Building(w == gate ? "wooden_gate" : "wooden_wall"), w.Upgrade);
            // Until its work is done the barrier stands as it is.
            Assert.True(w.Active);
            Assert.Equal(w == gate ? "barrier_gate" : "barrier", w.Def.Id);
            Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(w)); // one upgrade at a time
        });
        Assert.Equal(BlockType.Palisade, sim.World.Get(20, walls[0].Y + 1, 30));
        Assert.Equal(BlockType.Air, sim.World.Get(20, walls[0].Y + 2, 30));

        // The worker does them one after another, moving on along the wall.
        RunUntil(sim, () => walls.Append(gate).All(w => w.Upgrade == null), 60 * Tps, "the worker to upgrade the stretch");
        Assert.All(walls, w =>
        {
            Assert.Equal("wooden_wall", w.Def.Id);
            Assert.True(w.Active);
            for (int y = 0; y < 4; y++) Assert.Equal(BlockType.Planks, sim.World.Get(w.X, w.Y + y, w.Z));
            Assert.Equal(BlockType.Air, sim.World.Get(w.X, w.Y + 4, w.Z));
            Assert.Equal(w.MaxHealth, w.Health);
        });
        Assert.Equal("wooden_gate", gate.Def.Id);
        Assert.Equal(new[] { BlockType.Gate, BlockType.Gate, BlockType.Planks, BlockType.Planks }, Enumerable.Range(0, 4).Select(y => sim.World.Get(24, gate.Y + y, 30)));
        // A wooden wall is one column thick: nothing has gone up beside it.
        Assert.Equal(5, sim.Buildings.All.Count(b => b.Def.IsWall));
        sim.Run(Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, worker)]);

        // Stone needs masonry.
        Assert.All(walls, w => Assert.Equal(PlaceResult.Locked, sim.Economy.CanUpgrade(w)));
        sim.Schedule(sim.Tick, new UpgradeCommand(0, new[] { worker }, walls[0].Id));
        sim.Step();
        Assert.All(walls, w => Assert.Null(w.Upgrade));
        Assert.Equal(100, store.Stock[(int)Good.Stone]);

        sim.Schedule(sim.Tick, new SandboxTechCommand(0, 0, "masonry"));
        sim.Schedule(sim.Tick + 1, new UpgradeCommand(0, new[] { worker }, walls[0].Id));
        sim.Run(2);
        // 10 stone a wall; the gate 12 stone and 4 wood. That pays for the facing too.
        Assert.Equal(100 - 4 * 10 - 12, store.Stock[(int)Good.Stone]);
        RunUntil(sim, () => walls.Append(gate).All(w => w.Upgrade == null), 120 * Tps, "the worker to turn the stretch to stone");
        Assert.All(walls, w =>
        {
            Assert.Equal("stone_wall", w.Def.Id);
            for (int y = 0; y < 6; y++) Assert.Equal(BlockType.Rampart, sim.World.Get(w.X, w.Y + y, w.Z));
            Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(w)); // the last tier
        });
        Assert.Equal(new[] { BlockType.Gate, BlockType.Gate, BlockType.Rampart, BlockType.Rampart, BlockType.Rampart, BlockType.Rampart },
            Enumerable.Range(0, 6).Select(y => sim.World.Get(24, gate.Y + y, 30)));

        // The facing: a site on each side of every column, paid for already, that the worker goes on to build.
        var facings = sim.Buildings.All.Where(b => b.Def.FacingOf != null).ToArray();
        Assert.Equal(10, facings.Length);
        Assert.Equal(100 - 4 * 10 - 12, store.Stock[(int)Good.Stone]);
        RunUntil(sim, () => facings.All(f => f.Active), 240 * Tps, "the worker to build the facing");
        Assert.Equal(48, store.Stock[(int)Good.Stone]);
        // Three columns thick and six high, with the gate a gatehouse as deep.
        for (int x = 20; x <= 24; x++)
        for (int z = 29; z <= 31; z++)
        for (int y = 0; y < 6; y++)
            Assert.Equal(x == 24 && y < 2 ? BlockType.Gate : BlockType.Rampart, sim.World.Get(x, walls[0].Y + y, z));
        Assert.All(facings, f => Assert.Equal(f.X == 24 ? "stone_gate_facing" : "stone_facing", f.Def.Id));
        // It is as thick as it gets: a facing has no facing of its own, and nothing is left to upgrade.
        sim.Run(5 * Tps);
        Assert.Equal(15, sim.Buildings.All.Count(b => b.Def.IsWall));
        Assert.All(sim.Buildings.All.Where(b => b.Def.IsWall), w => Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(w)));
    }

    [Fact]
    public void AnUpgradeTakesTheStretchOfItsTierNearestFirstUntilTheGoodsRunOut()
    {
        var sim = Flat();
        var store = Store(sim, 0, stone: 0, wood: 10);
        // A line with a corner, one column of it already a wooden wall, and a barrier that stands apart.
        var line = Line(sim, 0, "barrier", 20, 22, 30);
        var wooden = Wall(sim, 0, "wooden_wall", 23, 30);
        var beyond = Line(sim, 0, "barrier", 24, 25, 31); // joined corner to corner
        var apart = Wall(sim, 0, "barrier", 30, 30);
        var enemys = Wall(sim, 1, "barrier", 19, 30);

        Assert.Equal(new[] { line[1], line[2], line[0], beyond[0], beyond[1] }, sim.Economy.Stretch(line[1]));
        sim.Schedule(0, new UpgradeCommand(0, Array.Empty<int>(), line[1].Id));
        sim.Step();
        // Wood for three: the one named and the two beside it. The wooden wall is passed over, not paid for.
        Assert.All(line, w => Assert.NotNull(w.Upgrade));
        Assert.All(beyond.Append(wooden).Append(apart).Append(enemys), w => Assert.Null(w.Upgrade));
        Assert.Equal(1, store.Stock[(int)Good.Wood]);

        // One column only.
        store.Stock[(int)Good.Wood] = 10;
        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), beyond[1].Id, Stretch: false));
        sim.Step();
        Assert.NotNull(beyond[1].Upgrade);
        Assert.Null(beyond[0].Upgrade);
        // Not somebody else's wall.
        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), enemys.Id));
        sim.Step();
        Assert.Null(enemys.Upgrade);
        Assert.Equal(7, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void AnUpgradeMakesADamagedWallWholeAgain()
    {
        var sim = Flat();
        Store(sim, 0, stone: 0, wood: 100);
        var wall = Wall(sim, 0, "barrier_gate", 25, 30);
        int spear = sim.SpawnUnit(1, "spearman", 25, 28);
        sim.Schedule(0, new AttackBuildingCommand(1, new[] { spear }, wall.Id));
        RunUntil(sim, () => wall.Lost > 0, 60 * Tps, "the beam over the gate to be knocked off");
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { spear }, SandboxAction.Remove));
        Assert.True(wall.Health < wall.MaxHealth);
        int worker = sim.SpawnUnit(0, "worker", 25, 33);
        sim.Schedule(sim.Tick, new UpgradeCommand(0, new[] { worker }, wall.Id));
        RunUntil(sim, () => wall.Def.Id == "wooden_gate", 60 * Tps, "the gate to be upgraded");
        Assert.Equal(wall.MaxHealth, wall.Health);
        Assert.Equal(BlockType.Planks, sim.World.Get(25, wall.Y + 2, 30));
    }

    [Fact]
    public void EachTierIsSturdierThanTheLast()
    {
        // The same four spearmen at one column of each tier: how long it stands.
        int Stands(string def)
        {
            var sim = Flat();
            var wall = Wall(sim, 0, def, 25, 30);
            var spears = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(1, "spearman", 24 + k % 2 * 2, 28 + k / 2 * 4)).ToArray();
            sim.Schedule(0, new AttackBuildingCommand(1, spears, wall.Id));
            return RunUntil(sim, () => sim.Buildings.Get(wall.Id) == null, 600 * Tps, $"the {def} to come down");
        }
        int barrier = Stands("barrier"), wooden = Stands("wooden_wall"), stone = Stands("stone_wall");
        Assert.True(barrier < 10 * Tps, $"a barrier is easy to destroy: {barrier} ticks");
        Assert.True(wooden > 3 * barrier, $"a wooden wall stands much longer than a barrier: {wooden} against {barrier} ticks");
        Assert.True(stone > 2 * wooden, $"a stone wall stands much longer than a wooden one: {stone} against {wooden} ticks");
        Assert.True(Stands("barrier_gate") < Stands("wooden_gate") && Stands("wooden_gate") < Stands("stone_gate"));
    }

    // --- Stone walls three columns thick -----------------------------------------------------------

    /// <summary>A match in test mode where player 0 has masonry and what it orders built is done at once.</summary>
    private static Simulation Instant(int stone = 500)
    {
        var sim = FlatWithTechs("masonry");
        Store(sim, 0, stone, wood: 500);
        sim.Schedule(0, new SandboxCheatCommand(0, 0, SandboxCheat.InstantBuild, true));
        sim.Step();
        return sim;
    }

    private static (int X, int Z)[] Facings(Simulation sim) =>
        sim.Buildings.All.Where(b => b.Def.FacingOf != null).Select(b => (b.X, b.Z)).OrderBy(c => c.Z).ThenBy(c => c.X).ToArray();

    [Fact]
    public void AStoneWallIsFacedOnTheFreeSidesAcrossItsLineAndRoundTheOutsideOfATurn()
    {
        var sim = Instant();
        var store = sim.Buildings.All.Single(b => b.Def.Storage);
        // A line along x that turns a corner at (24, 30) and runs on along z.
        var line = Line(sim, 0, "wooden_wall", 20, 24, 30);
        var down = Enumerable.Range(31, 3).Select(z => Wall(sim, 0, "wooden_wall", 24, z)).ToArray();
        // Across the line: the two columns beside it. An end has no cap.
        Assert.Equal(new[] { (21, 29), (21, 31) }, sim.Economy.Sides(line[1]));
        Assert.Equal(new[] { (20, 29), (20, 31) }, sim.Economy.Sides(line[0]));
        Assert.Equal(new[] { (23, 33), (25, 33) }, sim.Economy.Sides(down[2]));
        // The corner: its two free sides, and the corner outside the turn.
        Assert.Equal(new[] { (24, 29), (25, 30), (25, 29) }, sim.Economy.Sides(line[4]));
        // A wall that stands alone has no line to be thick across.
        Assert.Empty(sim.Economy.Sides(Wall(sim, 0, "wooden_wall", 40, 40)));
        // Wooden walls are not faced.
        Assert.Empty(sim.Economy.MissingFacings(line[1]));

        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), line[0].Id));
        sim.Run(5);
        Assert.All(line.Concat(down), w => Assert.Equal("stone_wall", w.Def.Id));
        // 10 stone a column, facing and all: it is paid for with the upgrade.
        Assert.Equal(500 - 8 * 10, store.Stock[(int)Good.Stone]);
        var expected = new List<(int X, int Z)>();
        for (int x = 20; x <= 25; x++) expected.Add((x, 29));      // outside, with the corner
        for (int x = 20; x <= 23; x++) expected.Add((x, 31));      // inside
        for (int z = 30; z <= 33; z++) expected.Add((25, z));      // outside, on round the turn
        for (int z = 32; z <= 33; z++) expected.Add((23, z));      // inside
        Assert.Equal(expected.OrderBy(c => c.Z).ThenBy(c => c.X), Facings(sim));
        sim.Run(5);
        foreach (var (x, z) in expected)
        {
            var f = sim.Buildings.AtColumn(x, z)!;
            Assert.Equal("stone_facing", f.Def.Id);
            Assert.True(f.Active);
            for (int y = 0; y < 6; y++) Assert.Equal(BlockType.Rampart, sim.World.Get(x, f.Y + y, z));
        }
        // The wall does not grow outward: a facing is never faced, and there is nothing left to do.
        sim.Run(Tps);
        Assert.Equal(expected.Count, Facings(sim).Length);
        Assert.All(sim.Buildings.All.Where(b => b.Def.IsWall && b.Def.Id != "wooden_wall"), b => Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(b)));
        Assert.Equal(500 - 8 * 10, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void ABuilderGoesOnToFacingsFurtherOffThanItLooksForMoreWall()
    {
        var sim = FlatWithTechs("masonry");
        Store(sim, 0, stone: 500, wood: 100);
        // A stone wall that has its facing in the middle only: the ends, twenty columns apart, have lost theirs.
        var line = Line(sim, 0, "stone_wall", 16, 47, 30);
        Line(sim, 0, "stone_facing", 22, 41, 29);
        Line(sim, 0, "stone_facing", 22, 41, 31);
        int worker = sim.SpawnUnit(0, "worker", 16, 33);
        sim.Step();
        sim.Schedule(sim.Tick, new UpgradeCommand(0, new[] { worker }, line[0].Id));
        sim.Step();
        Assert.Equal(40 + 24, Facings(sim).Length);
        Assert.Equal(24, sim.Buildings.All.Count(b => b.State == BuildingState.Site));
        // The one worker faces the west end, and then the east end too: that is further off than it would go
        // for another column of wall (nobody laid these out, so nobody was sent to each), but not for a facing.
        Assert.True(42 - 21 > EconomySystem.WallReach && 42 - 21 <= EconomySystem.FacingReach);
        RunUntil(sim, () => sim.Buildings.All.All(b => b.Active), 300 * Tps, "the worker to face both ends of the wall");
        sim.Run(2);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, worker)]);
    }

    [Fact]
    public void AFacingLeavesTheWayThroughAGateOpenAndAStoneGateIsAGatehouse()
    {
        var sim = Instant();
        // A gate in a line along x, right beside the corner where the line turns north.
        var west = Line(sim, 0, "wooden_wall", 20, 22, 30);
        var gate = Wall(sim, 0, "wooden_gate", 23, 30);
        var corner = Wall(sim, 0, "wooden_wall", 24, 30);
        var north = new[] { Wall(sim, 0, "wooden_wall", 24, 29), Wall(sim, 0, "wooden_wall", 24, 28) };
        // The way through the gate is across the line: north and south of it.
        Assert.Equal(new[] { (23, 29), (23, 31) }, sim.Economy.Sides(gate));
        // The wall north of the corner would face onto (23, 29) and (23, 28), which is the way through the gate
        // and out of it: it does not.
        Assert.Contains((23, 29), sim.Economy.Sides(north[0]));
        Assert.DoesNotContain((23, 29), sim.Economy.FacingCells(north[0]));
        Assert.Contains((23, 28), sim.Economy.Sides(north[1]));
        Assert.DoesNotContain((23, 28), sim.Economy.FacingCells(north[1]));
        Assert.Contains((25, 28), sim.Economy.FacingCells(north[1]));
        foreach (var w in west.Append(corner).Concat(north))
            sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), w.Id, Stretch: false));
        sim.Run(10);
        Assert.All(west.Append(corner).Concat(north), w => Assert.Equal("stone_wall", w.Def.Id));
        Assert.Equal("wooden_gate", gate.Def.Id);
        Assert.Null(sim.Buildings.AtColumn(23, 29));
        Assert.Null(sim.Buildings.AtColumn(23, 28));
        Assert.Null(sim.Buildings.AtColumn(23, 31));
        Assert.NotNull(sim.Buildings.AtColumn(22, 29));
        Assert.NotNull(sim.Buildings.AtColumn(25, 29));

        // Its owner still walks through, between the facings.
        int own = sim.SpawnUnit(0, "spearman", 23, 36);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { own }, 22, 26));
        bool through = false;
        RunUntil(sim, () =>
        {
            int i = Index(sim, own);
            through |= sim.Units.X[i].FloorToInt() == 23 && sim.Units.Z[i].FloorToInt() == 30;
            return sim.Units.Z[i].FloorToInt() <= 26;
        }, 40 * Tps, "the owner's unit to walk through the gate between the facings");
        Assert.True(through);

        // The gate upgraded: a gatehouse as deep as the wall is thick, leaves below and stone above, all the way through.
        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), gate.Id, Stretch: false));
        sim.Run(10);
        Assert.Equal("stone_gate", gate.Def.Id);
        for (int z = 29; z <= 31; z++)
        {
            var g = sim.Buildings.AtColumn(23, z)!;
            Assert.Equal(z == 30 ? "stone_gate" : "stone_gate_facing", g.Def.Id);
            Assert.Equal(BuildingKind.Gate, g.Def.Kind);
            Assert.Equal(new[] { BlockType.Gate, BlockType.Gate, BlockType.Rampart, BlockType.Rampart, BlockType.Rampart, BlockType.Rampart },
                Enumerable.Range(0, 6).Select(y => sim.World.Get(23, g.Y + y, z)));
        }
        through = false;
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { own }, 23, 36));
        RunUntil(sim, () =>
        {
            int i = Index(sim, own);
            through |= sim.Units.X[i].FloorToInt() == 23 && sim.Units.Z[i].FloorToInt() == 30;
            return sim.Units.Z[i].FloorToInt() >= 35;
        }, 40 * Tps, "the owner's unit to walk back through the gatehouse");
        Assert.True(through);
        // Nobody else does.
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { own }, SandboxAction.Remove));
        sim.Step();
        int enemy = sim.SpawnUnit(1, "spearman", 23, 36);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { enemy }, 23, 28));
        for (int t = 0; t < 20 * Tps; t++)
        {
            sim.Step();
            int i = Index(sim, enemy);
            Assert.False(sim.Units.X[i].FloorToInt() == 23 && sim.Units.Z[i].FloorToInt() is >= 29 and <= 31, "an enemy in the gatehouse");
        }
    }

    [Fact]
    public void ALostFacingIsPutBackForItsOwnPrice()
    {
        var sim = Instant(stone: 100);
        var store = sim.Buildings.All.Single(b => b.Def.Storage);
        var line = Line(sim, 0, "wooden_wall", 20, 22, 30);
        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), line[0].Id));
        sim.Run(10);
        Assert.Equal(6, Facings(sim).Length);
        Assert.Equal(70, store.Stock[(int)Good.Stone]);
        Assert.All(line, w => Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(w)));

        // A facing goes (here it is cleared away): the wall it stood against can have it back.
        sim.Schedule(sim.Tick, new SandboxCheatCommand(0, 0, SandboxCheat.InstantBuild, false));
        sim.Schedule(sim.Tick, new SandboxClearCommand(0, 21, 31, 0));
        sim.Step();
        Assert.Null(sim.Buildings.AtColumn(21, 31));
        Assert.Equal(new[] { (21, 31) }, sim.Economy.MissingFacings(line[1]));
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanUpgrade(line[1]));
        Assert.Equal(PlaceResult.Blocked, sim.Economy.CanUpgrade(line[0]));
        // Without the stone for it, it cannot.
        store.Stock[(int)Good.Stone] = 2;
        Assert.Equal(PlaceResult.CannotAfford, sim.Economy.CanUpgrade(line[1]));
        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), line[1].Id));
        sim.Step();
        Assert.Null(sim.Buildings.AtColumn(21, 31));

        // The order to upgrade the stretch puts it back: 3 stone, and a worker's job like any site.
        store.Stock[(int)Good.Stone] = 70;
        int worker = sim.SpawnUnit(0, "worker", 21, 34);
        sim.Schedule(sim.Tick, new UpgradeCommand(0, new[] { worker }, line[0].Id));
        sim.Step();
        var site = sim.Buildings.AtColumn(21, 31)!;
        Assert.Equal("stone_facing", site.Def.Id);
        Assert.Equal(BuildingState.Site, site.State);
        Assert.Equal(67, store.Stock[(int)Good.Stone]);
        Assert.Equal((UnitJob.Build, site.Id), (sim.Units.Job[Index(sim, worker)], sim.Units.JobTarget[Index(sim, worker)]));
        RunUntil(sim, () => site.Active, 30 * Tps, "the worker to build the facing again");
        Assert.Equal(6, Facings(sim).Length);
        Assert.Equal(67, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void AttackersBreakStraightThroughAThickWallAndGoOnToWhatTheyWereSentAt()
    {
        var sim = Instant();
        Ring(sim, 0, gate: false, "wooden");
        sim.Schedule(sim.Tick, new UpgradeCommand(0, Array.Empty<int>(), sim.Buildings.AtColumn(35, Hi)!.Id));
        sim.Run(10);
        sim.Schedule(sim.Tick, new SandboxCheatCommand(0, 0, SandboxCheat.InstantBuild, false));
        Assert.All(new[] { Hi - 1, Hi, Hi + 1 }, z => Assert.Equal(BlockType.Rampart, sim.World.Get(35, Ground + 6, z)));
        var house = Wall(sim, 0, "house", 33, 33);
        var sappers = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(1, "sapper", 34 + k, Hi + 5)).ToArray();
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, sappers, house.Id));
        int walls = sim.Buildings.All.Count(b => b.Def.IsWall);

        // The house is walled in: each goes for the facing in front of it, and remembers what it came for.
        RunUntil(sim, () => sappers.All(id => sim.Buildings.Get(sim.Units.TargetBuilding[Index(sim, id)]) is { Def.Id: "stone_facing" }), 60 * Tps,
            "the sappers to go for the wall's facing");
        Assert.All(sappers, id =>
        {
            int i = Index(sim, id);
            var facing = sim.Buildings.Get(sim.Units.TargetBuilding[i])!;
            Assert.Equal((34 + Array.IndexOf(sappers, id), Hi + 1), (facing.X, facing.Z)); // the one straight ahead, on its side
            Assert.Equal(house.Id, sim.Units.BreachFor[i]);
        });
        // The facing down, they do not stand about: the wall behind it is next, then the facing behind that,
        // from on top of the rubble of the last, which hurts nobody as it spreads round their feet.
        RunUntil(sim, () => sappers.Any(id => sim.Units.Exists(id) && Inside(sim, id)), 150 * Tps,
            $"a sapper to get through the wall; {sappers.Count(sim.Units.Exists)} of 3 are left");
        Assert.All(sappers, id => Assert.True(sim.Units.Exists(id), "a sapper was killed by the wall it brought down"));
        Assert.All(new[] { Hi - 1, Hi, Hi + 1 }, z => Assert.Null(sim.Buildings.AtColumn(35, z)));
        Assert.Equal(walls - 9, sim.Buildings.All.Count(b => b.Def.IsWall)); // three columns each, straight through
        // And on to the house.
        RunUntil(sim, () => sim.Buildings.Get(house.Id) == null, 120 * Tps, "the sappers to bring the house down");
    }

    // --- Posts on the walls ------------------------------------------------------------------------

    private static bool OnTop(Simulation sim, int id, Building wall)
    {
        int i = Index(sim, id);
        return sim.Units.X[i].FloorToInt() == wall.X && sim.Units.Z[i].FloorToInt() == wall.Z && sim.Units.Y[i] == wall.Y + wall.Def.Height;
    }

    [Fact]
    public void ArchersArePostedOnTopOfAWoodenWallOneToAColumn()
    {
        var sim = Flat();
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        var archers = new[] { sim.SpawnUnit(0, "archer", 23, 34), sim.SpawnUnit(0, "archer", 25, 34), sim.SpawnUnit(0, "archer", 27, 34) };
        int spear = sim.SpawnUnit(0, "spearman", 25, 36);
        sim.Schedule(0, new PostCommand(0, archers.Append(spear).ToArray(), walls[5].Id));
        sim.Step();
        // Only those that shoot go up; each has a column of its own, the nearest to the one named.
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, spear)]);
        Assert.All(archers, id => Assert.Equal(UnitJob.Post, sim.Units.Job[Index(sim, id)]));
        Assert.Equal(new[] { walls[5].Id, walls[4].Id, walls[6].Id }, archers.Select(id => sim.Units.JobTarget[Index(sim, id)]));
        RunUntil(sim, () => archers.All(id => sim.Units.Job[Index(sim, id)] == UnitJob.None), 30 * Tps, "the archers to climb up");
        Assert.True(OnTop(sim, archers[0], walls[5]) && OnTop(sim, archers[1], walls[4]) && OnTop(sim, archers[2], walls[6]));
        Assert.All(archers, id => Assert.Equal(walls[5].Y + 4, sim.Units.Y[Index(sim, id)]));
        Assert.Equal(walls[5], sim.Economy.WallUnder(Index(sim, archers[0])));
        Assert.Null(sim.Economy.WallUnder(Index(sim, spear)));

        // Another archer sent to the same column takes the next free one; those already up stay where they are.
        int late = sim.SpawnUnit(0, "archer", 25, 34);
        sim.Schedule(sim.Tick, new PostCommand(0, archers.Append(late).ToArray(), walls[5].Id));
        RunUntil(sim, () => sim.Units.Job[Index(sim, late)] == UnitJob.None && sim.Economy.WallUnder(Index(sim, late)) != null, 30 * Tps, "the late archer to climb up");
        Assert.True(OnTop(sim, archers[0], walls[5]) && OnTop(sim, archers[1], walls[4]) && OnTop(sim, archers[2], walls[6]));
        Assert.True(OnTop(sim, late, walls[3]) || OnTop(sim, late, walls[7]));
    }

    [Fact]
    public void OnAThickWallArchersStandAlongItsEdgesAndClimbFromEitherSide()
    {
        var sim = Flat();
        var line = Line(sim, 0, "stone_wall", 20, 30, 30);
        var northFace = Line(sim, 0, "stone_facing", 20, 30, 29);
        var southFace = Line(sim, 0, "stone_facing", 20, 30, 31);
        // The middle of the deck is no post: the battlements are along the edges.
        Assert.False(sim.Economy.PostFree(line[5]));
        Assert.False(sim.Economy.PostFree(line[5], 25, 30));
        Assert.True(sim.Economy.PostFree(northFace[5]) && sim.Economy.PostFree(southFace[5]));
        Assert.Equal(1, sim.Economy.FreePosts(southFace[5]));

        // Two archers south of the wall, sent to its middle: one to each edge there, both climbing from the south.
        var archers = new[] { sim.SpawnUnit(0, "archer", 25, 35), sim.SpawnUnit(0, "archer", 26, 35) };
        sim.Schedule(0, new PostCommand(0, archers, line[5].Id));
        sim.Step();
        var posts = archers.Select(id => (sim.Units.JobX[Index(sim, id)], sim.Units.JobZ[Index(sim, id)])).ToArray();
        Assert.Equal(new[] { (25, 29), (25, 31) }, posts.OrderBy(p => p.Item2));
        RunUntil(sim, () => archers.All(id => sim.Units.Job[Index(sim, id)] == UnitJob.None), 40 * Tps, "the archers to climb up");
        for (int k = 0; k < 2; k++)
        {
            int i = Index(sim, archers[k]);
            Assert.Equal((posts[k].Item1, posts[k].Item2, line[5].Y + 6), (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt(), sim.Units.Y[i]));
            Assert.NotNull(sim.Economy.WallUnder(i));
            Assert.True(sim.Combat.Holding(i));
        }
        Assert.False(sim.Economy.PostFree(northFace[5]));

        // Ordered north, each comes down on the north side of the wall, whichever edge it stood on, and walks on.
        sim.Schedule(sim.Tick, new MoveCommand(0, archers, 25, 22));
        RunUntil(sim, () => archers.All(id => sim.Units.Y[Index(sim, id)] == Ground + 1), 5 * Tps, "the archers to climb down");
        Assert.All(archers, id => Assert.True(sim.Units.Z[Index(sim, id)].FloorToInt() <= 28, "down on the side it is going to"));
        RunUntil(sim, () => archers.All(id => sim.Units.Z[Index(sim, id)].FloorToInt() <= 24), 20 * Tps, "the archers to walk on north");
    }

    [Fact]
    public void AnArcherOnAWallLeansOutToShootWhatStandsAtItsFoot()
    {
        var sim = Flat();
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        int archer = sim.SpawnUnit(0, "archer", 25, 33);
        sim.Schedule(0, new PostCommand(0, new[] { archer }, walls[5].Id));
        RunUntil(sim, () => OnTop(sim, archer, walls[5]), 30 * Tps, "the archer to climb up");
        // An enemy right at the foot of the wall under it: the wall's own top is in the way of a line from where it stands.
        int below = sim.SpawnUnit(1, "worker", 25, 29);
        var u = sim.Units;
        int a = Index(sim, archer), b = Index(sim, below);
        Assert.False(sim.Combat.ClearLine(u.X[a], u.Y[a], u.Z[a], u.X[b], u.Y[b], u.Z[b]));
        Assert.True(sim.Combat.ClearShot(a, b));
        int hp = u.Hp[b];
        RunUntil(sim, () => !sim.Units.Exists(below) || sim.Units.Hp[Index(sim, below)] < hp, 10 * Tps, "the archer to hit it");
        // From the ground beside it, with the wall between them, there is no shot.
        int grounded = sim.SpawnUnit(0, "archer", 27, 32);
        int other = sim.SpawnUnit(1, "worker", 27, 27);
        Assert.False(sim.Combat.ClearShot(Index(sim, grounded), Index(sim, other)));
    }

    [Fact]
    public void NobodyIsPostedOnABarrier()
    {
        var sim = Flat();
        var walls = Line(sim, 0, "barrier", 20, 30, 30);
        int archer = sim.SpawnUnit(0, "archer", 25, 34);
        Assert.False(sim.Economy.PostFree(walls[5]));
        sim.Schedule(0, new PostCommand(0, new[] { archer }, walls[5].Id));
        sim.Run(5 * Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, archer)]);
        Assert.Null(sim.Economy.WallUnder(Index(sim, archer)));
    }

    [Theory]
    [InlineData("wooden_wall")]
    [InlineData("stone_wall")]
    [InlineData("stone_gate")]
    public void AnArcherOnAWallShootsFurtherHoldsItsGroundAndComesDownWhenOrderedAway(string def)
    {
        var sim = Flat();
        var walls = Line(sim, 0, def, 20, 30, 30);
        int archer = sim.SpawnUnit(0, "archer", 25, 33);
        sim.Schedule(0, new PostCommand(0, new[] { archer }, walls[5].Id));
        RunUntil(sim, () => OnTop(sim, archer, walls[5]), 30 * Tps, "the archer to climb up");

        // A worker of the enemy's 11 blocks off: out of an archer's reach on the ground (9), in reach from the wall (a quarter more).
        int far = sim.SpawnUnit(1, "worker", 25, 19);
        int hp = sim.Units.Hp[Index(sim, far)];
        RunUntil(sim, () => !sim.Units.Exists(far) || sim.Units.Hp[Index(sim, far)] < hp, 10 * Tps, "the archer to hit it from the wall");
        Assert.True(OnTop(sim, archer, walls[5]));
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { far }, SandboxAction.Remove));

        // One it sees but cannot reach, 12 off: on the ground it would go after it; on the wall it stays.
        Assert.Equal(UnitStance.Aggressive, sim.Units.Stance[Index(sim, archer)]);
        int seen = sim.SpawnUnit(1, "worker", 25, 18);
        sim.Run(8 * Tps);
        Assert.True(OnTop(sim, archer, walls[5]), "it holds the wall");
        Assert.Equal(sim.Economy.MaxHp(1, sim.Content.Unit("worker")), sim.Units.Hp[Index(sim, seen)]);

        // Ordered away, it comes down and goes (from a stone wall, too high to step off, it climbs down).
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { archer }, 25, 40));
        RunUntil(sim, () => sim.Units.Order[Index(sim, archer)] == UnitOrder.Idle && sim.Units.Z[Index(sim, archer)].FloorToInt() >= 39, 30 * Tps,
            "the archer to come down and walk off");
        Assert.Equal(Ground + 1, sim.Units.Y[Index(sim, archer)]);
    }

    [Fact]
    public void SoldiersWhoCannotGetAtAnArcherOnAWallBringTheWallDownUnderIt()
    {
        var sim = Flat();
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        int archer = sim.SpawnUnit(0, "archer", 25, 33);
        sim.Schedule(0, new PostCommand(0, new[] { archer }, walls[5].Id));
        RunUntil(sim, () => OnTop(sim, archer, walls[5]), 30 * Tps, "the archer to climb up");
        // Sappers, as spearmen hacking at a thick wall get little done before the archer has shot them.
        var spears = Enumerable.Range(0, 8).Select(k => sim.SpawnUnit(1, "sapper", 21 + k, 24)).ToArray();
        // Blows from the ground do not reach it; the soldiers it shoots at turn on the wall it stands on.
        RunUntil(sim, () => spears.Any(id => sim.Units.Exists(id) && sim.Units.TargetBuilding[Index(sim, id)] == walls[5].Id), 60 * Tps,
            "a sapper to go for the wall under the archer");
        Assert.Equal(sim.Economy.MaxHp(0, sim.Content.Unit("archer")), sim.Units.Hp[Index(sim, archer)]);
        RunUntil(sim, () => sim.Buildings.Get(walls[5].Id) == null, 120 * Tps, "the wall to come down under it");
        RunUntil(sim, () => !sim.Units.Exists(archer) || sim.Units.Y[Index(sim, archer)] <= Ground + 2, 5 * Tps, "the archer to come down with it");
    }

    [Fact]
    public void AnArcherStaysOnItsWallWhenTheWallIsUpgradedToStone()
    {
        var sim = FlatWithTechs("masonry");
        Store(sim, 0, stone: 100, wood: 100);
        var walls = Line(sim, 0, "wooden_wall", 20, 30, 30);
        int archer = sim.SpawnUnit(0, "archer", 25, 33), worker = sim.SpawnUnit(0, "worker", 24, 33);
        sim.Schedule(0, new PostCommand(0, new[] { archer }, walls[5].Id));
        RunUntil(sim, () => OnTop(sim, archer, walls[5]), 30 * Tps, "the archer to climb up");
        sim.Schedule(sim.Tick, new UpgradeCommand(0, new[] { worker }, walls[5].Id, Stretch: false));
        RunUntil(sim, () => walls[5].Def.Id == "stone_wall", 30 * Tps, "the column to turn to stone");
        sim.Run(2);
        Assert.True(OnTop(sim, archer, walls[5]), "the archer is on top of the taller wall");
        Assert.Equal(walls[5].Y + 6, sim.Units.Y[Index(sim, archer)]);
    }

    [Fact]
    public void UpgradesAndPostsAreLoggedAndReplayTheSame()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64 };
        Command[] commands = { new UpgradeCommand(0, new[] { 3, 4 }, 7), new UpgradeCommand(0, Array.Empty<int>(), 9, Stretch: false), new PostCommand(0, new[] { 5 }, 8) };
        foreach (var c in commands) log.Add(5, c);
        log.Ticks = 10;
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(commands, copy.Commands.Select(c => c.Command));

        ulong Play()
        {
            var sim = Flat();
            Store(sim, 0, stone: 100, wood: 100);
            var walls = Line(sim, 0, "barrier", 20, 26, 30);
            var wooden = Line(sim, 0, "wooden_wall", 27, 30, 30);
            var workers = new[] { sim.SpawnUnit(0, "worker", 22, 33), sim.SpawnUnit(0, "worker", 26, 33) };
            var archers = new[] { sim.SpawnUnit(0, "archer", 28, 34), sim.SpawnUnit(0, "archer", 29, 34) };
            sim.Schedule(0, new UpgradeCommand(0, workers, walls[0].Id));
            sim.Schedule(0, new PostCommand(0, archers, wooden[1].Id));
            sim.Run(20 * Tps);
            Assert.All(walls, w => Assert.Equal("wooden_wall", w.Def.Id));
            Assert.All(archers, id => Assert.NotNull(sim.Economy.WallUnder(Index(sim, id))));
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
