using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Hard computer player (issue #2), and what every level but Classic may know: only what it has seen.
/// </summary>
public class HardAiTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static readonly AiLevel[] FairLevels = { AiLevel.Easy, AiLevel.Normal, AiLevel.Hard };

    /// <summary>A flat world with a start area (and so a town hall with the starting goods) for each column given.</summary>
    private static Simulation WithStarts(int size, params (int X, int Z)[] starts)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    /// <summary>Steps the match with the computer players thinking before every tick, until done or maxTicks; keeps their commands.</summary>
    private static bool Play(Simulation sim, IReadOnlyList<AiPlayer> players, Func<bool> done, int maxTicks, List<Command>? log = null)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            foreach (var ai in players)
                ai.Think(sim, c =>
                {
                    log?.Add(c);
                    sim.Schedule(sim.Tick, c);
                });
            sim.Step();
        }
        return done();
    }

    private static (int X, int Z) Middle(Building b) => (b.X + b.Def.SizeX / 2, b.Z + b.Def.SizeZ / 2);

    // --- Only what it has seen -------------------------------------------------------------------------

    [Fact]
    public void UnderFogNoLevelButClassicKnowsOfATownHallItHasNotSeen()
    {
        foreach (var level in FairLevels)
        {
            // 150 columns apart: nothing of player 1 is in sight. With fog it knows of none of their buildings.
            var sim = WithStarts(192, (20, 96), (170, 96));
            sim.Fog = true;
            for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "spearman", 14 + k, 106);
            var ai = new AiPlayer(0, 1, level);
            Play(sim, new[] { ai }, () => false, 5 * Tps);
            Assert.False(ai.KnowsEnemyHall, $"{level} knows a town hall it has not seen");
            Assert.Empty(ai.KnownBuildings);

            // Without fog everything is in sight, as it is for the person playing.
            var open = WithStarts(192, (20, 96), (170, 96));
            for (int k = 0; k < 4; k++) open.SpawnUnit(0, "spearman", 14 + k, 106);
            var sees = new AiPlayer(0, 1, level);
            Play(open, new[] { sees }, () => false, 5 * Tps);
            Assert.True(sees.KnowsEnemyHall, $"{level} without fog");
        }
    }

    [Fact]
    public void ABuildingIsRememberedUntilThePlaceIsSeenWithoutIt()
    {
        foreach (var level in FairLevels)
        {
            var sim = WithStarts(192, (20, 96), (170, 96));
            sim.Fog = true;
            sim.TestMode = true;
            var store = sim.Economy.PlaceFinished(1, sim.Content.Building("storehouse"), 80, 96)!;
            int watcher = sim.SpawnUnit(0, "spearman", 76, 99);
            var ai = new AiPlayer(0, 1, level);
            var players = new[] { ai };
            Assert.True(Play(sim, players, () => ai.KnownBuildings.Contains(store.Id), 5 * Tps), $"{level} to see the storehouse");

            // The watcher goes home, and the storehouse comes down out of its sight: it still knows of it.
            var (sx, sz) = Middle(store);
            sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, new[] { watcher }, SandboxAction.Remove));
            Play(sim, players, () => false, 2 * Tps);
            Assert.False(sim.Units.Exists(watcher));
            Assert.False(sim.Vision.Sees(0, sx, sz));
            sim.Schedule(sim.Tick, new SandboxClearCommand(0, sx, sz, 4));
            Play(sim, players, () => false, 3 * Tps);
            Assert.Null(sim.Buildings.Get(store.Id));
            Assert.Contains(store.Id, ai.KnownBuildings);

            // Someone looks at the place again: gone.
            sim.SpawnUnit(0, "spearman", 76, 99);
            Assert.True(Play(sim, players, () => !ai.KnownBuildings.Contains(store.Id), 5 * Tps), $"{level} to see it gone");
        }
    }

    [Fact]
    public void TheScoutSweepsRoundItsGuessUntilItFindsATownHallOffTheMirrorPoint()
    {
        // Player 1's town hall is some thirty columns from the point across the map from player 0's: the
        // scout sees nothing at its guess, sweeps the ground round it, and finds the town hall.
        var sim = WithStarts(192, (20, 96), (150, 122));
        sim.Fog = true;
        int rider = sim.SpawnUnit(0, "rider", 24, 104);
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "spearman", 14 + k, 106);
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var players = new[] { ai };
        var log = new List<Command>();
        Play(sim, players, () => false, 2 * Tps, log);
        Assert.Equal(rider, ai.Scout);
        // Straight across the map from its own town hall, to a point a little short of there (it is enough to see it).
        var first = log.OfType<MoveCommand>().First(m => m.Units.Contains(rider));
        var (hx, hz) = Middle(sim.Economy.TownHall(0)!);
        Assert.InRange(first.X, 191 - hx - AiPlayer.LookShort, 191 - hx - 1);
        Assert.InRange(first.Z, 191 - hz - 1, 191 - hz + 1);
        Assert.True(Play(sim, players, () => ai.KnowsEnemyHall, 3 * 60 * Tps, log), "the scout to find the town hall");
        Assert.True(log.OfType<MoveCommand>().Count(m => m.Units.Contains(rider)) >= 2, "a sweep after the guess");
    }

    [Fact]
    public void AScoutSentWhereABuildingStandsTriesFromFurtherBackAndFindsTheTown()
    {
        // Player 1's town hall stands right on the point across the map from player 0's, and a house of theirs
        // on the point short of it the scout is sent to: a move order to a column with a building on it ends
        // on the roof, which there is no way up to. The scout stops, tries from further back, and sees the town.
        var sim = WithStarts(192, (20, 96), (171, 95));
        sim.Fog = true;
        var (hx, hz) = Middle(sim.Economy.TownHall(0)!);
        var (gx, gz) = (191 - hx, 191 - hz);
        Assert.Equal(1, sim.Buildings.AtColumn(gx, gz)?.Owner);
        Assert.NotNull(sim.Economy.PlaceFinished(1, sim.Content.Building("house"), gx - AiPlayer.LookShort - 3, gz - 2));
        int rider = sim.SpawnUnit(0, "rider", 24, 104);
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "spearman", 14 + k, 106);
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var log = new List<Command>();
        Assert.True(Play(sim, new[] { ai }, () => ai.KnowsEnemyHall, 60 * Tps, log), "the scout to find the town hall");
        var moves = log.OfType<MoveCommand>().Where(m => m.Units.Contains(rider)).ToList();
        Assert.Equal(1, sim.Buildings.AtColumn(moves[0].X, moves[0].Z)?.Owner); // the first try: onto their house
        Assert.True(moves.Count >= 2, "a second try");
        Assert.True(moves[1].X <= moves[0].X - AiPlayer.LookBack, $"the second try, at {moves[1].X}, is further back than {moves[0].X}");
        Assert.Null(sim.Buildings.AtColumn(moves[1].X, moves[1].Z));
        Assert.DoesNotContain(ai.Journal, j => j.Text.Contains("gives the place up"));
    }

    // --- Hard: its army ----------------------------------------------------------------------------------

    [Fact]
    public void HardWaitsUntilItIsHalfAgainAsStrongThenLooksAndThenAttacks()
    {
        var sim = WithStarts(192, (20, 96), (170, 96));
        sim.Fog = true;
        int rider = sim.SpawnUnit(0, "rider", 24, 104);
        for (int k = 0; k < 19; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 106 + k / 10);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        var log = new List<Command>();

        // With what it started with it takes the enemy to have as much: no wave, and no scout risked either.
        Play(sim, players, () => false, 60 * Tps, log);
        Assert.Equal(0, ai.Waves);
        Assert.False(ai.KnowsEnemyHall);
        Assert.DoesNotContain(log, c => c is MoveCommand m && m.Units.Contains(rider) && m.X > 150);

        // Half as many again and more: now it would attack, so first the rider goes for a look...
        for (int k = 0; k < 24; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 110 + k / 10);
        Assert.True(Play(sim, players, () => ai.Scout == rider, 10 * Tps, log), "the scout to set out");
        Assert.Equal(0, ai.Waves);
        // ...and once it has seen their town hall the wave sets out, in formation.
        Assert.True(Play(sim, players, () => ai.Waves == 1, 2 * 60 * Tps, log), "the wave to set out");
        Assert.True(ai.KnowsEnemyHall);
        Assert.True(ai.Wave.Count >= AiPlayer.HardWave);
        Assert.Contains(log, c => c is FormationMoveCommand f && f.Attack && f.Units.Length == ai.Wave.Count);
        Assert.Contains(ai.Journal, j => j.Text.StartsWith("sends a wave of"));
    }

    [Fact]
    public void AWaveWithNothingItKnowsOfToGoForSearchesUntilItFindsTheEnemy()
    {
        // No rider to scout with, and the enemy town hall is thirty columns off the guess: the wave goes to
        // the guess, sees nothing there, and looks round it until it finds the town hall.
        var sim = WithStarts(192, (20, 96), (150, 122));
        sim.Fog = true;
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 10 + k, 106);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        Play(sim, players, () => false, 2 * Tps);
        for (int k = 0; k < 16; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 108 + k / 10);
        Assert.True(Play(sim, players, () => ai.Waves == 1, 20 * Tps), "the wave to set out");
        Assert.False(ai.KnowsEnemyHall);
        Assert.True(Play(sim, players, () => ai.KnowsEnemyHall, 4 * 60 * Tps), "the wave to find the town hall");
        Assert.True(ai.Wave.Count > 0);
    }

    [Fact]
    public void WhileHardsWaveLooksForTheEnemyARiderLooksToo()
    {
        // The wave sets out for where it guesses the enemy is with no rider to send ahead for a look...
        var sim = WithStarts(192, (20, 96), (150, 122));
        sim.Fog = true;
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 10 + k, 106);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        Play(sim, players, () => false, 2 * Tps);
        for (int k = 0; k < 16; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 108 + k / 10);
        Assert.True(Play(sim, players, () => ai.Waves == 1, 20 * Tps), "the wave to set out");
        Assert.Equal(0, ai.Scout);
        // ...and then it has one: the rider does not wait at home for the wave to come back, it goes looking too.
        int rider = sim.SpawnUnit(0, "rider", 24, 104);
        Assert.True(Play(sim, players, () => ai.Scout == rider, 5 * Tps), "the rider to go looking");
        Assert.True(ai.Wave.Count > 0 && !ai.Wave.Contains(rider));
        Assert.True(Play(sim, players, () => ai.KnowsEnemyHall, 3 * 60 * Tps), "the town hall to be found");
    }

    [Fact]
    public void HardMeetsRaidersAtAFarBuildingWithAPartyAndAnAttackOnItsTownWithEveryone()
    {
        // One enemy spearman by a storehouse forty columns from the town hall: three of the twelve go.
        var sim = WithStarts(160, (40, 80));
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 12; k++) sim.SpawnUnit(0, "spearman", hall.X + k % 6, hall.MaxZ + 4 + k / 6);
        sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 76, 78);
        int raider = sim.SpawnUnit(1, "spearman", 82, 86);
        sim.Schedule(0, new StanceCommand(1, new[] { raider }, UnitStance.Hold));
        var log = new List<Command>();
        Play(sim, new[] { new AiPlayer(0, 1, AiLevel.Hard) }, () => false, 3 * Tps, log);
        var sent = log.Where(c => c is FormationMoveCommand or AttackMoveCommand).Cast<UnitsCommand>().ToList();
        Assert.NotEmpty(sent);
        Assert.Equal(3, sent.SelectMany(c => c.Units).Distinct().Count());

        // Six of them at the town hall: everyone, in formation.
        var town = WithStarts(160, (40, 80));
        hall = town.Economy.TownHall(0)!;
        for (int k = 0; k < 12; k++) town.SpawnUnit(0, "spearman", hall.X + k % 6, hall.MaxZ + 4 + k / 6);
        for (int k = 0; k < 6; k++) town.SpawnUnit(1, "spearman", hall.X + k, hall.MaxZ + 20);
        var townLog = new List<Command>();
        Play(town, new[] { new AiPlayer(0, 1, AiLevel.Hard) }, () => false, 3 * Tps, townLog);
        Assert.Contains(townLog, c => c is FormationMoveCommand f && f.Attack && f.Units.Length == 12);
    }

    [Fact]
    public void HardsSoldiersGatherBesideItsTownHallAndMeetWhatComesAtThemThereTogether()
    {
        // They gather where they heal as at home: by the town hall, whether or not it knows where the enemy is.
        var sim = WithStarts(160, (40, 80), (120, 80));
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 12; k++) sim.SpawnUnit(0, "spearman", hall.X + k % 6, hall.MaxZ + 4 + k / 6);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        Play(sim, new[] { ai }, () => false, 2 * Tps);
        Assert.True(ai.KnowsEnemyHall); // no fog: it sees their town
        var (gx, gz) = ai.GatheringGround(sim)!.Value;
        Assert.True(hall.ColumnDistance(gx, gz) <= sim.Content.Combat.HomeRange, $"it gathers at {gx},{gz}, too far from the town hall to heal as at home");
        Assert.True(Math.Abs(gx - Middle(hall).X) <= 2 && gz > hall.MaxZ, $"it gathers at {gx},{gz}, not beside the town hall");

        // Six enemy spearmen further from the town hall than it defends, but by where its soldiers stand: everyone, in formation.
        for (int k = 0; k < 6; k++) sim.SpawnUnit(1, "spearman", hall.X + k, gz + AiPlayer.HardHallRadius - 4);
        Assert.True(gz + AiPlayer.HardHallRadius - 4 - Middle(hall).Z > AiPlayer.HardHallRadius);
        var log = new List<Command>();
        Play(sim, new[] { ai }, () => false, 3 * Tps, log);
        Assert.Contains(log, c => c is FormationMoveCommand f && f.Attack && f.Units.Length == 12);
    }

    /// <summary>A Hard player 0 with a rider and 19 spearmen, then 24 more: its first wave has set out for player 1's town hall.</summary>
    private static (Simulation Sim, AiPlayer Ai, AiPlayer[] Players) WithAWaveOut()
    {
        var sim = WithStarts(192, (20, 96), (170, 96));
        sim.Fog = true;
        sim.TestMode = true;
        sim.SpawnUnit(0, "rider", 24, 104);
        for (int k = 0; k < 19; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 106 + k / 10);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        Play(sim, players, () => false, 2 * Tps);
        for (int k = 0; k < 24; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 110 + k / 10);
        Assert.True(Play(sim, players, () => ai.Waves == 1, 3 * 60 * Tps), "the wave to set out");
        return (sim, ai, players);
    }

    [Fact]
    public void HardCallsItsWaveHomeOnlyForAForceThoseAtHomeCannotSeeOff()
    {
        // The wave has just left, and a handful stayed. One enemy spearman by the town hall is for them; eight are not.
        foreach (int enemies in new[] { 1, 8 })
        {
            var (sim, ai, players) = WithAWaveOut();
            Play(sim, players, () => false, 3 * Tps);
            int wave = ai.Wave.Count;
            var hall = sim.Economy.TownHall(0)!;
            for (int k = 0; k < enemies; k++) sim.SpawnUnit(1, "spearman", hall.X + k, hall.MaxZ + 6); // in sight of the town hall
            Play(sim, players, () => false, 3 * Tps);
            if (enemies == 1) Assert.True(ai.Wave.Count >= wave, "the wave to stay out");
            else Assert.Empty(ai.Wave);
            Assert.Equal(enemies > 1, ai.Journal.Any(j => j.Text.StartsWith("calls its wave")));
        }
    }

    [Fact]
    public void AWaveDownToAQuarterComesHomeBeatenAndTheNextWaitsUntilItIsHalfAgainAsStrongAsThatOne()
    {
        var (sim, ai, players) = WithAWaveOut();
        int sent = ai.Wave.Count;
        var lost = ai.Wave.Skip(5).ToArray(); // all but five of them are gone
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, lost, SandboxAction.Remove));
        Assert.True(Play(sim, players, () => ai.Wave.Count == 0, 5 * Tps), "the wave to come home");
        Assert.Contains(ai.Journal, j => j.Text == $"its wave is beaten: the 5 left of {sent} come home");

        // Thirty more spearmen: more than half again what a player starts with, but not what that wave was
        // worth and half again. It has had its look at their town, and still it waits.
        for (int k = 0; k < 30; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 106 + k / 10);
        Play(sim, players, () => false, 2 * 60 * Tps);
        Assert.Equal(1, ai.Waves);
        // Thirty more again: now it goes.
        for (int k = 0; k < 30; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 110 + k / 10);
        Assert.True(Play(sim, players, () => ai.Waves == 2, 3 * 60 * Tps), "the second wave to set out");
    }

    [Fact]
    public void ASoldierShutInAHoleIsNotSentAnywhere()
    {
        // A hole one column wide and two deep, as levelling the ground for two houses on a slope can leave
        // between them, with the only rider in it: Hard does not make it its scout or put it in its wave.
        var sim = WithStarts(192, (20, 96), (170, 96));
        sim.Fog = true;
        sim.World.Set(30, Ground, 104, BlockType.Air);
        sim.World.Set(30, Ground - 1, 104, BlockType.Air);
        int rider = sim.SpawnUnit(0, "rider", 30, 104);
        Assert.Equal(Ground - 1, sim.Units.Y[sim.Units.IndexOf(rider)]);
        sim.Step(); // the hole is there before the computer player first looks
        for (int k = 0; k < 19; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 106 + k / 10);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        var log = new List<Command>();
        Play(sim, players, () => false, 2 * Tps, log);
        for (int k = 0; k < 24; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 110 + k / 10);
        // With nobody to send for a look, the wave goes without one.
        Assert.True(Play(sim, players, () => ai.Waves == 1, 30 * Tps, log), "the wave to set out");
        Assert.DoesNotContain(rider, ai.Wave);
        Assert.DoesNotContain(log, c => c is UnitsCommand u && u.Units.Contains(rider));
    }

    // --- Hard: its economy -------------------------------------------------------------------------------

    [Fact]
    public void WhileItsArmyIsShortHardTrainsSoldiersBeforeMoreWorkersUnlessItsTownIsTooSmall()
    {
        // It takes the enemy to have at least the army a player starts with, and keeps its own a fifth
        // stronger: with only what it started with, the barracks comes before the town hall; with half as
        // many soldiers again, a worker comes first. And with fewer workers than a town needs to keep an
        // army (after a raid, say) a worker comes first whatever its army is worth.
        // A minute in, a town needs 13 workers to keep an army, and Hard wants 15.
        foreach (var (workers, moreSoldiers, soldierFirst) in new[] { (AiPlayer.HardLeastWorkers + 1, false, true), (AiPlayer.HardLeastWorkers + 1, true, false), (4, false, false) })
        {
            var sim = WithStarts(96, (40, 48));
            var hall = sim.Economy.TownHall(0)!;
            var barracks = sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), hall.X - 12, hall.Z)!;
            sim.Economy.PlaceFinished(0, sim.Content.Building("house"), hall.X - 12, hall.Z + 10);
            for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", hall.X + k, hall.MaxZ + 4);
            for (int k = 0; k < workers; k++) sim.SpawnUnit(0, "worker", hall.X + k % 6, hall.MaxZ + 8 + k / 6);
            for (int t = 0; t < 61 * Tps; t++) sim.Step(); // the first minute passes
            Array.Clear(hall.Stock);
            var ai = new AiPlayer(0, 1, AiLevel.Hard);
            var players = new[] { ai };
            var log = new List<Command>();
            Play(sim, players, () => false, 1, log); // it takes stock of what it starts with
            Assert.Empty(log.OfType<TrainCommand>());
            if (moreSoldiers)
                for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "spearman", hall.X + k, hall.MaxZ + 5);
            // Food for one of the two: a spearman (40 food, 20 wood) or a worker (30 food).
            hall.Stock[(int)Good.Food] = 75;
            hall.Stock[(int)Good.Wood] = 20;
            Assert.True(Play(sim, players, () => log.OfType<TrainCommand>().Any(), 6 * Tps, log), "something to be trained");
            var train = log.OfType<TrainCommand>().First();
            Assert.Equal(soldierFirst ? barracks.Id : hall.Id, train.Building);
        }
    }

    [Fact]
    public void HardRunsItsTownItself()
    {
        // Two Hard players on a generated map for ten minutes.
        Simulation? last = null;
        IReadOnlyList<AiPlayer>? ais = null;
        var result = AiMatch.Play(3, 256, 64, 256, 10 * 60 * Tps, levels: new[] { AiLevel.Hard }, fog: true, watch: (sim, players) =>
            (last, ais) = (sim, players));
        Assert.NotNull(last);
        var log = result.Log.Commands;
        // It lays out every building itself and makes each new worker automatic, rather than all of them for good.
        Assert.DoesNotContain(log, c => c.Command is AutomateAllCommand);
        foreach (var ai in ais!)
        {
            int p = ai.Player;
            var mine = log.Where(c => c.Command.PlayerId == p).Select(c => c.Command).ToList();
            Assert.Contains(mine, c => c is AutomateCommand);
            Assert.Contains(mine, c => c is DefaultPriorityCommand);
            var techs = mine.OfType<ResearchCommand>().Select(c => c.Tech).ToList();
            Assert.Equal(new[] { "farming", "tools" }, techs.Take(2));
            foreach (string kind in new[] { "house", "farm", "barracks", "storehouse" })
                Assert.Contains(mine, c => c is BuildCommand b && b.Building == kind);

            // Stone lies under the soil: it digs a quarry, and builds nothing in the pit.
            Assert.Contains(mine, c => c is DigAreaCommand);
            Assert.True(ai.QuarryArea is not null, "a quarry");
            var (x1, z1, x2, z2, _) = ai.QuarryArea!.Value;
            Assert.DoesNotContain(last!.Buildings.All, b => b.Owner == p && b.X <= x2 && b.MaxX >= x1 && b.Z <= z2 && b.MaxZ >= z1);
            Assert.True(last.Economy.Player(p).Gathered[(int)Good.Stone] > 0, "stone from the quarry");

            // Nothing goes up where its soldiers gather, beside the town hall: a house built among them would push them aside.
            var (rx, rz) = ai.GatheringGround(last)!.Value;
            var onIt = last.Buildings.All.Where(b => b.Owner == p && b.Def.Kind != BuildingKind.TownHall && b.X <= rx + AiPlayer.RallyClear
                && b.MaxX >= rx - AiPlayer.RallyClear && b.Z <= rz + AiPlayer.RallyClear && b.MaxZ >= rz - AiPlayer.RallyClear).ToList();
            Assert.True(onIt.Count == 0, $"player {p} gathers at {rx},{rz} and built a {onIt.FirstOrDefault()?.Def.Id} at {onIt.FirstOrDefault()?.X},{onIt.FirstOrDefault()?.Z}");

            int workers = Enumerable.Range(0, last.Units.Count).Count(i => last.Units.Owner[i] == p && last.Content.Units[last.Units.Def[i]].Role == UnitRole.Worker);
            Assert.True(workers >= 20, $"player {p} has {workers} workers after ten minutes");
            Assert.Contains(ai.Journal, j => j.Text == "researches Farming");
            Assert.Contains(ai.Journal, j => j.Text.StartsWith("marks out a quarry"));
        }
    }

    [Fact]
    public void HardSendsWorkersATreeHasNoRoomForOnToTheNextTrees()
    {
        // A lone tree 40 columns east of its town hall and a grove 70 columns east, with nothing to build or
        // train with. The lone tree has work for some of its six workers; the rest come back with nothing to
        // do. Hard does not send those at that tree again while it stands: they go on to the grove.
        var sim = WithStarts(160, (30, 80));
        TestWorlds.Tree(sim.World, 70, 80, 4, Ground);
        for (int k = 0; k < 8; k++) TestWorlds.Tree(sim.World, 100 + 3 * (k % 4), 77 + 6 * (k / 4), 4, Ground);
        var hall = sim.Economy.TownHall(0)!;
        Array.Clear(hall.Stock);
        var workers = new List<int>();
        for (int k = 0; k < 6; k++) workers.Add(sim.SpawnUnit(0, "worker", hall.X + k, hall.MaxZ + 2));
        var players = new[] { new AiPlayer(0, 1, AiLevel.Hard) };

        int Cutting(Func<int, bool> where) => workers.Count(id =>
        {
            int i = sim.Units.IndexOf(id);
            return sim.Units.Job[i] == UnitJob.Gather && sim.Units.JobBlock[i] == BlockType.Wood && where(sim.Units.JobX[i]);
        });
        int Idle() => workers.Count(id => sim.Units.Job[sim.Units.IndexOf(id)] == UnitJob.None);
        Assert.True(Play(sim, players, () => Idle() > 0 && Cutting(x => x == 70) > 0, AiPlayer.AssignTicks),
            "the lone tree to have work for some of them and none for the rest");
        int left = Idle();
        Assert.True(Play(sim, players, () => Cutting(x => x >= 100) == left, AiPlayer.AssignTicks + 2 * Tps),
            $"the {left} it had no work for to be sent on to the grove; {Cutting(x => x >= 100)} are");
        Assert.Equal(workers.Count - left, Cutting(x => x == 70)); // the others are still at the lone tree,
        Assert.True(BlockInfo.IsTrunk(sim.World.Get(70, Ground + 1, 80))); // which still stands
    }

    [Fact]
    public void WhileHardIsShortOfStoneForSomethingItKeepsOnlyTheStoneInHand()
    {
        // Better tools is next (40 wood and 20 stone), and it has wood for that or for fields, not both.
        // Without the stone it does not sit on the wood: the fields go up. With the stone in hand it keeps
        // the wood for the research too, and the fields wait.
        foreach (bool hasStone in new[] { false, true })
        {
            var sim = WithStarts(96, (40, 48), (40, 20));
            var hall = sim.Economy.TownHall(1)!; // the Orcs: no wall to save for
            sim.Economy.Grant(1, sim.Content.Tech("farming"));
            for (int k = 0; k < 6; k++) sim.SpawnUnit(1, "worker", hall.X + k, hall.MaxZ + 2);
            Array.Clear(hall.Stock);
            hall.Stock[(int)Good.Wood] = 35;
            hall.Stock[(int)Good.Stone] = hasStone ? 20 : 0;
            var log = new List<Command>();
            Play(sim, new[] { new AiPlayer(1, 1, AiLevel.Hard) }, () => false, 3 * Tps, log);
            Assert.Empty(log.OfType<ResearchCommand>());
            Assert.Equal(!hasStone, log.OfType<BuildCommand>().Any(b => b.Building == "farm"));
        }
    }

    // --- Hard: how it plays each faction -------------------------------------------------------------------

    /// <summary>The game's data with how the computer plays the Settlers and the Orcs (see AiStyle) as given.</summary>
    private static Content.ContentDb WithStyles(string settlers, string orcs)
    {
        var files = new Dictionary<string, string>();
        foreach (var f in Content.ContentLoader.Files) files[f] = Content.ContentLoader.EmbeddedText(f)!;
        string text = files["factions.json"];
        Assert.Contains("\"aiStyle\": \"balanced\"", text);
        Assert.Contains("\"aiStyle\": \"waves\"", text);
        files["factions.json"] = text.Replace("\"aiStyle\": \"balanced\"", "\"aiStyle\": \"@settlers\"").Replace("\"aiStyle\": \"waves\"", "\"aiStyle\": \"@orcs\"")
            .Replace("@settlers", settlers).Replace("@orcs", orcs);
        return Content.ContentLoader.Load(files);
    }

    /// <summary>
    /// A flat world with two towns far apart under fog, and for the player a barracks, twenty workers and
    /// goods enough to wall the town and build more besides (no trees: what it has is all the wood there is).
    /// </summary>
    private static (Simulation Sim, Building Hall) ATownToWall(int player, Content.ContentDb? content = null, int x = 60)
    {
        var sim = new Simulation(new RegionMap(1, TestWorlds.Flat(192, 16, 192, Ground)), content);
        sim.Map.Starts.Add(new StartArea(0, player == 0 ? x : 132, Ground + 1, 96));
        sim.Map.Starts.Add(new StartArea(1, player == 1 ? x : 132, Ground + 1, 96));
        sim.PlaceStartingBuildings();
        sim.Fog = true;
        var hall = sim.Economy.TownHall(player)!;
        sim.Economy.PlaceFinished(player, sim.Content.Building("barracks"), hall.X - 9, hall.Z - 9);
        for (int k = 0; k < 20; k++) sim.SpawnUnit(player, "worker", hall.X + k % 7, hall.MaxZ + 2 + k / 7);
        hall.Stock[(int)Good.Food] = 600;
        hall.Stock[(int)Good.Wood] = 2000;
        hall.Stock[(int)Good.Stone] = 1000;
        return (sim, hall);
    }

    [Fact]
    public void HardWallsATownItPlaysBalancedAFewColumnsAtATimeAndKeepsTheLineClear()
    {
        var (sim, hall) = ATownToWall(0); // the Settlers
        // Test mode only to take a column of the wall away, further down, and so that the other town cannot be
        // razed: the match has to last until the wall is stone.
        sim.TestMode = true;
        sim.Schedule(0, new SandboxCheatCommand(0, 1, SandboxCheat.Invulnerable, true));
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        var log = new List<Command>();
        Play(sim, players, () => false, 1, log);

        // The line is marked out at once, before anything is built: 16 columns out from the town hall, 20
        // on the side of its door, with a gate in the north and south sides and two in the west and east.
        Assert.Equal((hall.X - AiPlayer.KeepGap, hall.Z - AiPlayer.KeepGap, hall.MaxX + AiPlayer.KeepGap, hall.MaxZ + AiPlayer.KeepYard), ai.TownWall);
        var (x0, z0, x1, z1) = ai.TownWall!.Value;
        Assert.Equal(6, ai.TownGates.Count);
        Assert.Equal((1, 1), (ai.TownGates.Count(g => g.AlongX && g.Z == z0), ai.TownGates.Count(g => g.AlongX && g.Z == z1)));
        Assert.Equal((2, 2), (ai.TownGates.Count(g => !g.AlongX && g.X == x0), ai.TownGates.Count(g => !g.AlongX && g.X == x1)));
        Assert.Contains(ai.Journal, j => j.Text.StartsWith("marks out a wall round its town hall"));
        var line = new List<(int X, int Z)>();
        for (int x = x0; x <= x1; x++) line.AddRange(new[] { (x, z0), (x, z1) });
        for (int z = z0 + 1; z < z1; z++) line.AddRange(new[] { (x0, z), (x1, z) });
        bool InGate(int x, int z) => ai.TownGates.Any(g => g.AlongX ? z == g.Z && Math.Abs(x - g.X) <= AiPlayer.KeepGate / 2 : x == g.X && Math.Abs(z - g.Z) <= AiPlayer.KeepGate / 2);

        // It lays the wall a few columns at a time, and its workers build it as they do any site.
        int most = 0;
        bool Whole() => line.All(c => sim.Buildings.AtColumn(c.X, c.Z) is { Owner: 0, State: BuildingState.Active } b && b.Def.IsWall);
        Assert.True(Play(sim, players, () =>
        {
            most = Math.Max(most, sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.IsWall && b.WantsBuilders));
            return Whole();
        }, 8 * 60 * Tps, log), $"the wall to be whole; {line.Count(c => sim.Buildings.AtColumn(c.X, c.Z) == null)} of {line.Count} columns are not laid");
        Assert.InRange(most, 2, AiPlayer.KeepOpenSites);
        Assert.Contains(ai.Journal, j => j.Text.Contains("side of its wall"));
        foreach (var (x, z) in line)
            Assert.Equal(InGate(x, z) ? BuildingKind.Gate : BuildingKind.Wall, sim.Buildings.AtColumn(x, z)!.Def.Kind);
        Assert.Equal(6 * AiPlayer.KeepGate, line.Count(c => InGate(c.X, c.Z)));
        // It is laid as barriers, the cheap first tier: no column is upgraded while one is still missing.
        Assert.All(log.OfType<WallCommand>(), c => Assert.Contains(c.Building, new[] { "barrier", "barrier_gate" }));
        int firstUpgrade = log.FindIndex(c => c is UpgradeCommand);
        Assert.True(firstUpgrade < 0 || firstUpgrade > log.FindLastIndex(c => c is WallCommand), "an upgrade before the line was laid");

        // Meanwhile its town has grown, and nothing stands on the line, right beside it (which would leave a
        // hole: a wall column cannot stand against another building) or in the way through a gate.
        var town = sim.Buildings.All.Where(b => b.Owner == 0 && !b.Def.IsWall && b.Def.Kind != BuildingKind.TownHall).ToList();
        Assert.True(town.Count >= 6, $"{town.Count} buildings");
        foreach (var b in town)
        {
            bool inside = b.X > x0 + 1 && b.MaxX < x1 - 1 && b.Z > z0 + 1 && b.MaxZ < z1 - 1;
            bool outside = b.MaxX < x0 - 1 || b.X > x1 + 1 || b.MaxZ < z0 - 1 || b.Z > z1 + 1;
            Assert.True(inside || outside, $"a {b.Def.Id} at {b.X},{b.Z} is on the line of the wall");
            foreach (var g in ai.TownGates)
            {
                int wx = g.AlongX ? AiPlayer.KeepGate / 2 : AiPlayer.KeepLane, wz = g.AlongX ? AiPlayer.KeepLane : AiPlayer.KeepGate / 2;
                Assert.False(b.X <= g.X + wx && b.MaxX >= g.X - wx && b.Z <= g.Z + wz && b.MaxZ >= g.Z - wz, $"a {b.Def.Id} at {b.X},{b.Z} is in the way through the gate at {g.X},{g.Z}");
            }
        }
        Assert.Contains(town, b => b.X > x0 && b.MaxX < x1 && b.Z > z0 && b.MaxZ < z1); // some of it within the wall

        // With the line whole it makes it stronger out of what it can spare, a few columns at a time: all of
        // it to wooden walls first, the side toward the enemy (east) before the others, and only then, with
        // masonry, to stone.
        int Tier(int x, int z) => sim.Buildings.AtColumn(x, z) is { Owner: 0, Upgrade: null } b ? b.Def.Tier : -1;
        bool AllOf(int tier) => line.All(c => Tier(c.X, c.Z) == tier);
        void KeepStocked()
        {
            hall.Stock[(int)Good.Wood] = Math.Max(hall.Stock[(int)Good.Wood], 1000);
            hall.Stock[(int)Good.Stone] = Math.Max(hall.Stock[(int)Good.Stone], 1000);
        }
        most = 0;
        bool eastFirst = false, wooden = false;
        Assert.True(Play(sim, players, () =>
        {
            KeepStocked();
            most = Math.Max(most, sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.IsWall && b.WantsBuilders));
            bool barriers = line.Any(c => sim.Buildings.AtColumn(c.X, c.Z)!.Def.Tier == 0);
            bool stone = sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.IsWall && (b.Def.Tier == 2 || b.Upgrade?.Tier == 2));
            Assert.False(barriers && stone, "a column is being made stone while another is still a barrier");
            if (!eastFirst && line.Where(c => c.X == x1 && c.Z > z0 && c.Z < z1).All(c => Tier(c.X, c.Z) == 1))
            {
                eastFirst = true;
                Assert.Contains(line, c => c.X == x0 && Tier(c.X, c.Z) == 0); // the west side has not been reached yet
            }
            if (!wooden && AllOf(1))
            {
                wooden = true;
                sim.Economy.Grant(0, sim.Content.Tech("masonry")); // if it has not researched it by now
            }
            return AllOf(2);
        }, 25 * 60 * Tps, log), $"the wall to be stone all round; {line.Count(c => Tier(c.X, c.Z) != 2)} of {line.Count} columns are not: " +
            $"{line.Count(c => Tier(c.X, c.Z) == 0)} barriers, {line.Count(c => Tier(c.X, c.Z) == 1)} wooden, {line.Count(c => Tier(c.X, c.Z) < 0)} being upgraded or gone; " +
            string.Join("; ", ai.Journal.TakeLast(6).Select(j => $"{j.Tick / Tps}s {j.Text}")));
        Assert.True(eastFirst && wooden);
        Assert.InRange(most, 2, AiPlayer.KeepOpenSites);
        Assert.All(log.OfType<UpgradeCommand>(), c => Assert.False(c.Stretch));
        Assert.Contains(ai.Journal, j => j.Text.Contains("columns of its wall to wooden wall"));
        Assert.Contains(ai.Journal, j => j.Text.Contains("columns of its wall to stone wall"));
        Assert.Equal(sim.Content.Building("stone_gate"), sim.Buildings.AtColumn(ai.TownGates[0].X, ai.TownGates[0].Z)!.Def);

        // A column knocked out is laid again the next time it looks at its wall (as a barrier, to be upgraded in its turn).
        var (kx, kz) = (x0, z0 + 5);
        log.Clear();
        sim.Schedule(sim.Tick, new SandboxClearCommand(0, kx, kz, 0));
        sim.Step();
        Assert.Null(sim.Buildings.AtColumn(kx, kz));
        Assert.True(Play(sim, players, () => log.OfType<WallCommand>().Any(), AiPlayer.KeepLookTicks + 2 * Tps, log), "the column to be laid again");
        var again = Assert.Single(log.OfType<WallCommand>());
        Assert.Equal(("barrier", kx, kz, kx, kz), (again.Building, again.X1, again.Z1, again.X2, again.Z2));
        Assert.True(Play(sim, players, () =>
        {
            KeepStocked();
            return AllOf(2);
        }, 3 * 60 * Tps, log), "the new column to be made stone like the rest");
    }

    /// <summary>
    /// Puts a finished wall of the tier (wooden or stone) on the line Hard marks out round the town hall (see
    /// <see cref="AiPlayer.KeepGap"/>), gates and all, before it has had its first look at its town.
    /// </summary>
    private static void FinishWall(Simulation sim, Building hall, string tier)
    {
        int x0 = hall.X - AiPlayer.KeepGap, z0 = hall.Z - AiPlayer.KeepGap, x1 = hall.MaxX + AiPlayer.KeepGap, z1 = hall.MaxZ + AiPlayer.KeepYard;
        var (hx, hz) = Middle(hall);
        int yard = (hall.MaxZ + 1 + z1) / 2, h = AiPlayer.KeepGate / 2;
        bool InGate(int x, int z) => ((z == z0 || z == z1) && Math.Abs(x - hx) <= h) || ((x == x0 || x == x1) && (Math.Abs(z - hz) <= h || Math.Abs(z - yard) <= h));
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
            if (x == x0 || x == x1 || z == z0 || z == z1)
                Assert.NotNull(sim.Economy.PlaceFinished(hall.Owner, sim.Content.Building(tier + (InGate(x, z) ? "_gate" : "_wall")), x, z));
    }

    /// <summary>The wall <see cref="FinishWall"/> put up is on the line Hard marked out, its gates where Hard wants them.</summary>
    private static void AssertWalled(Simulation sim, AiPlayer ai)
    {
        var (x0, z0, x1, z1) = ai.TownWall!.Value;
        Assert.All(new[] { (x0, z0), (x1, z1), (x0, z1), (x1, z0) }, c => Assert.Equal(BuildingKind.Wall, sim.Buildings.AtColumn(c.Item1, c.Item2)?.Def.Kind));
        Assert.All(ai.TownGates, g => Assert.Equal(BuildingKind.Gate, sim.Buildings.AtColumn(g.X, g.Z)?.Def.Kind));
        Assert.Equal(6 * AiPlayer.KeepGate, sim.Buildings.All.Count(b => b.Owner == ai.Player && b.Def.Kind == BuildingKind.Gate));
    }

    [Fact]
    public void HardPostsItsArchersOnItsWallByTheGateTowardTheEnemyAndTheyStayUpThereWhenTheEnemyComes()
    {
        var (sim, hall) = ATownToWall(0);
        var ai = new AiPlayer(0, 1, AiLevel.Hard);
        var players = new[] { ai };
        FinishWall(sim, hall, "wooden");
        Play(sim, players, () => false, 1);
        AssertWalled(sim, ai);
        var (x0, z0, x1, z1) = ai.TownWall!.Value;
        var u = sim.Units;
        // Too few to send as a wave. (The spearmen come first: one of them goes off to explore.)
        var spears = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "spearman", hall.MaxX + 3 + k % 3, hall.MaxZ + 6 + k / 3)).ToArray();
        var archers = Enumerable.Range(0, 6).Select(k => sim.SpawnUnit(0, "archer", hall.MaxX + 3 + k % 3, hall.MaxZ + 3 + k / 3)).ToArray();
        bool OnWall(int id) => sim.Economy.WallUnder(u.IndexOf(id)) != null;

        // The enemy is to the east: the archers go up by a gate in the east side; the spearmen stay on the ground.
        Assert.True(Play(sim, players, () => archers.All(OnWall), 90 * Tps), $"the archers to take their posts; {archers.Count(OnWall)} of 6 have");
        Assert.All(archers, id => Assert.Equal(x1, u.X[u.IndexOf(id)].FloorToInt()));
        Assert.All(archers, id => Assert.Contains(ai.TownGates, g => !g.AlongX && g.X == x1 && Math.Abs(g.Z - u.Z[u.IndexOf(id)].FloorToInt()) <= EconomySystem.PostReach));
        Assert.DoesNotContain(spears, OnWall);
        // They are not called back to where the others gather.
        Play(sim, players, () => false, 60 * Tps);
        Assert.All(archers, id => Assert.True(OnWall(id)));

        // Enemies come at the wall below them: the spearmen go out to meet them, the archers shoot from where they stand.
        int az = u.Z[u.IndexOf(archers[0])].FloorToInt();
        var raiders = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(1, "spearman", x1 + 8, az - 2 + k)).ToArray();
        Assert.True(Play(sim, players, () => raiders.All(id => !u.Exists(id)), 120 * Tps), "the raiders to be beaten");
        Assert.Contains(ai.Journal, j => j.Text.StartsWith("defends its town"));
        Assert.All(archers.Where(u.Exists), id => Assert.True(OnWall(id), "an archer came down to fight"));
        Assert.True(archers.Count(u.Exists) >= 5);
    }

    [Fact]
    public void AWaveLeavesAQuarterOfTheSoldiersAtHomeForABalancedFactionAndFewerForOneItPlaysInWaves()
    {
        foreach (int player in new[] { 0, 1 })
        {
            // The same army for the Settlers (balanced) and the Orcs (waves): the twenty a player starts with
            // (it takes the enemy to have as many), then forty more. Three quarters of the sixty are enough
            // to attack with: the part it sends is what has to be half again as strong as the enemy's.
            var sim = WithStarts(192, (20, 96), (170, 96));
            sim.Fog = true;
            var hall = sim.Economy.TownHall(player)!;
            sim.SpawnUnit(player, "rider", hall.X + 7, hall.MaxZ + 5);
            for (int k = 0; k < 19; k++) sim.SpawnUnit(player, "spearman", hall.X - 7 + k % 10, hall.MaxZ + 7 + k / 10);
            var ai = new AiPlayer(player, 1, AiLevel.Hard);
            var players = new[] { ai };
            Play(sim, players, () => false, 1); // it takes stock of what it starts with
            for (int k = 0; k < 40; k++) sim.SpawnUnit(player, "spearman", hall.X - 7 + k % 10, hall.MaxZ + 11 + k / 10);
            Assert.True(Play(sim, players, () => ai.Waves == 1, 4 * 60 * Tps), $"player {player}'s wave to set out");
            int left = 60 - ai.Wave.Count;
            int share = player == 0 ? AiPlayer.KeepGuardPercent : AiPlayer.HardGuardPercent;
            Assert.InRange(left, 60 * share / 100 - 1, 60 * share / 100 + 1); // 15 of the Settlers', 9 of the Orcs', give or take the rider
        }
        Assert.True(AiPlayer.KeepGuardPercent > AiPlayer.HardGuardPercent);
    }

    [Fact]
    public void AWalledTownDoesNotSendItsSoldiersAfterRaidersAtAnOutpost()
    {
        // One enemy spearman by a storehouse sixty columns from the town hall, forty outside the line of the
        // wall. The Orcs, with no wall, send a party of three. The Settlers send nobody: their soldiers would
        // leave through the gates a few at a time for a fight far from home. They meet the enemy at the town.
        foreach (int player in new[] { 0, 1 })
        {
            var sim = WithStarts(160, player == 0 ? (40, 80) : (140, 20), player == 0 ? (140, 20) : (40, 80));
            sim.Fog = true;
            var hall = sim.Economy.TownHall(player)!;
            for (int k = 0; k < 12; k++) sim.SpawnUnit(player, "spearman", hall.X + k % 6, hall.MaxZ + 4 + k / 6);
            sim.Economy.PlaceFinished(player, sim.Content.Building("storehouse"), 100, 78);
            int raider = sim.SpawnUnit(1 - player, "spearman", 106, 86);
            sim.Schedule(0, new StanceCommand(1 - player, new[] { raider }, UnitStance.Hold));
            var ai = new AiPlayer(player, 1, AiLevel.Hard);
            var log = new List<Command>();
            Play(sim, new[] { ai }, () => false, 3 * Tps, log);
            var sent = log.Where(c => c is FormationMoveCommand or AttackMoveCommand).Cast<UnitsCommand>().SelectMany(c => c.Units).Distinct().Count();
            Assert.Equal(player == 0 ? 0 : 3, sent);
            // The storehouse (its middle is at 102) is more than 24 columns outside the line. Nearer than that
            // the Settlers send their party too: see HardMeetsRaidersAtAFarBuildingWithAPartyAndAnAttackOnItsTownWithEveryone.
            if (player == 0) Assert.True(102 > ai.TownWall!.Value.X1 + AiPlayer.KeepReach);
            else Assert.Null(ai.TownWall);
        }
    }

    [Fact]
    public void HardBuildsNoWallForAFactionItPlaysInWaves()
    {
        // The Orcs' town with all it takes to start a wall: none is marked out and none is laid.
        var (sim, _) = ATownToWall(1);
        var ai = new AiPlayer(1, 1, AiLevel.Hard);
        var log = new List<Command>();
        Play(sim, new[] { ai }, () => false, 30 * Tps, log);
        Assert.Null(ai.TownWall);
        Assert.Empty(ai.TownGates);
        Assert.Empty(log.OfType<WallCommand>());
        Assert.NotEmpty(log.OfType<BuildCommand>()); // it is building its town all the same

        // It goes by how the data says the faction is played, not by which faction it is: with the two the
        // other way round the Orcs wall their town and the Settlers do not.
        var swapped = WithStyles(settlers: "waves", orcs: "balanced");
        foreach (int player in new[] { 0, 1 })
        {
            (sim, _) = ATownToWall(player, swapped);
            ai = new AiPlayer(player, 1, AiLevel.Hard);
            log.Clear();
            Play(sim, new[] { ai }, () => false, 30 * Tps, log);
            Assert.Equal(player == 1, ai.TownWall != null);
            Assert.Equal(player == 1, log.OfType<WallCommand>().Any());
        }

        // And a town too near the edge of the map for the line to fit gets none.
        (sim, _) = ATownToWall(0, x: 20);
        ai = new AiPlayer(0, 1, AiLevel.Hard);
        log.Clear();
        Play(sim, new[] { ai }, () => false, 30 * Tps, log);
        Assert.Null(ai.TownWall);
        Assert.Empty(log.OfType<WallCommand>());
    }

    [Fact]
    public void WhileOnlyItsWallWantsStoneHardKeepsAFewWorkersAtItsQuarry()
    {
        // Thirty-six workers, every tech, a barracks and no stone. A town with no wall keeps five workers
        // on stone; one that wants the stone only to turn its wooden wall to stone keeps three, and the wall
        // is upgraded as the stone comes in.
        foreach (int player in new[] { 0, 1 })
        {
            var (sim, hall) = ATownToWall(player);
            foreach (var tech in sim.Content.Techs) sim.Economy.Grant(player, tech);
            for (int k = 0; k < 16; k++) sim.SpawnUnit(player, "worker", hall.X + k % 7, hall.MaxZ + 5 + k / 7);
            Array.Clear(hall.Stock);
            hall.Stock[(int)Good.Food] = 300;
            var ai = new AiPlayer(player, 1, AiLevel.Hard);
            if (player == 0) FinishWall(sim, hall, "wooden");
            int most = 0;
            Play(sim, new[] { ai }, () =>
            {
                most = Math.Max(most, ai.Masons.Count);
                return false;
            }, 5 * AiPlayer.AssignTicks);
            Assert.Equal(player == 0 ? AiPlayer.KeepMasons : AiPlayer.MostMasons, most);
            if (player == 0) AssertWalled(sim, ai);
        }
    }

    [Fact]
    public void AHardMatchWithFogReplaysTheSame()
    {
        var result = AiMatch.Play(5, 256, 64, 256, 6 * 60 * Tps, levels: new[] { AiLevel.Hard }, fog: true);
        var text = CommandLog.Parse(result.Log.ToString());
        var first = MatchReplay.Run(text, 600, out var sim1);
        var second = MatchReplay.Run(text, 600, out var sim2);
        Assert.Equal(-1, MatchReplay.FirstMismatch(first, second));
        Assert.Equal(sim1.StateHash(), sim2.StateHash());
        Assert.Equal(result.Winner, sim1.Match.Winner);
    }
}
