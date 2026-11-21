using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Short truces between rival players (see <see cref="TruceSystem"/>).</summary>
public class TruceTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Simulation WithStarts(int size, params (int X, int Z)[] starts)
    {
        var sim = Flat(size);
        for (int p = 0; p < starts.Length; p++) sim.Map.Starts.Add(new StartArea(p, starts[p].X, Ground + 1, starts[p].Z));
        sim.PlaceStartingBuildings();
        return sim;
    }

    private static int[] Line(Simulation sim, int owner, string unit, int n, int x, int z)
    {
        var ids = new int[n];
        for (int k = 0; k < n; k++)
        {
            ids[k] = sim.SpawnUnit(owner, unit, x, z + 2 * k);
            Assert.True(ids[k] > 0);
        }
        return ids;
    }

    private static int Alive(Simulation sim, int[] ids) => ids.Count(sim.Units.Exists);

    private static int Hurt(Simulation sim, int[] ids) =>
        ids.Count(id => sim.Units.Exists(id) && sim.Units.Hp[sim.Units.IndexOf(id)] < sim.Economy.MaxHp(sim.Units.Owner[sim.Units.IndexOf(id)], sim.Content.Units[sim.Units.Def[sim.Units.IndexOf(id)]]));

    /// <summary>Both players say yes, one tick apart: from then on they are at peace.</summary>
    private static void Agree(Simulation sim, int a, int b)
    {
        sim.Schedule(sim.Tick, new TruceCommand(a, b));
        sim.Schedule(sim.Tick + 1, new TruceCommand(b, a));
        sim.Run(2);
        Assert.True(sim.Truces.AtPeace(a, b));
    }

    private static List<GameEvent> Events(Simulation sim, int player, GameEventKind kind) =>
        sim.Events.After(0, player).Where(e => e.Kind == kind).ToList();

    [Fact]
    public void AnOfferAcceptedInTimeStartsATruceForFiveMinutes()
    {
        var sim = Flat();
        sim.Schedule(0, new TruceCommand(0, 1));
        sim.Run(1);
        Assert.False(sim.Truces.AtPeace(0, 1));
        Assert.True(sim.Truces.Offered(0, 1));
        Assert.False(sim.Truces.Offered(1, 0));
        var offer = Assert.Single(Events(sim, 1, GameEventKind.TruceOffered));
        Assert.Equal(0, offer.Subject);

        sim.Schedule(sim.Tick, new TruceCommand(1, 0));
        sim.Run(1);
        long start = sim.Tick - 1;
        Assert.True(sim.Truces.AtPeace(0, 1));
        Assert.True(sim.Truces.AtPeace(1, 0));
        Assert.False(sim.Truces.Offered(0, 1));
        Assert.Equal(start + TruceSystem.TruceTicks, sim.Truces.EndsAt(0, 1));
        Assert.Single(Events(sim, 0, GameEventKind.TruceStarted));
        Assert.Single(Events(sim, 1, GameEventKind.TruceStarted));
        Assert.False(sim.Truces.AtPeace(0, 2));

        // A warning 30 seconds before it ends, then it is over.
        sim.Run((int)(start + TruceSystem.TruceTicks - TruceSystem.WarnTicks - sim.Tick));
        Assert.Empty(Events(sim, 0, GameEventKind.TruceEnding));
        sim.Run(1);
        var warn = Assert.Single(Events(sim, 1, GameEventKind.TruceEnding));
        Assert.Equal(-1, warn.Def);
        sim.Run(TruceSystem.WarnTicks);
        Assert.False(sim.Truces.AtPeace(0, 1));
        Assert.Single(Events(sim, 0, GameEventKind.TruceEnded));
        Assert.Single(Events(sim, 1, GameEventKind.TruceEnded));
    }

    [Fact]
    public void AnOfferNotAcceptedInTimeRunsOut()
    {
        var sim = Flat();
        sim.Schedule(0, new TruceCommand(0, 1));
        sim.Run(TruceSystem.OfferTicks + 1);
        Assert.False(sim.Truces.Offered(0, 1));
        // A late yes is only an offer of its own.
        sim.Schedule(sim.Tick, new TruceCommand(1, 0));
        sim.Run(1);
        Assert.False(sim.Truces.AtPeace(0, 1));
        Assert.True(sim.Truces.Offered(1, 0));
        // And it can be taken back.
        sim.Schedule(sim.Tick, new EndTruceCommand(1, 0));
        sim.Run(1);
        Assert.False(sim.Truces.Offered(1, 0));
        // Nobody makes a truce with themselves or the wild side.
        sim.Schedule(sim.Tick, new TruceCommand(0, 0));
        sim.Schedule(sim.Tick, new TruceCommand(0, Simulation.WildPlayer));
        sim.Run(1);
        Assert.False(sim.Truces.Offered(0, Simulation.WildPlayer));
    }

    [Fact]
    public void AtPeaceSoldiersSideBySideDoNotFightAndFightOnceItEnds()
    {
        var sim = Flat();
        Agree(sim, 0, 1);
        var a = Line(sim, 0, "spearman", 4, 28, 26);
        var b = Line(sim, 1, "archer", 4, 32, 26);
        sim.Run(20 * Tps);
        Assert.Equal(0, Hurt(sim, a));
        Assert.Equal(0, Hurt(sim, b));

        // An order to attack the other side is refused.
        sim.Schedule(sim.Tick, new AttackCommand(0, a, b[0]));
        sim.Run(10 * Tps);
        Assert.Equal(0, Hurt(sim, b));
        Assert.Equal(0, sim.Units.Target[sim.Units.IndexOf(a[0])]);

        // When the truce is over they fight.
        sim.Run((int)(sim.Truces.EndsAt(0, 1) - sim.Tick) + 20 * Tps);
        Assert.True(Hurt(sim, a) + Hurt(sim, b) + (8 - Alive(sim, a) - Alive(sim, b)) > 0);
    }

    [Fact]
    public void AFightGoingOnStopsWhenTheTruceStarts()
    {
        var sim = Flat();
        var a = Line(sim, 0, "spearman", 4, 26, 26);
        var b = Line(sim, 1, "spearman", 4, 34, 26);
        sim.Schedule(0, new AttackMoveCommand(0, a, 40, 30));
        sim.Schedule(0, new AttackMoveCommand(1, b, 20, 30));
        for (int t = 0; t < 30 * Tps && Hurt(sim, a) + Hurt(sim, b) == 0; t++) sim.Step();
        Assert.True(Hurt(sim, a) + Hurt(sim, b) > 0, "the fight to start");
        Agree(sim, 0, 1);
        sim.Run(2 * Tps); // blows already swung land
        var hp = a.Concat(b).Where(sim.Units.Exists).ToDictionary(id => id, id => sim.Units.Hp[sim.Units.IndexOf(id)]);
        sim.Run(20 * Tps);
        foreach (var (id, before) in hp)
        {
            Assert.True(sim.Units.Exists(id));
            Assert.True(sim.Units.Hp[sim.Units.IndexOf(id)] >= before, "no more blows at peace");
        }
    }

    [Fact]
    public void BreakingOffATruceGivesAWarningAndThenAWaitBeforeAnother()
    {
        var sim = Flat();
        Agree(sim, 0, 1);
        sim.Run(Tps);
        sim.Schedule(sim.Tick, new EndTruceCommand(1, 0));
        sim.Run(1);
        long ends = sim.Tick - 1 + TruceSystem.WarnTicks;
        Assert.True(sim.Truces.AtPeace(0, 1));
        Assert.Equal(ends, sim.Truces.EndsAt(0, 1));
        Assert.Equal(1, sim.Truces.BrokenBy(0, 1));
        var warn = Assert.Single(Events(sim, 0, GameEventKind.TruceEnding));
        Assert.Equal(1, warn.Def);
        Assert.Equal(1, warn.Subject);

        sim.Run((int)(ends - sim.Tick) + 1);
        Assert.False(sim.Truces.AtPeace(0, 1));
        Assert.Equal(1, Assert.Single(Events(sim, 0, GameEventKind.TruceEnded)).Def);

        // Three minutes before the two can agree again.
        Assert.False(sim.Truces.CanOffer(0, 1));
        sim.Schedule(sim.Tick, new TruceCommand(0, 1));
        sim.Schedule(sim.Tick + 1, new TruceCommand(1, 0));
        sim.Run(2);
        Assert.False(sim.Truces.AtPeace(0, 1));
        sim.Run((int)(sim.Truces.FreeAt(0, 1) - sim.Tick));
        Agree(sim, 0, 1);
    }

    [Fact]
    public void AtPeaceStorehousesCannotBeRaided()
    {
        var sim = Flat();
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 10)!;
        store.Stock[(int)Good.Wood] = 50;
        int raider = sim.SpawnUnit(1, "spearman", 30, 30);
        Agree(sim, 0, 1);
        sim.Schedule(sim.Tick, new CaptureCommand(1, new[] { raider }, store.Id));
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { raider }, store.Id));
        sim.Run(EconomySystem.CaptureTicks + 600);
        Assert.Equal(0, store.Owner);
        Assert.Equal(0, store.Lost + store.Damage);
        Assert.Equal(50, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void AComputerPlayerTakesATruceUnlessItIsClearlyTheStronger()
    {
        // Weaker than the one offering: it says yes.
        var sim = WithStarts(96, (20, 48), (76, 48));
        Line(sim, 0, "spearman", 3, 14, 56);
        Line(sim, 1, "spearman", 8, 70, 56);
        var ai = new[] { new AiPlayer(0, 1) };
        sim.Schedule(0, new TruceCommand(1, 0));
        Play(sim, ai, () => sim.Truces.AtPeace(0, 1), 10 * Tps);
        Assert.True(sim.Truces.AtPeace(0, 1));

        // Much stronger: it lets the offer run out.
        sim = WithStarts(96, (20, 48), (76, 48));
        Line(sim, 0, "spearman", 8, 14, 56);
        Line(sim, 1, "spearman", 2, 70, 56);
        ai = new[] { new AiPlayer(0, 1) };
        sim.Schedule(0, new TruceCommand(1, 0));
        Play(sim, ai, () => sim.Truces.AtPeace(0, 1), TruceSystem.OfferTicks + Tps);
        Assert.False(sim.Truces.AtPeace(0, 1));
    }

    [Fact]
    public void AComputerPlayerAtPeaceSendsNoWaveAtItsPartner()
    {
        // As AiTests' check that the computer finishes off an enemy with no soldiers, but at peace with it.
        var sim = WithStarts(96, (20, 48), (76, 48));
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 14 + k, 56);
        var hall1 = sim.Economy.TownHall(1)!;
        Agree(sim, 0, 1);
        var players = new[] { new AiPlayer(0, 1), new AiPlayer(1, 1) };
        Play(sim, players, () => false, TruceSystem.TruceTicks - 2 * Tps);
        Assert.True(sim.Truces.AtPeace(0, 1));
        Assert.NotNull(sim.Buildings.Get(hall1.Id));
        Assert.Equal(0, hall1.Lost + hall1.Damage);
        Assert.NotEqual(hall1.Id, players[0].WaveTarget);
    }

    [Fact]
    public void TrucesAreInTheHashAndReplayFromTheLog()
    {
        var log = CommandLog.UsualStart(5, 128, 48, 128);
        log.Add(1, new TruceCommand(0, 1));
        log.Add(3, new TruceCommand(1, 0));
        log.Add(40, new EndTruceCommand(0, 1));
        log.Ticks = 60;
        var text = log.ToString();
        Assert.Contains("truce 0 1", text);
        Assert.Contains("endtruce 0 1", text);
        var again = CommandLog.Parse(text);
        Assert.Equal(log.Commands, again.Commands);

        var a = MatchReplay.Run(log, 10, out var played);
        var b = MatchReplay.Run(again, 10);
        Assert.Equal(-1, MatchReplay.FirstMismatch(a, b));
        Assert.True(played.Truces.CanOffer(0, 1) == false); // broken off at 40, ends 30 seconds later: still at peace
        Assert.True(played.Truces.AtPeace(0, 1));
        var other = log.CreateSimulation();
        other.Run(60);
        Assert.NotEqual(other.StateHash(), played.StateHash());
    }

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
}
