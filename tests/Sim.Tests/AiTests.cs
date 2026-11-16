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

/// <summary>Computer players (<see cref="AiPlayer"/>) and matches between them (<see cref="AiMatch"/>).</summary>
public class AiTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    /// <summary>A flat world with a start area (and so a town hall with the starting goods) for each column given.</summary>
    private static Simulation WithStarts(int size, params (int X, int Z)[] starts)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    /// <summary>Steps the match with the computer players thinking before every tick, until done or maxTicks.</summary>
    private static bool Play(Simulation sim, IReadOnlyList<AiPlayer> players, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            foreach (var ai in players) ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        return done();
    }

    [Fact]
    public void AComputerMatchReplaysTheSameFromItsCommandLog()
    {
        var result = AiMatch.Play(3, 256, 64, 256, 5 * 60 * Tps);
        var log = result.Log;
        Assert.Equal(5 * 60 * Tps, log.Ticks); // nobody has won yet in five minutes
        Assert.Equal(-1, result.Winner);
        Assert.True(log.StartBuildings && log.StartLairs && log.StartCities);
        // Both players play: they make their workers automatic, research, train, build and fight.
        foreach (var type in new[] { typeof(AutomateAllCommand), typeof(ResearchCommand), typeof(TrainCommand),
                     typeof(AttackMoveCommand), typeof(BuildCommand) })
            Assert.Contains(log.Commands, c => c.Command.GetType() == type);
        Assert.Equal(2, log.Commands.Where(c => c.Command is AutomateAllCommand).Select(c => c.Command.PlayerId).Distinct().Count());

        // The log is the whole match: replaying it (through its text form) gives the same game every time.
        var text = CommandLog.Parse(log.ToString());
        var first = MatchReplay.Run(text, 600, out var sim1);
        var second = MatchReplay.Run(text, 600, out var sim2);
        Assert.Equal(-1, MatchReplay.FirstMismatch(first, second));
        Assert.Equal(log.Ticks, first[^1].Tick);
        Assert.Equal(sim1.StateHash(), sim2.StateHash());
        Assert.Equal(result.Winner, sim1.Match.Winner);
    }

    [Fact]
    public void TheComputerFinishesOffAnEnemyWithNoSoldiersAndWins()
    {
        var sim = WithStarts(96, (20, 48), (76, 48));
        var army = new List<int>();
        for (int k = 0; k < 8; k++) army.Add(sim.SpawnUnit(0, "spearman", 14 + k, 56));
        var players = new[] { new AiPlayer(0, 1), new AiPlayer(1, 1) };
        var hall1 = sim.Economy.TownHall(1)!;

        // No wave before the first wave's time, then a small one: player 1 has no soldiers at all.
        Assert.False(Play(sim, players, () => players[0].Waves > 0, AiPlayer.FirstWaveTicks - 1));
        Assert.True(Play(sim, players, () => players[0].Waves > 0, 30 * Tps));
        Assert.Equal(hall1.Id, players[0].WaveTarget);
        Assert.InRange(players[0].Wave.Count, AiPlayer.LeastWave, army.Count - AiPlayer.LeastGuard);
        Assert.Equal(0, players[1].Waves);

        Assert.True(Play(sim, players, () => sim.Match.Over, 8 * 60 * Tps), "the town hall to fall");
        Assert.Equal(0, sim.Match.Winner);
        Assert.Null(sim.Buildings.Get(hall1.Id));
    }

    [Fact]
    public void SoldiersStrandedOnTheWayLeaveTheWaveAndTheRestBesiegeTheTarget()
    {
        // Half the wave ends up on a pillar far behind that nobody can climb down from: the middle of the
        // wave never gets near the target while they count, so after a minute without getting nearer they
        // leave the wave, and the rest bring the town hall down.
        var sim = WithStarts(96, (20, 48), (76, 48));
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 14 + k, 56);
        var players = new[] { new AiPlayer(0, 1), new AiPlayer(1, 1) };
        Assert.True(Play(sim, players, () => players[0].Waves > 0, AiPlayer.FirstWaveTicks + 30 * Tps));
        var stranded = players[0].Wave.Take(players[0].Wave.Count / 2).ToList();
        TestWorlds.Box(sim.World, 2, Ground + 1, 2, 4, Ground + 6, 4, BlockType.StoneBrick);
        foreach (int id in stranded)
        {
            int i = sim.Units.IndexOf(id);
            (sim.Units.X[i], sim.Units.Z[i], sim.Units.Y[i]) = (Fix64.FromInt(3) + Fix64.Half, Fix64.FromInt(3) + Fix64.Half, Ground + 7);
        }
        Assert.True(Play(sim, players, () => stranded.All(id => !players[0].Wave.Contains(id)), 3 * 60 * Tps), "the stranded to leave the wave");
        Assert.True(Play(sim, players, () => sim.Match.Over, 8 * 60 * Tps), "the town hall to fall");
        Assert.Equal(0, sim.Match.Winner);
    }

    /// <summary>
    /// Player 0, a computer player, against player 1, who gives no orders: eight spearmen against player 1's
    /// one soldier, a spearman holding its ground at (x, z) from the door of its town hall.
    /// </summary>
    private static (Simulation Sim, AiPlayer[] Players, Building Hall, int Guard) AgainstOneDefender(int dx, int dz, string defender = "spearman")
    {
        var sim = WithStarts(96, (20, 48), (76, 48));
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 14 + k, 56);
        var hall = sim.Economy.TownHall(1)!;
        Assert.True(sim.Economy.AccessSpot(hall, out int x, out int z, out _));
        int guard = sim.SpawnUnit(1, defender, x + dx, z + dz);
        Assert.True(guard > 0);
        sim.Schedule(0, new StanceCommand(1, new[] { guard }, UnitStance.Hold));
        return (sim, new[] { new AiPlayer(0, 1) }, hall, guard);
    }

    [Fact]
    public void AWaveGoesForADefenderThatStaysOutOfSight()
    {
        // Issue #37: the defender holds its ground 15 columns from the door, further than the wave can see
        // from there, so the wave stood at the door waiting for a fight that never came.
        var (sim, players, hall, guard) = AgainstOneDefender(9, 12);
        Assert.True(Play(sim, players, () => !sim.Units.Exists(guard), AiPlayer.FirstWaveTicks + 3 * 60 * Tps), "the defender to fall");
        Assert.NotNull(sim.Buildings.Get(hall.Id)); // the defender first
        Assert.True(Play(sim, players, () => sim.Match.Over, 4 * 60 * Tps), "the town hall to fall");
        Assert.Equal(0, sim.Match.Winner);
        Assert.Null(sim.Buildings.Get(hall.Id));
    }

    [Fact]
    public void AWaveBringsTheTargetDownWhenItCannotGetAtTheDefender()
    {
        // The defender, an archer, stands on a pillar six blocks high that nobody can climb: the wave can't
        // get at it, so after a while it stops trying and brings the town hall down anyway.
        var (sim, players, hall, guard) = AgainstOneDefender(9, 12, "archer");
        int g = sim.Units.IndexOf(guard);
        int px = sim.Units.X[g].FloorToInt(), pz = sim.Units.Z[g].FloorToInt();
        TestWorlds.Box(sim.World, px - 1, Ground + 1, pz - 1, px + 1, Ground + 6, pz + 1, BlockType.StoneBrick);
        sim.Units.Y[g] = Ground + 7;
        Assert.True(Play(sim, players, () => sim.Match.Over, AiPlayer.FirstWaveTicks + 8 * 60 * Tps), "the town hall to fall");
        Assert.Equal(0, sim.Match.Winner);
        Assert.Null(sim.Buildings.Get(hall.Id));
    }

    [Fact]
    public void AnEvenlyMatchedEnemyIsNotAttackedWithASmallWave()
    {
        var sim = WithStarts(96, (20, 48), (76, 48));
        for (int k = 0; k < 8; k++)
        {
            sim.SpawnUnit(0, "spearman", 14 + k, 56);
            sim.SpawnUnit(1, "spearman", 70 + k, 56);
        }
        var players = new[] { new AiPlayer(0, 1), new AiPlayer(1, 1) };
        Play(sim, players, () => false, AiPlayer.FirstWaveTicks + 60 * Tps);
        Assert.Equal(0, players[0].Waves);
        Assert.Equal(0, players[1].Waves);
        Assert.False(sim.Match.Over);
    }

    [Fact]
    public void WhenWoodIsShortTheComputerSendsWorkersToTreesFarFromItsTownHall()
    {
        // Trees only 80 columns away: automatic workers look for trees nearer than that, so they find none.
        var sim = WithStarts(128, (20, 64));
        for (int k = 0; k < 6; k++) TestWorlds.Tree(sim.World, 100, 58 + 2 * k, 4, Ground);
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Wood] = 0;
        var workers = new List<int>();
        for (int k = 0; k < 4; k++) workers.Add(sim.SpawnUnit(0, "worker", 18 + k, hall.MaxZ + 2));
        var players = new[] { new AiPlayer(0, 1) };

        bool Cutting(int n) => workers.Count(id =>
        {
            int i = sim.Units.IndexOf(id);
            return sim.Units.Job[i] == UnitJob.Gather && sim.Units.JobBlock[i] == BlockType.Wood && sim.Units.JobX[i] == 100;
        }) >= n;
        Assert.True(Play(sim, players, () => Cutting(AiPlayer.MovedAtOnce), 10 * Tps), "the first workers to be sent to the trees");
        Assert.False(Cutting(AiPlayer.MovedAtOnce + 1)); // a few at a time
        Assert.True(Play(sim, players, () => Cutting(AiPlayer.LeastWoodWorkers), AiPlayer.WoodTicks + 10 * Tps), "more to follow");
        Assert.True(Play(sim, players, () => sim.Economy.Player(0).Gathered[(int)Good.Wood] > 0, 3 * 60 * Tps), "wood to come in");
    }

    [Fact]
    public void WhenFoodRunsShortTheComputerKeepsWoodForAFarm()
    {
        // No food and no berries: the computer keeps wood for a farm (which automation lays out) instead of
        // spending it on research.
        var sim = WithStarts(96, (30, 48));
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Food] = 0;
        hall.Stock[(int)Good.Wood] = 50;
        sim.Economy.Grant(0, sim.Content.Tech("farming"));
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "worker", 28 + k, hall.MaxZ + 2);
        var players = new[] { new AiPlayer(0, 1) };
        Assert.True(Play(sim, players, () => sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Farm), 20 * Tps),
            "a farm to be laid out");
        Assert.Null(sim.Economy.Researching(0)); // tools (40 wood) waits: the farm came first
    }

    // --- The smart levels (issue #47) ------------------------------------------------------------------

    /// <summary>Steps the match like <see cref="Play"/>, keeping every command the computer players give.</summary>
    private static bool PlayLogged(Simulation sim, IReadOnlyList<AiPlayer> players, List<Command> log, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            foreach (var ai in players) ai.Think(sim, c => { log.Add(c); sim.Schedule(sim.Tick, c); });
            sim.Step();
        }
        return done();
    }

    /// <summary>
    /// The smart computer goes for the goblins' totem (issue #67): with soldiers to spare it sends a party to
    /// break it, holds the town after, and hires the goblins' mercenaries.
    /// </summary>
    [Fact]
    public void TheSmartComputerBreaksTheGoblinsTotemAndHiresTheirBlades()
    {
        var sim = WithStarts(160, (24, 80));
        var town = sim.Cities.Place(sim.Content.CityState("goblins"), 110, 80)!;
        var hall = sim.Economy.TownHall(0)!;
        hall.Stock[(int)Good.Food] = 2000;
        hall.Stock[(int)Good.Iron] = 200;
        for (int k = 0; k < 14; k++) sim.SpawnUnit(0, "spearman", 20 + k % 7, hall.MaxZ + 2 + k / 7);
        // Room for a mercenary however many of the spearmen come back (bleeding decides that, issue #72).
        Assert.NotNull(sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 10, hall.Z));
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var log = new List<Command>();
        Assert.True(PlayLogged(sim, new[] { ai }, log, () => ai.TotemPartyIds.Count > 0, AiPlayer.TotemTicks + 10 * Tps), "a party to set out");
        Assert.Equal(AiPlayer.TotemParty, ai.TotemPartyIds.Count);
        Assert.Contains(log, c => c is AttackMoveCommand m && m.X == town.X);
        // The first party usually falls to the guards; the computer sends another, and that one breaks it.
        Assert.True(PlayLogged(sim, new[] { ai }, log, () => town.Holder == 0, 3 * 60 * Tps), "the party to break the totem");
        Assert.True(PlayLogged(sim, new[] { ai }, log, () => log.OfType<HireCommand>().Any(), 30 * Tps), "a goblin to be hired");
    }

    [Fact]
    public void ASmartComputerMatchWithFogReplaysTheSame()
    {
        var result = AiMatch.Play(3, 256, 64, 256, 6 * 60 * Tps, levels: new[] { AiLevel.Normal, AiLevel.Easy }, fog: true);
        var log = result.Log;
        Assert.True(log.Fog);
        Assert.Contains(log.Commands, c => c.Command is FormationMoveCommand || c.Command is MoveCommand);
        var text = CommandLog.Parse(log.ToString());
        var first = MatchReplay.Run(text, 600, out var sim1);
        var second = MatchReplay.Run(text, 600, out var sim2);
        Assert.Equal(-1, MatchReplay.FirstMismatch(first, second));
        Assert.Equal(sim1.StateHash(), sim2.StateHash());
        Assert.Equal(result.Winner, sim1.Match.Winner);
    }

    [Fact]
    public void UnderFogTheSmartComputerScoutsForTheEnemyTownHall()
    {
        // 150 columns apart: nothing of player 1 is in sight at the start. The rider scouts the other start
        // area and finds the town hall there; the classic player would have known all along.
        var sim = WithStarts(192, (20, 96), (170, 96));
        sim.Fog = true;
        int rider = sim.SpawnUnit(0, "rider", 24, 104);
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "spearman", 14 + k, 106);
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var players = new[] { ai };
        Play(sim, players, () => false, 2 * Tps);
        Assert.False(ai.KnowsEnemyHall);
        Assert.Equal(rider, ai.Scout); // the fastest goes
        Assert.True(Play(sim, players, () => ai.KnowsEnemyHall, 90 * Tps), "the scout to find the town hall");
        Assert.True(Play(sim, players, () => ai.Scout == 0, 10 * Tps), "the scout to be done");
    }

    [Fact]
    public void UnderFogTheSmartComputerDoesNotAnswerEnemiesItCannotSee()
    {
        // An enemy spearman holds 26 columns behind the town hall, well inside the classic player's defence
        // radius but out of sight of everything player 0 has: the classic player sends its soldiers at it,
        // the smart one does not know it is there.
        foreach (var level in new[] { AiLevel.Classic, AiLevel.Easy })
        {
            var sim = WithStarts(160, (40, 80), (140, 80));
            sim.Fog = true;
            var hall = sim.Economy.TownHall(0)!;
            var (hx, hz) = (hall.X + hall.Def.SizeX / 2, hall.Z + hall.Def.SizeZ / 2);
            for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "spearman", hx - 2 + k, hall.MaxZ + 4);
            int enemy = sim.SpawnUnit(1, "spearman", hx, hz - 26);
            sim.Schedule(0, new StanceCommand(1, new[] { enemy }, UnitStance.Hold));
            var log = new List<Command>();
            PlayLogged(sim, new[] { new AiPlayer(0, 1, level) }, log, () => false, 20 * Tps);
            bool answered = log.Any(c => c is AttackMoveCommand m && m.PlayerId == 0 && Math.Abs(m.Z - (hz - 26)) <= 2);
            Assert.Equal(level == AiLevel.Classic, answered);
        }
    }

    [Fact]
    public void TheSmartComputerTrainsCountersToTheSoldiersItHasSeen()
    {
        // Player 1's archers come in sight of player 0's town hall. Riders beat archers, and archers beat
        // spearmen: riders become by far the likeliest pick at the barracks, spearmen hardly ever. Before it has
        // seen them, the mix is the classic one.
        var sim = WithStarts(128, (30, 64), (110, 64));
        var hall = sim.Economy.TownHall(0)!;
        foreach (var t in new[] { "archery", "horse_breeding", "basic_magic", "sapping" }) sim.Economy.Grant(0, sim.Content.Tech(t));
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var c = sim.Content;
        long W(string unit) => ai.TrainWeight(sim, c.Unit(unit));
        Assert.True(W("spearman") > W("rider") && W("rider") == W("archer"));
        var archers = new List<int>();
        for (int k = 0; k < 8; k++) archers.Add(sim.SpawnUnit(1, "archer", hall.MaxX + 6 + k % 4, hall.Z - 2 - k / 4));
        Play(sim, new[] { ai }, () => false, 3 * Tps);
        Assert.Equal(8, ai.Spotted.Count());
        foreach (var other in new[] { "spearman", "archer", "mage", "sapper" })
            Assert.True(W("rider") >= 3 * W(other), $"rider {W("rider")}, {other} {W(other)}");
        Assert.Equal(1, W("spearman"));
    }

    [Fact]
    public void WoundedSoldiersOfAWaveGoHomeToHealAndComeBack()
    {
        var sim = WithStarts(96, (20, 48), (76, 48));
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 14 + k, 56);
        sim.SpawnUnit(1, "spearman", 70, 56);
        var ai = new AiPlayer(0, 1, AiLevel.Easy);
        var players = new[] { ai };
        Assert.True(Play(sim, players, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 30 * Tps), "a wave to set out");
        Assert.False(Play(sim, players, () => ai.Waves > 0 && sim.Tick < AiPlayer.SmartFirstWaveTicks, 0));
        int hurt = ai.Wave[0];
        int i = sim.Units.IndexOf(hurt);
        sim.Units.Hp[i] = sim.Content.Units[sim.Units.Def[i]].MaxHp / 5;
        Assert.True(Play(sim, players, () => ai.Mending.Contains(hurt), 2 * Tps), "the wounded soldier to go home");
        Assert.DoesNotContain(hurt, ai.Wave);
        var hall = sim.Economy.TownHall(0)!;
        Assert.True(Play(sim, players, () => !ai.Mending.Contains(hurt), 90 * Tps), "it to heal");
        i = sim.Units.IndexOf(hurt);
        Assert.True(i >= 0);
        Assert.True(hall.ColumnDistance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()) <= 12, "it healed by the town hall");
        Assert.True(sim.Units.Hp[i] * 100 >= sim.Content.Units[sim.Units.Def[i]].MaxHp * AiPlayer.HealedPercent);
    }

    [Fact]
    public void FromNormalUpTheComputerFocusesItsArchersOnTheWeakestEnemyInReach()
    {
        // Three enemy spearmen come at the town hall; one is badly hurt. The archers at home all shoot it first.
        var sim = WithStarts(96, (30, 48));
        var hall = sim.Economy.TownHall(0)!;
        var archers = new List<int>();
        for (int k = 0; k < 4; k++) archers.Add(sim.SpawnUnit(0, "archer", hall.X + k, hall.MaxZ + 4));
        var enemies = new List<int>();
        for (int k = 0; k < 3; k++) enemies.Add(sim.SpawnUnit(1, "spearman", hall.X + 2 * k, hall.MaxZ + 11));
        int weak = enemies[2];
        sim.Units.Hp[sim.Units.IndexOf(weak)] = 10;
        var log = new List<Command>();
        foreach (var level in new[] { AiLevel.Normal })
            PlayLogged(sim, new[] { new AiPlayer(0, 1, level) }, log, () => log.OfType<AttackCommand>().Any(), 5 * Tps);
        var focus = log.OfType<AttackCommand>().First();
        Assert.Equal(weak, focus.Target);
        Assert.True(focus.Units.Length >= 3, $"{focus.Units.Length} archers on it");
    }

    [Fact]
    public void ANormalComputerWallsItsTownHallWithGates()
    {
        var sim = WithStarts(96, (40, 48));
        var hall = sim.Economy.TownHall(0)!;
        var log = new List<Command>();
        var ai = new AiPlayer(0, 1, AiLevel.Normal);
        PlayLogged(sim, new[] { ai }, log, () => false, AiPlayer.WallTicks - 5 * Tps);
        Assert.Empty(log.OfType<WallCommand>()); // not before its time
        hall.Stock[(int)Good.Stone] = 1000;
        hall.Stock[(int)Good.Wood] = 200;
        PlayLogged(sim, new[] { ai }, log, () => false, 10 * Tps);
        // Walls and gates on all four sides.
        var walls = log.OfType<WallCommand>().ToList();
        Assert.Contains(walls, w => w.Building == "gate" && w.Z1 > hall.MaxZ);
        Assert.Contains(walls, w => w.Building == "gate" && w.Z1 < hall.Z);
        Assert.Contains(walls, w => w.Building == "wall" && w.X1 == w.X2 && w.X1 < hall.X);
        Assert.Contains(walls, w => w.Building == "wall" && w.X1 == w.X2 && w.X1 > hall.MaxX);
        Assert.True(sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Gate) >= 3, "gates " + sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Gate));
        Assert.True(sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.IsWall) >= 24, "walls " + sim.Buildings.All.Count(b => b.Owner == 0 && b.Def.IsWall) + " stone " + hall.Stock[(int)Good.Stone]);

        // Easy does not wall.
        var normal = WithStarts(96, (40, 48));
        normal.Economy.TownHall(0)!.Stock[(int)Good.Stone] = 1000;
        var normalLog = new List<Command>();
        PlayLogged(normal, new[] { new AiPlayer(0, 1, AiLevel.Easy) }, normalLog, () => false, AiPlayer.WallTicks + 10 * Tps);
        Assert.Empty(normalLog.OfType<WallCommand>());

        // Nor does Normal for a faction the computer plays in waves (the Orcs, player 1): walls are for a
        // faction it plays balanced, as it does the Settlers above.
        var orcs = WithStarts(96, (40, 20), (40, 60));
        Assert.Equal(Content.AiStyle.Waves, orcs.Economy.Faction(1).AiStyle);
        orcs.Economy.TownHall(1)!.Stock[(int)Good.Stone] = 1000;
        orcs.Economy.TownHall(1)!.Stock[(int)Good.Wood] = 200;
        var orcLog = new List<Command>();
        PlayLogged(orcs, new[] { new AiPlayer(1, 1, AiLevel.Normal) }, orcLog, () => false, AiPlayer.WallTicks + 10 * Tps);
        Assert.Empty(orcLog.OfType<WallCommand>());
    }
}
