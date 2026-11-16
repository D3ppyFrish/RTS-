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
        Assert.Equal(moves[0].X - AiPlayer.LookBack, moves[1].X); // further back
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
        // One enemy spearman by a storehouse sixty columns from the town hall: three of the twelve go.
        var sim = WithStarts(160, (40, 80));
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 12; k++) sim.SpawnUnit(0, "spearman", hall.X + k % 6, hall.MaxZ + 4 + k / 6);
        sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 100, 78);
        int raider = sim.SpawnUnit(1, "spearman", 106, 86);
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
            for (int t = 0; t < 61 * Tps; t++) sim.Step(); // the first minute passes, and its upkeep is paid
            Assert.False(sim.Economy.Starving(0));
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
    public void HardRunsItsTownItselfAndNeverStarvesIt()
    {
        // Two Hard players on a generated map for ten minutes.
        Simulation? last = null;
        IReadOnlyList<AiPlayer>? ais = null;
        var starving = new List<long>();
        var result = AiMatch.Play(3, 256, 64, 256, 10 * 60 * Tps, levels: new[] { AiLevel.Hard }, fog: true, watch: (sim, players) =>
        {
            (last, ais) = (sim, players);
            foreach (var ai in players)
                if (sim.Economy.Starving(ai.Player)) starving.Add(sim.Tick);
        });
        Assert.NotNull(last);
        Assert.Empty(starving); // the food for each payment is put by in time
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
