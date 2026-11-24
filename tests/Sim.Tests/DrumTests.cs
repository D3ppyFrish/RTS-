using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #151: War drums trains the war drummer, who rallies broken orcs and beats the charge.</summary>
public class DrumTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Orcs = 1; // player 1 plays the orcs

    private static Simulation Flat(bool fog = false, int size = 96)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = fog;
        return sim;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static long Dist2(Simulation sim, int id, int x, int z)
    {
        int i = Index(sim, id);
        long dx = sim.Units.X[i].FloorToInt() - x, dz = sim.Units.Z[i].FloorToInt() - z;
        return dx * dx + dz * dz;
    }

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

    [Fact]
    public void TheDrummerIsDataAndNeedsWarDrums()
    {
        var db = ContentLoader.LoadEmbedded();
        var d = db.Unit("war_drummer");
        Assert.NotNull(d.Drums);
        Assert.Equal((40, 50, 6 * Tps, 60 * Tps, 40), (d.Drums!.Speed, d.Drums.Blow, d.Drums.ChargeTicks, d.Drums.WaitTicks, d.Drums.Heard));
        Assert.Equal(Fix64.FromInt(14), d.Rally);
        Assert.Equal("orcs", d.Faction!.Id);
        Assert.Equal("barracks", d.TrainedAt!.Id);
        Assert.False(d.InStart);

        var sim = Flat();
        var hall = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 4, 4)!;
        var barracks = sim.Economy.PlaceFinished(Orcs, sim.Content.Building("barracks"), 20, 4)!;
        foreach (var g in Goods.All) hall.Stock[(int)g] = 500;
        d = sim.Content.Unit("war_drummer");
        Assert.False(sim.Economy.Unlocked(Orcs, d));
        sim.Economy.Grant(Orcs, sim.Content.Tech("orc_war_drums"));
        Assert.True(sim.Economy.Unlocked(Orcs, d));
        sim.Schedule(0, new TrainCommand(Orcs, barracks.Id, "war_drummer"));
        RunUntil(sim, () => Enumerable.Range(0, sim.Units.Count).Any(i => sim.Units.Def[i] == d.Index), 30 * Tps, "the drummer's training");
    }

    [Fact]
    public void BrokenOrcsNearADrummerRallyThereInsteadOfRunningHome()
    {
        (Simulation Sim, int[] Orcs, int Drummer) Rout(bool drummer)
        {
            var sim = Flat();
            sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 4, 4);
            var spears = new[] { sim.SpawnUnit(Orcs, "spearman", 70, 70), sim.SpawnUnit(Orcs, "spearman", 72, 70), sim.SpawnUnit(Orcs, "spearman", 70, 72) };
            int drum = drummer ? sim.SpawnUnit(Orcs, "war_drummer", 76, 76) : 0;
            sim.Step();
            foreach (int s in spears) sim.Units.Morale[Index(sim, s)] = 10; // shaken to breaking
            sim.Run(4 * Tps);
            return (sim, spears, drum);
        }

        var (alone, run, _) = Rout(false);
        foreach (int s in run)
        {
            Assert.True(alone.Units.Rout[Index(alone, s)] > 0, "with no drummer they are still running");
            Assert.True(Dist2(alone, s, 8, 8) < 76 * 76, "running home, to the town hall"); // from about 88 columns off
        }

        var (sim, rallied, drummerId) = Rout(true);
        Assert.True(sim.Combat.IsDrumming(Index(sim, drummerId)) || rallied.All(s => sim.Units.Rout[Index(sim, s)] == 0));
        foreach (int s in rallied)
        {
            int i = Index(sim, s);
            Assert.Equal(0, sim.Units.Rout[i]);
            Assert.True(sim.Units.Morale[i] >= CombatSystem.RallyMorale);
            Assert.True(Dist2(sim, s, 76, 76) <= 4 * 4, "rallied at the drum");
        }
    }

    [Fact]
    public void ABrokenOrcOutOfEarshotStillRunsHome()
    {
        var sim = Flat();
        sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 4, 4);
        int spear = sim.SpawnUnit(Orcs, "spearman", 70, 70);
        sim.SpawnUnit(Orcs, "war_drummer", 70, 90); // 20 columns off: out of earshot
        sim.Step();
        sim.Units.Morale[Index(sim, spear)] = 10;
        sim.Run(3 * Tps);
        Assert.True(sim.Units.Rout[Index(sim, spear)] > 0);
        Assert.True(Dist2(sim, spear, 8, 8) < 80 * 80); // from about 88 columns off
    }

    [Fact]
    public void AChargeGetsOrcsInEarshotToTheirTargetSooner()
    {
        var sim = Flat(size: 128);
        int drum = sim.SpawnUnit(Orcs, "war_drummer", 10, 20);
        int near = sim.SpawnUnit(Orcs, "spearman", 10, 22);
        int far = sim.SpawnUnit(Orcs, "spearman", 10, 80); // 60 columns from the drum
        sim.Schedule(0, new MoveCommand(Orcs, new[] { drum, near }, 40, 21));
        sim.Schedule(0, new MoveCommand(Orcs, new[] { far }, 40, 80));
        sim.Schedule(0, new ChargeCommand(Orcs, new[] { drum }));
        sim.Step();
        Assert.True(sim.Combat.IsDrumming(Index(sim, drum)));
        int nearAt = -1, farAt = -1;
        for (int t = 1; t < 20 * Tps && (nearAt < 0 || farAt < 0); t++)
        {
            if (nearAt < 0 && sim.Units.Order[Index(sim, near)] == UnitOrder.Idle) nearAt = t;
            if (farAt < 0 && sim.Units.Order[Index(sim, far)] == UnitOrder.Idle) farAt = t;
            sim.Step();
        }
        Assert.True(nearAt > 0 && farAt > 0, "both got there");
        Assert.True(nearAt * 100 < farAt * 85, $"the charge is much faster: {nearAt} ticks against {farAt}");
    }

    [Fact]
    public void TheFirstBlowInAChargeLandsHarderAndOnlyTheFirst()
    {
        var sim = Flat();
        int drum = sim.SpawnUnit(Orcs, "war_drummer", 20, 18);
        int spear = sim.SpawnUnit(Orcs, "spearman", 20, 20);
        var def = sim.Content.Unit("spearman");
        sim.Schedule(0, new ChargeCommand(Orcs, new[] { drum }));
        sim.Step();
        int i = Index(sim, spear);
        Assert.True(sim.Combat.Charged(i));
        Assert.Equal(def.Damage * 150 / 100, sim.Combat.AttackOf(i, def));
        // An enemy steps up: the first blow is struck, and the next ones are as usual.
        int foe = sim.SpawnUnit(0, "spearman", 21, 20);
        RunUntil(sim, () => !sim.Combat.Charged(Index(sim, spear)), 3 * Tps, "the first blow");
        Assert.True(sim.Units.Hp[Index(sim, foe)] < sim.Content.Unit("spearman").MaxHp);
        Assert.Equal(def.Damage, sim.Combat.AttackOf(Index(sim, spear), def));
        // Out of earshot when the charge was beaten: nothing.
        var sim2 = Flat();
        int drum2 = sim2.SpawnUnit(Orcs, "war_drummer", 20, 18);
        int spear2 = sim2.SpawnUnit(Orcs, "spearman", 20, 60);
        sim2.Schedule(0, new ChargeCommand(Orcs, new[] { drum2 }));
        sim2.Step();
        Assert.False(sim2.Combat.Charged(Index(sim2, spear2)));
    }

    [Fact]
    public void TheDrumNeedsAMinuteBetweenCharges()
    {
        var sim = Flat();
        int drum = sim.SpawnUnit(Orcs, "war_drummer", 20, 20);
        var d = sim.Content.Unit("war_drummer").Drums!;
        sim.Schedule(0, new ChargeCommand(Orcs, new[] { drum }));
        sim.Step();
        Assert.False(sim.Combat.CanCharge(Index(sim, drum)));
        sim.Run(d.ChargeTicks + 1);
        Assert.False(sim.Combat.IsDrumming(Index(sim, drum)), "the charge is over");
        sim.Schedule(sim.Tick, new ChargeCommand(Orcs, new[] { drum }));
        sim.Step();
        Assert.False(sim.Combat.IsDrumming(Index(sim, drum)), "not ready again yet");
        sim.Run(d.WaitTicks - d.ChargeTicks);
        Assert.True(sim.Combat.CanCharge(Index(sim, drum)));
        sim.Schedule(sim.Tick, new ChargeCommand(Orcs, new[] { drum }));
        sim.Step();
        Assert.True(sim.Combat.IsDrumming(Index(sim, drum)));
        // Only its owner can beat it, and only a drummer drums.
        int spear = sim.SpawnUnit(Orcs, "spearman", 30, 20);
        sim.Schedule(sim.Tick, new ChargeCommand(Orcs, new[] { spear }));
        sim.Step();
        Assert.False(sim.Combat.IsDrumming(Index(sim, spear)));
    }

    [Fact]
    public void ADrummingDrummerShowsThroughTheFogToRivalsInEarshot()
    {
        var sim = Flat(fog: true, size: 128);
        int watcher = sim.SpawnUnit(0, "spearman", 10, 10);
        int farWatcher = sim.SpawnUnit(2, "spearman", 100, 100); // over 40 columns off
        int drum = sim.SpawnUnit(Orcs, "war_drummer", 40, 10);  // 30 columns off: well out of sight
        int spear = sim.SpawnUnit(Orcs, "spearman", 40, 12);
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.False(sim.Vision.SeesUnit(0, Index(sim, drum)));

        sim.Schedule(sim.Tick, new ChargeCommand(Orcs, new[] { drum }));
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.True(sim.Vision.SeesUnit(0, Index(sim, drum)), "the drum is heard");
        Assert.False(sim.Vision.SeesUnit(0, Index(sim, spear)), "only the drummer shows");
        Assert.False(sim.Vision.SeesUnit(2, Index(sim, drum)), "too far off to hear it");

        sim.Run(sim.Content.Unit("war_drummer").Drums!.ChargeTicks + VisionSystem.UpdateTicks);
        Assert.False(sim.Vision.SeesUnit(0, Index(sim, drum)), "silent again");
        _ = watcher;
        _ = farWatcher;
    }

    [Fact]
    public void TheChargeIsLoggedAndReplays()
    {
        var log = new CommandLog();
        log.Add(12, new ChargeCommand(1, new[] { 7, 9 }));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(new ChargeCommand(1, new[] { 7, 9 }), back.Commands.Single().Command);
    }

    [Fact]
    public void SameChargeSameHash()
    {
        ulong Play()
        {
            var sim = Flat();
            sim.Economy.PlaceFinished(Orcs, sim.Content.Building("town_hall"), 4, 4);
            int drum = sim.SpawnUnit(Orcs, "war_drummer", 40, 40);
            var orcs = new[] { sim.SpawnUnit(Orcs, "spearman", 42, 40), sim.SpawnUnit(Orcs, "spearman", 44, 40) };
            sim.SpawnUnit(0, "spearman", 50, 40);
            sim.SpawnUnit(0, "spearman", 50, 42);
            sim.Schedule(2, new ChargeCommand(Orcs, new[] { drum }));
            sim.Schedule(2, new AttackMoveCommand(Orcs, orcs, 52, 40));
            sim.Run(10 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    // --- Computer players ---------------------------------------------------------------------------

    private static Simulation WithStarts(int size, params (int X, int Z)[] starts)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks, List<Command>? log = null)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c =>
            {
                log?.Add(c);
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return done();
    }

    [Theory]
    [InlineData(AiLevel.Classic)]
    [InlineData(AiLevel.Normal)]
    public void AnOrcComputerPlayerTrainsADrummer(AiLevel level)
    {
        var sim = WithStarts(128, (24, 64), (100, 64));
        var hall = sim.Economy.TownHall(Orcs)!;
        Assert.NotNull(sim.Economy.PlaceFinished(Orcs, sim.Content.Building("barracks"), hall.X - 12, hall.Z + 10));
        sim.Economy.Grant(Orcs, sim.Content.Tech("orc_war_drums"));
        var ai = new AiPlayer(Orcs, 7, level);
        var drummer = sim.Content.Unit("war_drummer");
        bool Has() => Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == Orcs && sim.Units.Def[i] == drummer.Index) == 1;
        Assert.True(Play(sim, ai, () =>
        {
            foreach (var g in Goods.All) hall.Stock[(int)g] = Math.Max(hall.Stock[(int)g], 500);
            return Has();
        }, 3 * 60 * Tps), "a drummer to be trained");
        Play(sim, ai, () => { foreach (var g in Goods.All) hall.Stock[(int)g] = 500; return false; }, 60 * Tps);
        Assert.True(Has(), "only the one drummer");
    }

    [Fact]
    public void AnOrcWaveTakesItsDrummerAndBeatsTheChargeAtTheTarget()
    {
        var sim = WithStarts(128, (24, 64), (100, 64));
        for (int k = 0; k < 8; k++) sim.SpawnUnit(Orcs, "spearman", 94 + k, 56);
        int drum = sim.SpawnUnit(Orcs, "war_drummer", 110, 72); // well back: the wave sets out without it
        var ai = new AiPlayer(Orcs, 7, AiLevel.Normal);
        var log = new List<Command>();
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 3 * 60 * Tps, log), "a wave to set out");
        Assert.True(Play(sim, ai, () => ai.Wave.Contains(drum), 10 * Tps, log), "the drummer to go with the wave");
        Assert.True(Play(sim, ai, () => log.OfType<ChargeCommand>().Any(), 3 * 60 * Tps, log), "the charge to be beaten");
        var hall = sim.Economy.TownHall(0)!;
        int d = Index(sim, drum);
        Assert.True(d >= 0, "the drummer is alive when it beats the charge");
        Assert.True(Dist2(sim, drum, hall.X, hall.Z) <= (long)(AiPlayer.SiegeReach + 16) * (AiPlayer.SiegeReach + 16), "near the target");
    }
}
