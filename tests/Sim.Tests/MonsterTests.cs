using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Wild monsters and their lairs, and the Orcs' taming (issue #13).</summary>
public class MonsterTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    // Players are given the factions in data order: the Orcs tame monsters, the Settlers do not.
    private const int Settlers = 0, Orcs = 1, Wild = Simulation.WildPlayer;
    private const int DenX = 60, DenZ = 48;

    private static Simulation Flat(int size = 96) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Lair PlaceLair(Simulation sim, int x = DenX, int z = DenZ) =>
        sim.Monsters.Place(sim.Content.Unit("wolf"), x, z) ?? throw new InvalidOperationException("no ground for the den");

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static UnitDef DefOf(Simulation sim, int id) => sim.Content.Units[sim.Units.Def[Index(sim, id)]];

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    private static int Owner(Simulation sim, int id) => sim.Units.Owner[Index(sim, id)];

    private static bool Subdued(Simulation sim, int id) => sim.Units.Subdued[Index(sim, id)];

    /// <summary>How far unit id stands from the middle of the lair's den, in whole blocks (rounded).</summary>
    private static int FromDen(Simulation sim, int id, Lair lair)
    {
        int i = Index(sim, id);
        Fix64 dx = sim.Units.X[i] - (Fix64.FromInt(lair.X) + Fix64.Half), dz = sim.Units.Z[i] - (Fix64.FromInt(lair.Z) + Fix64.Half);
        return Fix64.Sqrt(dx * dx + dz * dz).RoundToInt();
    }

    /// <summary>Spawns units of one type in a block of rows of four, from column (x, z) eastward and southward.</summary>
    private static int[] Squad(Simulation sim, int owner, string unit, int x, int z, int count) =>
        Enumerable.Range(0, count).Select(k => sim.SpawnUnit(owner, unit, x + k % 4, z + k / 4 * 2)).ToArray();

    /// <summary>A wild wolf at (x, z), down to 1 hit point, beaten by an orc spearman beside it. Returns the wolf.</summary>
    private static int BeatAWolf(Simulation sim, int x, int z, out int spearman)
    {
        int wolf = sim.SpawnUnit(Wild, "wolf", x, z);
        spearman = sim.SpawnUnit(Orcs, "spearman", x - 1, z);
        sim.Units.Hp[Index(sim, wolf)] = 1;
        sim.Schedule(sim.Tick, new AttackCommand(Orcs, new[] { spearman }, wolf));
        RunUntil(sim, () => Owner(sim, wolf) == Orcs, 3 * Tps, "the spearman beats the wolf");
        return wolf;
    }

    /// <summary>Steps until the condition holds; fails the test if it takes longer than maxTicks. Returns the ticks taken.</summary>
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

    // --- Issue #13's check -------------------------------------------------------------------------

    /// <summary>
    /// Orc spearmen clear a wolf lair. Each wolf they beat is theirs, waiting for their choice: one is
    /// recruited and joins the army at full health, and one is ridden by the nearest spearman, who becomes
    /// a wolf rider more than twice as fast. Clearing the lair gives the Orcs no goods.
    /// </summary>
    [Fact]
    public void OrcsClearAWolfLairThenRecruitOneWolfAndRideAnother()
    {
        var sim = Flat();
        var hall = Place(sim, Orcs, "town_hall", 10, 10);
        var lair = PlaceLair(sim);
        var wolves = lair.Pack.ToArray();
        Assert.Equal(4, wolves.Length);
        Assert.All(wolves, w => Assert.Equal(Wild, Owner(sim, w)));

        var spearmen = Squad(sim, Orcs, "spearman", DenX - 24, DenZ - 2, 8);
        sim.Schedule(0, new AttackMoveCommand(Orcs, spearmen, DenX, DenZ));
        RunUntil(sim, () => lair.Cleared, 60 * Tps, "the orcs clear the lair");

        Assert.Equal(Orcs, lair.ClearedBy);
        Assert.Empty(lair.Pack);
        foreach (int w in wolves)
        {
            Assert.True(sim.Units.Exists(w), "a wolf the orcs beat is not killed");
            Assert.Equal(Orcs, Owner(sim, w));
            Assert.True(Subdued(sim, w));
            Assert.Equal(1, Hp(sim, w));
        }
        Assert.All(hall.Stock, n => Assert.Equal(0, n)); // no reward for a faction that tames
        Assert.Equal(4, sim.Combat.Kills(Orcs));

        // A beaten wolf takes no orders until the player chooses.
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { wolves[0] }, 10, DenZ));
        sim.Run(Tps);
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, wolves[0])]);

        // Recruited, it joins the army at full health and takes orders.
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { wolves[0] }, TameChoice.Recruit));
        sim.Step();
        Assert.False(Subdued(sim, wolves[0]));
        Assert.Equal(60, Hp(sim, wolves[0]));
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { wolves[0] }, 10, DenZ));
        sim.Step();
        Assert.Equal(UnitOrder.Move, sim.Units.Order[Index(sim, wolves[0])]);

        // Ridden: the nearest spearman climbs on and becomes a wolf rider, keeping its share of hit points.
        int ri = sim.Monsters.RiderFor(Index(sim, wolves[1]));
        Assert.True(ri >= 0, "a spearman is near the wolf");
        int rider = sim.Units.Id[ri];
        Assert.Equal("spearman", DefOf(sim, rider).Id);
        int hp = Hp(sim, rider);
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { wolves[1] }, TameChoice.Ride));
        sim.Step();
        Assert.False(sim.Units.Exists(wolves[1]));
        Assert.Equal("wolf_rider", DefOf(sim, rider).Id);
        Assert.Equal(Math.Max(1, hp * 110 / 80), Hp(sim, rider));
        Assert.True(Subdued(sim, wolves[2]) && Subdued(sim, wolves[3]), "the other wolves still wait");

        // The speed bonus: a wolf rider runs 9 blocks a second, a spearman walks 4.
        int walker = spearmen.First(id => id != rider && sim.Units.Exists(id));
        var from = (Rider: sim.Units.X[Index(sim, rider)], Walker: sim.Units.X[Index(sim, walker)]);
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { rider }, 2, DenZ - 12));
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { walker }, 2, DenZ + 12));
        sim.Run(3 * Tps);
        int riderRan = (from.Rider - sim.Units.X[Index(sim, rider)]).RoundToInt();
        int walkerWalked = (from.Walker - sim.Units.X[Index(sim, walker)]).RoundToInt();
        Assert.True(riderRan > 2 * walkerWalked, $"the rider ran {riderRan} blocks to the spearman's {walkerWalked}");
        Assert.InRange(walkerWalked, 6, 13);
    }

    /// <summary>
    /// The Settlers do not tame: the wolves they beat die, and clearing the lair puts its reward in their
    /// storage nearest the den.
    /// </summary>
    [Fact]
    public void SettlersWhoClearALairGetItsRewardInstead()
    {
        var sim = Flat();
        var far = Place(sim, Settlers, "town_hall", 6, 80);
        var near = Place(sim, Settlers, "town_hall", 10, 40);
        var lair = PlaceLair(sim);
        var wolves = lair.Pack.ToArray();

        var spearmen = Squad(sim, Settlers, "spearman", DenX - 24, DenZ - 2, 8);
        sim.Schedule(0, new AttackMoveCommand(Settlers, spearmen, DenX, DenZ));
        RunUntil(sim, () => lair.Cleared, 60 * Tps, "the settlers clear the lair");

        Assert.Equal(Settlers, lair.ClearedBy);
        Assert.All(wolves, w => Assert.False(sim.Units.Exists(w)));
        Assert.Equal(4, sim.Combat.Kills(Settlers));
        Assert.Equal(150, near.Stock[(int)Good.Food]);
        Assert.Equal(20, near.Stock[(int)Good.Iron]);
        Assert.All(far.Stock, n => Assert.Equal(0, n));
        Assert.True(spearmen.Where(sim.Units.Exists).Sum(id => sim.Units.Xp[Index(sim, id)]) >= 240, "the fight earned them experience");

        // A cleared lair pays once.
        sim.Run(5 * Tps);
        Assert.Equal(150, near.Stock[(int)Good.Food]);
    }

    /// <summary>The reward goes to whoever gets the last of the pack: here the Orcs beat three wolves and the Settlers kill the fourth.</summary>
    [Fact]
    public void TheLairsRewardGoesToWhoeverGetsTheLastWolf()
    {
        var sim = Flat();
        var hall = Place(sim, Settlers, "town_hall", 10, 40);
        var lair = PlaceLair(sim);
        var wolves = lair.Pack.ToArray();
        foreach (int w in wolves) sim.Units.Hp[Index(sim, w)] = 1;
        // One archer at a time shoots one wolf, then leaves: three orcs, then a settler.
        for (int k = 0; k < wolves.Length; k++)
        {
            int w = wolves[k], player = k < 3 ? Orcs : Settlers;
            int archer = sim.SpawnUnit(player, "archer", DenX, player == Orcs ? DenZ - 8 : DenZ + 8);
            sim.Schedule(sim.Tick, new AttackCommand(player, new[] { archer }, w));
            RunUntil(sim, () => !sim.Units.Exists(w) || Owner(sim, w) != Wild, 5 * Tps, $"wolf {k} is beaten");
            sim.Units.Remove(archer);
        }
        sim.Step();
        Assert.True(lair.Cleared);
        Assert.Equal(Settlers, lair.ClearedBy);
        Assert.Equal(150, hall.Stock[(int)Good.Food]);
        Assert.Equal(3, wolves.Count(w => sim.Units.Exists(w) && Owner(sim, w) == Orcs));
    }

    // --- The pack ------------------------------------------------------------------------------------

    /// <summary>
    /// The pack goes after units near its den, workers too, even out of its sight; units further out are
    /// left alone, and once the fight is over the wolves go back to the den.
    /// </summary>
    [Fact]
    public void WolvesGoAfterWorkersNearTheirDenAndThenGoHome()
    {
        var sim = Flat();
        var lair = PlaceLair(sim);
        // 11.3 blocks from the middle of the den: inside its guard radius of 12, out of the wolves' sight of 9.
        int near = sim.SpawnUnit(Settlers, "worker", DenX + 8, DenZ + 8);
        // 22.6 blocks off.
        int far = sim.SpawnUnit(Settlers, "worker", DenX + 16, DenZ + 16);
        RunUntil(sim, () => lair.Pack.Any(w => sim.Units.Target[Index(sim, w)] == near), MonsterSystem.GuardInterval + 1,
            "the pack goes for the worker near the den");
        RunUntil(sim, () => !sim.Units.Exists(near), 15 * Tps, "the wolves kill the worker");
        sim.Run(10 * Tps);
        Assert.Equal(40, Hp(sim, far));
        Assert.Equal(4, lair.Pack.Count);
        Assert.All(lair.Pack, w => Assert.True(FromDen(sim, w, lair) <= MonsterSystem.HomeRadius + 1,
            $"wolf {w} is {FromDen(sim, w, lair)} blocks from its den"));
    }

    /// <summary>A rider hits a wolf and runs: the pack gives chase, but not past its leash, and then goes home.</summary>
    [Fact]
    public void WolvesLuredPastTheirLeashBreakOffAndGoHome()
    {
        var sim = Flat(128);
        var lair = PlaceLair(sim, 100, 64);
        int leash = lair.Monster.Lair!.Leash;
        // An outrider, faster than a wolf, so it can get away.
        int rider = sim.SpawnUnit(Settlers, "outrider", 90, 64);
        sim.Schedule(0, new AttackCommand(Settlers, new[] { rider }, lair.Pack[0]));
        RunUntil(sim, () => lair.Pack.Count(w => sim.Units.Target[Index(sim, w)] == rider) >= 2, 5 * Tps, "the pack goes for the rider");
        sim.Schedule(sim.Tick, new MoveCommand(Settlers, new[] { rider }, 4, 64));
        int furthest = 0;
        for (int t = 0; t < 12 * Tps; t++)
        {
            sim.Step();
            foreach (int w in lair.Pack) furthest = Math.Max(furthest, FromDen(sim, w, lair));
        }
        Assert.InRange(furthest, leash - 4, leash + 3);
        Assert.True(sim.Units.Exists(rider), "the rider got away");
        Assert.True(FromDen(sim, rider, lair) > leash + 20);
        sim.Run(10 * Tps);
        Assert.All(lair.Pack, w => Assert.True(FromDen(sim, w, lair) <= MonsterSystem.HomeRadius + 1,
            $"wolf {w} is {FromDen(sim, w, lair)} blocks from its den"));
    }

    [Fact]
    public void MonstersNeverBreak()
    {
        var sim = Flat();
        var lair = PlaceLair(sim);
        int spearman = sim.SpawnUnit(Settlers, "spearman", 10, 10);
        foreach (int w in lair.Pack) sim.Units.Morale[Index(sim, w)] = 5;
        sim.Units.Morale[Index(sim, spearman)] = 5;
        sim.Step();
        Assert.All(lair.Pack, w => Assert.Equal(0, sim.Units.Rout[Index(sim, w)]));
        Assert.True(sim.Units.Rout[Index(sim, spearman)] > 0, "a spearman breaks");
    }

    /// <summary>Wild monsters belong to no player: their own side, which eats nothing and earns no experience.</summary>
    [Fact]
    public void TheWildSideIsNoPlayer()
    {
        var sim = Flat();
        var lair = PlaceLair(sim);
        Assert.Same(sim.Content.Wild, sim.Economy.Faction(Wild));
        Assert.DoesNotContain(sim.Content.Wild, sim.Content.Factions);
        Assert.Null(sim.Evolution.Rules(Wild));
        sim.Run(EconomySystem.UpkeepInterval + 1);
        Assert.False(sim.Economy.Starving(Wild));
        Assert.Equal(4, sim.Economy.Population(Wild));
        Assert.Equal(0, sim.Economy.Population(Settlers));
        Assert.All(lair.Pack, w => Assert.Equal(0, sim.Units.Xp[Index(sim, w)]));
    }

    // --- Beaten monsters -----------------------------------------------------------------------------

    /// <summary>
    /// A beaten wolf is out of the fight: it takes no orders, fights nobody and nobody goes for it on their
    /// own, and only its owner can tame it. Riding it needs a spearman within range: other units cannot.
    /// </summary>
    [Fact]
    public void ABeatenWolfWaitsForItsOwnerAndNeedsASpearmanNearbyToBeRidden()
    {
        var sim = Flat();
        int wolf = BeatAWolf(sim, 40, 40, out int spearman);
        Assert.True(Subdued(sim, wolf));
        Assert.Equal(1, Hp(sim, wolf));

        // Its spearman walks off, out of riding range; the wolf ignores an order to follow.
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { spearman }, 40, 10));
        sim.Schedule(sim.Tick, new MoveCommand(Orcs, new[] { wolf }, 40, 10));
        sim.Run(9 * Tps);
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, wolf)]);

        // An enemy beside it leaves it alone, and cannot tame it.
        int enemy = sim.SpawnUnit(Settlers, "spearman", 41, 41);
        sim.Schedule(sim.Tick, new TameCommand(Settlers, new[] { wolf }, TameChoice.Recruit));
        sim.Run(2 * Tps);
        Assert.Equal(0, sim.Units.Target[Index(sim, enemy)]);
        Assert.Equal(1, Hp(sim, wolf));
        Assert.True(Subdued(sim, wolf));
        Assert.Equal(Orcs, Owner(sim, wolf));
        sim.Units.Remove(enemy);

        // No spearman within range: riding does nothing. An archer beside it cannot ride a wolf.
        Assert.Equal(-1, sim.Monsters.RiderFor(Index(sim, wolf)));
        int archer = sim.SpawnUnit(Orcs, "archer", 41, 40);
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { wolf }, TameChoice.Ride));
        sim.Step();
        Assert.True(Subdued(sim, wolf));
        Assert.Equal("archer", DefOf(sim, archer).Id);

        // A spearman comes within range and climbs on.
        int second = sim.SpawnUnit(Orcs, "spearman", 46, 40);
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { wolf, archer, 999 }, TameChoice.Ride));
        sim.Step();
        Assert.False(sim.Units.Exists(wolf));
        Assert.Equal("wolf_rider", DefOf(sim, second).Id);
        Assert.Equal(110, Hp(sim, second));
        Assert.Equal("spearman", DefOf(sim, spearman).Id);
        Assert.Equal("archer", DefOf(sim, archer).Id);
    }

    [Fact]
    public void AnAttackOrderCanStillKillABeatenWolf()
    {
        var sim = Flat();
        int wolf = BeatAWolf(sim, 40, 40, out int spearman);
        sim.Units.Remove(spearman);
        int enemy = sim.SpawnUnit(Settlers, "spearman", 42, 40);
        sim.Schedule(sim.Tick, new AttackCommand(Settlers, new[] { enemy }, wolf));
        RunUntil(sim, () => !sim.Units.Exists(wolf), 3 * Tps, "the settler kills the beaten wolf");
        Assert.Equal(1, sim.Combat.Losses(Orcs));
    }

    /// <summary>Recruiting and riding many at once: each wolf takes the nearest spearman still on foot.</summary>
    [Fact]
    public void EachRiddenWolfTakesTheNearestSpearmanStillOnFoot()
    {
        var sim = Flat();
        int a = BeatAWolf(sim, 30, 30, out int s);
        sim.Units.Remove(s);
        int b = BeatAWolf(sim, 40, 30, out s);
        sim.Units.Remove(s);
        int c = BeatAWolf(sim, 70, 30, out s);
        sim.Units.Remove(s);
        int p = sim.SpawnUnit(Orcs, "spearman", 37, 30); // 7 blocks from a, 3 from b
        int q = sim.SpawnUnit(Orcs, "spearman", 22, 30); // 8 blocks from a, out of range of b
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { b, a }, TameChoice.Ride));
        sim.Schedule(sim.Tick, new TameCommand(Orcs, new[] { c, c }, TameChoice.Recruit));
        sim.Step();
        Assert.False(sim.Units.Exists(a) || sim.Units.Exists(b));
        // b went first and took p, its nearest; a's nearest, p, was riding already, so q climbed on a.
        Assert.Equal("wolf_rider", DefOf(sim, p).Id);
        Assert.Equal("wolf_rider", DefOf(sim, q).Id);
        Assert.False(Subdued(sim, c));
        Assert.Equal(60, Hp(sim, c));
    }

    // --- Lairs in a match ----------------------------------------------------------------------------

    [Fact]
    public void EveryStartHasAWolfLairOnTheWayToTheMapCentre()
    {
        var sim = new Simulation(1234, 256, 64, 256);
        sim.PlaceStartingBuildings();
        sim.SpawnStartingUnits(12);
        var lairs = sim.SpawnLairs();
        Assert.Equal(2, sim.Map.Starts.Count);
        Assert.Equal(sim.Map.Starts.Count, lairs.Count);
        int cx = sim.World.SizeX / 2, cz = sim.World.SizeZ / 2;
        for (int k = 0; k < lairs.Count; k++)
        {
            var (lair, start) = (lairs[k], sim.Map.Starts[k]);
            Assert.Equal("wolf", lair.Monster.Id);
            long fromStart = Sq(lair.X - start.X) + Sq(lair.Z - start.Z);
            Assert.InRange(fromStart, Sq(MonsterSystem.MinLairDistance), Sq(MonsterSystem.LairDistance + 1));
            Assert.True(Sq(lair.X - cx) + Sq(lair.Z - cz) < Sq(start.X - cx) + Sq(start.Z - cz), "the den is nearer the centre than the start");
            Assert.Equal(BlockType.Air, sim.World.Get(lair.X, lair.Y, lair.Z)); // not under water
            Assert.Equal(4, lair.Pack.Count);
            Assert.All(lair.Pack, w => Assert.Equal(Wild, Owner(sim, w)));
            Assert.All(lair.Pack, w => Assert.True(FromDen(sim, w, lair) <= 12));
        }
        // The players' own numbers leave the wolves out.
        Assert.Equal(12, sim.Economy.Population(0));
        Assert.Equal(12, sim.Economy.Population(1));
        Assert.Equal(8, sim.Economy.Population(Wild));
    }

    private static long Sq(long v) => v * v;

    [Fact]
    public void LairsAndTameCommandsRoundTripThroughTheLog()
    {
        var log = new CommandLog { Seed = 1, SizeX = 64, SizeY = 16, SizeZ = 64, Ticks = 10, StartLairs = true };
        log.Add(1, new TameCommand(Orcs, new[] { 90, 91 }, TameChoice.Ride));
        log.Add(2, new TameCommand(Orcs, new[] { 92 }, TameChoice.Recruit));
        string text = log.ToString();
        Assert.Contains("\nstart-lairs 1\n", text);
        Assert.Contains("\n1 tame 1 ride 90,91\n", text);
        Assert.Contains("\n2 tame 1 recruit 92\n", text);
        var copy = CommandLog.Parse(text);
        Assert.True(copy.StartLairs);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());
        Assert.False(CommandLog.Parse(text.Replace("start-lairs 1\n", "")).StartLairs);
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("ride", "eat")));
        Assert.Throws<FormatException>(() => CommandLog.Parse(text.Replace("start-lairs 1", "start-lairs 2")));
    }

    /// <summary>Scripted matches have lairs: the players fight them, tame what the Orcs beat, and replay the same.</summary>
    [Fact]
    public void ScriptedMatchesFightTheLairsAndReplayTheSame()
    {
        var log = ScriptedMatch.Generate(3, 256, 64, 256, ticks: 6000, startUnits: 40);
        Assert.True(log.StartLairs);
        Assert.Contains(log.Commands, c => c.Command is TameCommand);
        var hashes = new ulong[2];
        int cleared = 0, tamed = 0;
        for (int run = 0; run < 2; run++)
        {
            var sim = log.CreateSimulation();
            // A lair for each start, and one by each city-state's town.
            Assert.Equal(2 + sim.Cities.Towns.Count(t => t.LairId > 0), sim.Monsters.Lairs.Count);
            foreach (var c in log.Commands) sim.Schedule(c.Tick, c.Command);
            for (long t = 0; t < log.Ticks; t += 100)
            {
                sim.Run(100);
                tamed = Math.Max(tamed, Enumerable.Range(0, sim.Units.Count).Count(i =>
                    sim.Units.Owner[i] == Orcs && sim.Content.Units[sim.Units.Def[i]].Id is "wolf" or "wolf_rider" && !sim.Units.Subdued[i]));
            }
            cleared = sim.Monsters.Lairs.Count(l => l.Cleared);
            hashes[run] = sim.StateHash();
        }
        Assert.Equal(hashes[0], hashes[1]);
        Assert.True(cleared > 0, "no lair was cleared");
        Assert.True(tamed > 0, "the orcs rode or recruited no wolf");
    }

    // --- The data ------------------------------------------------------------------------------------

    [Fact]
    public void TheWolfIsData()
    {
        var db = ContentDb.Default;
        Assert.True(db.Faction("orcs").TamesMonsters);
        Assert.False(db.Faction("settlers").TamesMonsters);
        Assert.Equal(new[] { "wolf" }, db.LairMonsters.Select(u => u.Id));
        Assert.DoesNotContain(db.BaseUnits, u => u.Id is "wolf" or "wolf_rider");
        var wolf = db.Unit("wolf");
        Assert.Equal(UnitRole.Monster, wolf.Role);
        Assert.Equal((4, 12, 20), (wolf.Lair!.Pack, wolf.Lair.Guard, wolf.Lair.Leash));
        Assert.Equal(new[] { new GoodAmount(Good.Food, 150), new GoodAmount(Good.Iron, 20) }, wolf.Lair.Reward);
        Assert.Same(db.Unit("spearman"), wolf.Ride!.Rider);
        Assert.Same(db.Unit("wolf_rider"), wolf.Ride.Becomes);
        Assert.Null(db.Unit("wolf_rider").TrainedAt);
        Assert.Equal("wild", db.Wild.Id);
    }

    [Fact]
    public void MonsterDataIsChecked()
    {
        var files = Files();
        Edit(files, "units.json", "\"pack\": 4, \"guard\": 12, \"leash\": 20", "\"pack\": 0, \"guard\": 12, \"leash\": 10, \"lure\": 3");
        Edit(files, "units.json", "\"ride\": { \"rider\": \"spearman\", \"becomes\": \"wolf_rider\" }", "\"ride\": { \"rider\": \"orc\", \"becomes\": \"wolf\" }");
        Edit(files, "units.json", "\"id\": \"worker\", \"name\": \"Worker\", \"role\": \"worker\",", "\"id\": \"worker\", \"name\": \"Worker\", \"role\": \"worker\", \"lair\": { \"pack\": 2 },");
        Edit(files, "factions.json", "\"tamesMonsters\": true", "\"tamesMonsters\": \"yes\"");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("units.json: unit 'wolf', lair: \"pack\" is 0; it should be from 1 to 50", e.Errors);
        Assert.Contains("units.json: unit 'wolf', lair: \"leash\" is 10, but the pack guards 12 blocks around the den, so it should be at least that", e.Errors);
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'wolf', lair: unknown field \"lure\""));
        Assert.Contains("units.json: unit 'wolf': \"ride\": \"rider\" is \"orc\", which is not a unit", e.Errors);
        Assert.Contains("units.json: unit 'wolf': \"ride\": a monster cannot ride itself or be what its rider becomes", e.Errors);
        Assert.Contains("units.json: unit 'worker': lives in lairs, so it is not trained or evolved into: it cannot have \"trainedAt\", \"evolvesFrom\", \"cost\" or \"trainTime\"", e.Errors);
        Assert.Contains("units.json: unit 'worker': lives in lairs, so its \"role\" should be monster", e.Errors);
        Assert.Contains("factions.json: faction 'orcs': \"tamesMonsters\" should be true or false", e.Errors);
        Assert.Equal(8, e.Errors.Count);

        files = Files();
        Edit(files, "factions.json", "\"id\": \"settlers\"", "\"id\": \"wild\"");
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains("factions.json: faction 'wild': \"wild\" is the monsters' side, which the game provides", e.Errors);
    }

    /// <summary>The content hash covers the lair and taming numbers, so a replay with other numbers says so.</summary>
    [Fact]
    public void TheContentHashCoversLairsAndTaming()
    {
        ulong Hash(string file, string old, string replacement)
        {
            var files = Files();
            Edit(files, file, old, replacement);
            return ContentLoader.Load(files).Hash;
        }
        ulong original = ContentDb.Default.Hash;
        Assert.NotEqual(original, Hash("units.json", "\"pack\": 4", "\"pack\": 5"));
        Assert.NotEqual(original, Hash("units.json", "\"reward\": { \"food\": 150", "\"reward\": { \"food\": 151"));
        Assert.NotEqual(original, Hash("units.json", "\"rider\": \"spearman\"", "\"rider\": \"archer\""));
        Assert.NotEqual(original, Hash("factions.json", ", \"tamesMonsters\": true", ""));
        Assert.Equal(original, Hash("units.json", "Hunts in packs", "Runs in packs")); // descriptions do not count
    }

    private static Dictionary<string, string> Files()
    {
        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        return files;
    }

    private static void Edit(Dictionary<string, string> files, string file, string old, string replacement)
    {
        string text = files[file];
        int at = text.IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{file} does not contain {old}");
        files[file] = text[..at] + replacement + text[(at + old.Length)..];
    }
}
