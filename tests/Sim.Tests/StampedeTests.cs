using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.World;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #152: Beast pens builds the beast pen; the Orcs keep tamed beasts in it and loose them as a stampede.</summary>
public class StampedeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Orcs = 1; // player 1 plays the orcs
    private const int Foe = 0;
    private const int Line = 42; // the stampede runs east along z = 42

    private static Simulation Flat(int size = 96) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

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

    private static int I(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>How far (columns) unit id stands from (x, z).</summary>
    private static Fix64 Gone(Simulation sim, int id, Fix64 x, Fix64 z)
    {
        Fix64 dx = sim.Units.X[I(sim, id)] - x, dz = sim.Units.Z[I(sim, id)] - z;
        return Fix64.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>An Orc town with a finished beast pen at (10, 38), and <paramref name="beasts"/> wolves of theirs penned in it.</summary>
    private static (Simulation Sim, Building Pen, int[] Beasts) Penned(int beasts, string kind = "wolf", bool testMode = false)
    {
        var sim = Flat();
        sim.TestMode = testMode;
        sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 4, 4);
        sim.Economy.PlaceFinished(Foe, sim.Content.Building("town_hall"), 80, 80);
        var pen = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("beast_pen"), 10, 38)!;
        var ids = Enumerable.Range(0, beasts).Select(k => sim.SpawnUnit(Orcs, kind, 4 + k % 4, 30 + k / 4)).ToArray();
        sim.Schedule(0, new PenCommand(Orcs, ids, pen.Id));
        RunUntil(sim, () => ids.All(id => sim.Units.Penned[I(sim, id)] == pen.Id), 30 * Tps, "the beasts walking into the pen");
        return (sim, pen, ids);
    }

    [Fact]
    public void TheBeastPenIsDataAndNeedsBeastPens()
    {
        var db = ContentLoader.LoadEmbedded();
        var pen = db.Building("beast_pen");
        Assert.Equal(BuildingKind.Pen, pen.Kind);
        Assert.True(pen.Walkable);
        Assert.Equal("orcs", pen.Faction!.Id);
        Assert.Equal((6, 6), (pen.SizeX, pen.SizeZ));
        var d = pen.Pen!;
        Assert.Equal((8, 14, 30, 2 * Tps, 60, 60, 20 * Tps, 50), (d.Keeps, d.Speed, d.Trample, d.DownTicks, d.Smash, d.Run, d.TiredTicks, d.Slow));
        Assert.True(db.Unit("wolf").Pennable);
        Assert.True(db.Unit("boar").Pennable);
        Assert.False(db.Unit("stone_giant").Pennable);
        Assert.Empty(db.Tech("orc_beast_pens").Effects); // no more +30 for cavalry

        var sim = Flat();
        sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 4, 4);
        pen = sim.Content.Building("beast_pen");
        Assert.False(sim.Economy.Unlocked(Orcs, pen));
        sim.Economy.Grant(Orcs, sim.Content.Tech("horse_breeding"));
        sim.Economy.Grant(Orcs, sim.Content.Tech("orc_beast_pens"));
        Assert.True(sim.Economy.Unlocked(Orcs, pen));
    }

    [Fact]
    public void BeastsWalkIntoThePenAndAreKeptThereUpToItsRoom()
    {
        var sim = Flat();
        var pen = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("beast_pen"), 10, 38)!;
        var wolves = Enumerable.Range(0, 9).Select(k => sim.SpawnUnit(Orcs, "wolf", 4 + k % 4, 30 + k / 4)).ToList();
        int spear = sim.SpawnUnit(Orcs, "spearman", 8, 34);
        sim.Schedule(0, new PenCommand(Orcs, wolves.Append(spear).ToArray(), pen.Id));
        sim.Step();
        var u = sim.Units;
        Assert.Equal(8, wolves.Count(id => u.PenFor[I(sim, id)] == pen.Id)); // the ninth has no room; the spearman is no beast
        Assert.Equal(0, u.PenFor[I(sim, spear)]);
        RunUntil(sim, () => sim.Combat.PenCount(pen.Id) == 8, 30 * Tps, "eight wolves penned");
        // They stand in the yard, take no orders and do not fight.
        foreach (int i in sim.Combat.PennedIn(pen.Id))
            Assert.True(pen.Covers(u.X[i].FloorToInt(), u.Z[i].FloorToInt()));
        int first = wolves.First(id => u.Penned[I(sim, id)] != 0);
        var (x, z) = (u.X[I(sim, first)], u.Z[I(sim, first)]);
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { first }, 60, 60));
        int enemy = sim.SpawnUnit(Foe, "spearman", 13, 40);
        for (int t = 0; t < 3 * Tps; t++) sim.Step();
        Assert.Equal((x, z), (u.X[I(sim, first)], u.Z[I(sim, first)]));
        Assert.Equal(0, u.Target[I(sim, first)]);
        // Let out, they take orders again.
        sim.Schedule(sim.Tick, new UnpenCommand(Orcs, pen.Id));
        sim.Step();
        Assert.Equal(0, sim.Combat.PenCount(pen.Id));
        _ = enemy;
    }

    [Fact]
    public void AnotherOrderOnTheWayCallsThePenningOff()
    {
        var sim = Flat();
        var pen = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("beast_pen"), 40, 40)!;
        int wolf = sim.SpawnUnit(Orcs, "wolf", 4, 4);
        sim.Schedule(0, new PenCommand(Orcs, new[] { wolf }, pen.Id));
        sim.Schedule(5, new MoveCommand(Orcs, new[] { wolf }, 4, 20));
        for (int t = 0; t < 30 * Tps; t++) sim.Step();
        Assert.Equal(0, sim.Units.Penned[I(sim, wolf)]);
        Assert.Equal(0, sim.Units.PenFor[I(sim, wolf)]);
    }

    [Fact]
    public void AStampedeTramplesAndKnocksDownTheEnemiesOnItsLineAndNotThoseOffIt()
    {
        var (sim, pen, beasts) = Penned(2);
        var u = sim.Units;
        int on = sim.SpawnUnit(Foe, "spearman", 40, Line);
        int off = sim.SpawnUnit(Foe, "spearman", 40, Line + 8);
        sim.Schedule(sim.Tick, new StampedeCommand(Orcs, pen.Id, 20, Line, 60, Line));
        sim.Step();
        Assert.All(beasts, id => Assert.True(sim.Combat.Stampeding(I(sim, id))));
        Assert.Equal(0, sim.Combat.PenCount(pen.Id));
        bool downed = false, offDowned = false;
        RunUntil(sim, () =>
        {
            downed |= u.Stunned[I(sim, on)] > 0;
            offDowned |= u.Stunned[I(sim, off)] > 0;
            return beasts.All(id => !sim.Combat.Stampeding(I(sim, id)));
        }, 20 * Tps, "the stampede's run");
        Assert.True(downed, "the spearman on the line was not knocked down");
        Assert.False(offDowned, "the spearman off the line was knocked down");
        Assert.Equal(80 - 2 * 30, u.Hp[I(sim, on)]); // each of the two beasts tramples it once
        Assert.Equal(80, u.Hp[I(sim, off)]);
        Assert.True(sim.Combat.Trampled >= 2);
        // The beasts ran the line through to its end.
        Assert.All(beasts, id => Assert.True(u.X[I(sim, id)].FloorToInt() >= 58, $"beast at x {u.X[I(sim, id)]}"));
    }

    [Fact]
    public void AStampedeBreaksAPalisadeButStopsAtAStoneWall()
    {
        (Simulation Sim, int[] Beasts, Building[] Walls) Run(string wall)
        {
            var (sim, pen, beasts) = Penned(4);
            var walls = new[] { Line - 1, Line, Line + 1 }
                .Select(z => sim.Economy.PlaceFinished(Foe, sim.Content.Building(wall), 34, z)!).ToArray();
            sim.Schedule(sim.Tick, new StampedeCommand(Orcs, pen.Id, 20, Line, 60, Line));
            sim.Step();
            RunUntil(sim, () => beasts.All(id => !sim.Combat.Stampeding(I(sim, id))), 20 * Tps, "the stampede's run");
            return (sim, beasts, walls);
        }

        var (palisade, through, barriers) = Run("barrier");
        Assert.True(barriers.Count(b => palisade.Buildings.Get(b.Id) == null || palisade.Buildings.Get(b.Id)!.Damage > 0
            || palisade.World.Get(34, b.Y, b.Z) == BlockType.Air) >= 2, "the stampede did not break the palisade");
        Assert.All(through, id => Assert.True(palisade.Units.X[I(palisade, id)].FloorToInt() > 34, "a beast did not get through the palisade"));

        var (stone, stopped, walls) = Run("stone_wall");
        Assert.All(walls, w => Assert.Same(w, stone.Buildings.Get(w.Id)));
        Assert.All(walls, w => Assert.Equal(BlockType.Rampart, stone.World.Get(34, w.Y, w.Z)));
        Assert.All(stopped, id => Assert.True(stone.Units.X[I(stone, id)].FloorToInt() < 34, "a beast went through stone"));
    }

    [Fact]
    public void BeastsNotPennedCannotStampede()
    {
        var sim = Flat();
        var pen = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("beast_pen"), 10, 38)!;
        var wolves = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(Orcs, "wolf", 12 + k, 36)).ToArray();
        int on = sim.SpawnUnit(Foe, "spearman", 40, Line);
        sim.Schedule(0, new StampedeCommand(Orcs, pen.Id, 20, Line, 60, Line));
        for (int t = 0; t < 5 * Tps; t++) sim.Step();
        Assert.All(wolves, id => Assert.False(sim.Combat.Stampeding(I(sim, id))));
        Assert.Equal(0, sim.Combat.Trampled);
        Assert.Equal(80, sim.Units.Hp[I(sim, on)]);
    }

    [Fact]
    public void AfterTheRunTheBeastsAreTiredAndSlowThenTakeOrdersAndCanBePennedAgain()
    {
        var (sim, pen, beasts) = Penned(2, "boar");
        var u = sim.Units;
        sim.Schedule(sim.Tick, new StampedeCommand(Orcs, pen.Id, 20, Line, 40, Line));
        sim.Step();
        RunUntil(sim, () => beasts.All(id => !sim.Combat.Stampeding(I(sim, id))), 10 * Tps, "the stampede's run");
        int b = I(sim, beasts[0]);
        Assert.True(u.Tired[b] > 18 * Tps);
        Assert.Equal(50, sim.Combat.PaceOf(b));
        // Tired, it walks at half its pace.
        var x0 = u.X[b];
        var zStart = u.Z[b];
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { beasts[0] }, 90, Line));
        for (int t = 0; t < 2 * Tps; t++) sim.Step();
        var tired = Gone(sim, beasts[0], x0, zStart);
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        b = I(sim, beasts[0]);
        Assert.Equal(0, u.Tired[b]);
        Assert.Equal(100, sim.Combat.PaceOf(b));
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { beasts[0] }, 4, 60));
        sim.Step();
        var (rx, rz) = (u.X[I(sim, beasts[0])], u.Z[I(sim, beasts[0])]);
        for (int t = 0; t < 2 * Tps; t++) sim.Step();
        var rested = Gone(sim, beasts[0], rx, rz);
        Assert.True(rested > tired * Fix64.FromRatio(3, 2), $"tired {tired}, rested {rested}");
        // Back into the pen.
        sim.Schedule(sim.Tick, new PenCommand(Orcs, beasts, pen.Id));
        RunUntil(sim, () => sim.Combat.PenCount(pen.Id) == 2, 40 * Tps, "the beasts back in the pen");
    }

    [Fact]
    public void ThePensBeastsGoFreeWhenThePenIsLost()
    {
        var (sim, pen, beasts) = Penned(3, testMode: true);
        // Wrecked (test mode's damage, as a siege would): the yard's blocks are knocked out until it falls.
        for (int x = pen.X; x < pen.X + pen.Def.SizeX; x++)
        for (int z = pen.Z; z < pen.Z + pen.Def.SizeZ; z++)
            sim.Schedule(sim.Tick, new SandboxDamageCommand(0, x, z, 1000));
        RunUntil(sim, () => sim.Buildings.Get(pen.Id) == null, 5 * Tps, "the pen falling");
        sim.Step();
        Assert.All(beasts, id => Assert.Equal(0, sim.Units.Penned[I(sim, id)]));
    }

    [Fact]
    public void SameStampedeSameHashAndTheCommandsReplay()
    {
        ulong Play()
        {
            var (sim, pen, _) = Penned(4);
            sim.SpawnUnit(Foe, "spearman", 40, Line);
            sim.SpawnUnit(Foe, "spearman", 41, Line + 1);
            sim.Economy.PlaceFinished(Foe, sim.Content.Building("barrier"), 50, Line);
            sim.Schedule(sim.Tick, new StampedeCommand(Orcs, pen.Id, 20, Line, 60, Line));
            for (int t = 0; t < 10 * Tps; t++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());

        var log = new CommandLog();
        var cmds = new Command[] { new PenCommand(1, new[] { 7, 8 }, 12), new StampedeCommand(1, 12, 40, 50, 60, 51), new UnpenCommand(1, 12) };
        for (int k = 0; k < cmds.Length; k++) log.Add(59 + k, cmds[k]);
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(cmds, back.Commands.Select(c => c.Command).ToArray());
    }

    // --- Computer players ---------------------------------------------------------------------------

    private static Simulation WithStarts(int size, params (int X, int Z)[] starts)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks)
    {
        var hall = sim.Economy.TownHall(ai.Player)!;
        for (int t = 0; t < maxTicks; t++)
        {
            foreach (var g in Goods.All) hall.Stock[(int)g] = Math.Max(hall.Stock[(int)g], 500);
            if (done()) return true;
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    [Fact]
    public void AHardOrcBuildsAPenFillsItAndLoosesAStampedeAtABunchedEnemy()
    {
        var sim = WithStarts(128, (24, 64), (100, 64));
        sim.Economy.Grant(Orcs, sim.Content.Tech("horse_breeding"));
        sim.Economy.Grant(Orcs, sim.Content.Tech("orc_beast_pens"));
        var hall = sim.Economy.TownHall(Orcs)!;
        var beasts = new[] { sim.SpawnUnit(Orcs, "wolf", hall.X - 6, hall.Z - 6), sim.SpawnUnit(Orcs, "boar", hall.X - 5, hall.Z - 6),
            sim.SpawnUnit(Orcs, "wolf", hall.X - 4, hall.Z - 6) };
        var ai = new AiPlayer(Orcs, 7, AiLevel.Hard);
        Building? Pen() => sim.Buildings.All.FirstOrDefault(b => b.Owner == Orcs && b.Def.Kind == BuildingKind.Pen);
        Assert.True(Play(sim, ai, () => Pen() is { Active: true } p && sim.Combat.PenCount(p.Id) >= AiPlayer.PenBeasts, 4 * 60 * Tps),
            "a beast pen built and beasts penned in it");
        var pen = Pen()!;
        // An enemy army stands bunched up within reach of the pen.
        var (px, pz) = (pen.X + 3, pen.Z + 3);
        int fx = px < 64 ? px + 18 : px - 18;
        var foes = Enumerable.Range(0, 5).Select(k => sim.SpawnUnit(Foe, "spearman", fx + k % 3, pz + k / 3)).ToArray();
        Assert.True(Play(sim, ai, () => beasts.Any(id => sim.Units.IndexOf(id) is >= 0 and var i && sim.Combat.Stampeding(i)), 10 * Tps),
            "a stampede loosed at the enemy");
        Assert.True(Play(sim, ai, () => sim.Combat.Trampled > 0, 10 * Tps), "the stampede trampling them");
        Assert.Contains(ai.Journal, j => j.Text.StartsWith("looses a stampede"));
        _ = foes;
    }
}
