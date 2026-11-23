using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

public class BoatTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4; a ship floats at 4 too
    private const int RiverX0 = 30, RiverX1 = 35; // a river three blocks deep along z: too deep to wade

    /// <summary>Flat ground with a deep river along z, from x = 30 to 35.</summary>
    private static Simulation River()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, RiverX0, 1, 0, RiverX1, Ground, 63, BlockType.Water);
        return TestWorlds.Sim(w);
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int X(Simulation sim, int id) => sim.Units.X[Index(sim, id)].FloorToInt();
    private static int Z(Simulation sim, int id) => sim.Units.Z[Index(sim, id)].FloorToInt();
    private static bool Aboard(Simulation sim, int id, int ship) => sim.Units.Aboard[Index(sim, id)] == ship;

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    [Fact]
    public void AShipFloatsOnTheWaterAndSailsAlongIt()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "ship", 32, 10);
        Assert.True(ship > 0, "a ship is put on the water");
        Assert.Equal(Ground + 1, sim.Units.Y[Index(sim, ship)]);
        Assert.Equal(-1, sim.SpawnUnit(0, "ship", 10, 10)); // not on land

        sim.Schedule(0, new MoveCommand(0, new[] { ship }, 33, 50));
        RunUntil(sim, () => Z(sim, ship) == 50 && sim.Units.Order[Index(sim, ship)] == UnitOrder.Idle, 30 * Tps, "the ship to sail up the river");
        Assert.Equal(33, X(sim, ship));
        Assert.Equal(Ground + 1, sim.Units.Y[Index(sim, ship)]);
    }

    [Fact]
    public void AShipNeverGoesOntoLandAndABridgeStopsIt()
    {
        var sim = River();
        // A deck across the river at z = 30, like a bridge.
        TestWorlds.Box(sim.World, RiverX0, Ground + 1, 30, RiverX1, Ground + 1, 30, BlockType.Planks);
        int ship = sim.SpawnUnit(0, "ship", 32, 10);
        sim.Schedule(0, new MoveCommand(0, new[] { ship }, 32, 50));
        sim.Run(30 * Tps);
        Assert.True(Z(sim, ship) < 30, "the bridge is in the way");
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, ship)]);
        for (int t = 0; t < 5; t++)
        {
            Assert.InRange(X(sim, ship), RiverX0, RiverX1);
            sim.Step();
        }
    }

    [Fact]
    public void UnitsBoardAShipRideItAcrossAndArePutAshoreOnTheFarBank()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "ship", 30, 10); // by the west bank
        var troops = Enumerable.Range(0, 4).Select(k => sim.SpawnUnit(0, "spearman", 24, 8 + k)).ToArray();
        sim.Schedule(0, new BoardCommand(0, troops, ship));
        RunUntil(sim, () => troops.All(t => Aboard(sim, t, ship)), 20 * Tps, "the spearmen to board");
        Assert.Equal(4, sim.Boats.Load(Index(sim, ship)));

        // Aboard, they take no orders of their own.
        sim.Schedule(sim.Tick, new MoveCommand(0, troops, 10, 10));
        sim.Run(Tps);
        Assert.All(troops, t => Assert.True(Aboard(sim, t, ship)));

        // Right-click land on the far bank: the ship sails over and puts them ashore there.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { ship }, 40, 30));
        RunUntil(sim, () => troops.All(t => !Aboard(sim, t, ship)), 40 * Tps, "the spearmen to be put ashore");
        Assert.All(troops, t =>
        {
            Assert.True(X(sim, t) > RiverX1, "on the east bank");
            Assert.InRange(Z(sim, t), 20, 40);
            Assert.Equal(Ground + 1, sim.Units.Y[Index(sim, t)]);
        });
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, ship)]);

        // And now they walk again.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { troops[0] }, 50, 30));
        RunUntil(sim, () => X(sim, troops[0]) == 50, 20 * Tps, "a landed spearman to walk on");
    }

    [Fact]
    public void ARightClickOnTheFarBankLandsThemThereNotOnAnIsletOnTheWay()
    {
        var sim = River();
        // A rock in the river on the way, nearer the ship than the far bank.
        TestWorlds.Box(sim.World, 31, 1, 26, 31, Ground, 27, BlockType.Stone);
        int ship = sim.SpawnUnit(0, "ship", 30, 10);
        var troops = new[] { sim.SpawnUnit(0, "spearman", 28, 9), sim.SpawnUnit(0, "spearman", 28, 11) };
        sim.Schedule(0, new BoardCommand(0, troops, ship));
        RunUntil(sim, () => troops.All(t => Aboard(sim, t, ship)), 20 * Tps, "both to board");
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { ship }, 38, 28));
        RunUntil(sim, () => troops.All(t => !Aboard(sim, t, ship)), 40 * Tps, "them to be put ashore");
        Assert.All(troops, t => Assert.True(X(sim, t) > RiverX1, "on the far bank"));
    }

    [Fact]
    public void ARidingArmyGoesWhereTheShipGoes()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "ship", 30, 10);
        int archer = sim.SpawnUnit(0, "archer", 28, 10);
        sim.Schedule(0, new BoardCommand(0, new[] { archer }, ship));
        RunUntil(sim, () => Aboard(sim, archer, ship), 20 * Tps, "the archer to board");
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { ship }, 32, 40));
        sim.Run(5 * Tps);
        int s = Index(sim, ship), a = Index(sim, archer);
        Assert.True(Z(sim, ship) > 15);
        Assert.True(Fix64.Abs(sim.Units.X[a] - sim.Units.X[s]) <= Fix64.FromRatio(3, 2) && Fix64.Abs(sim.Units.Z[a] - sim.Units.Z[s]) <= Fix64.FromRatio(3, 2));
        Assert.Equal(sim.Units.Y[s], sim.Units.Y[a]);
    }

    [Fact]
    public void AShipHasOnlySoManySeats()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "ship", 30, 20);
        int seats = sim.Content.Unit("ship").Seats;
        var troops = Enumerable.Range(0, seats + 2).Select(k => sim.SpawnUnit(0, "spearman", 22 + k % 4, 14 + k / 4)).ToArray();
        sim.Schedule(0, new BoardCommand(0, troops, ship));
        sim.Run(30 * Tps);
        Assert.Equal(seats, troops.Count(t => Aboard(sim, t, ship)));
        Assert.All(troops.Where(t => !Aboard(sim, t, ship)), t => Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, t)]));
    }

    [Fact]
    public void NobodyBoardsAShipOutOnTheWaterOrAnEnemysShip()
    {
        var sim = River();
        // A wide lake: the ship in its middle is out of reach of the shore.
        var lake = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(lake, 20, 1, 20, 44, Ground, 44, BlockType.Water);
        var sim2 = TestWorlds.Sim(lake);
        int far = sim2.SpawnUnit(0, "ship", 32, 32);
        int spear = sim2.SpawnUnit(0, "spearman", 15, 32);
        sim2.Schedule(0, new BoardCommand(0, new[] { spear }, far));
        sim2.Run(5 * Tps);
        Assert.False(Aboard(sim2, spear, far));
        Assert.Equal(UnitJob.None, sim2.Units.Job[Index(sim2, spear)]);

        int theirs = sim.SpawnUnit(1, "ship", 30, 10);
        int mine = sim.SpawnUnit(0, "spearman", 28, 10);
        sim.Schedule(0, new BoardCommand(0, new[] { mine }, theirs));
        sim.Run(5 * Tps);
        Assert.False(Aboard(sim, mine, theirs));
    }

    [Fact]
    public void ThoseAboardGoDownWithTheShip()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "ship", 30, 10);
        var troops = new[] { sim.SpawnUnit(0, "spearman", 28, 9), sim.SpawnUnit(0, "archer", 28, 11) };
        sim.Schedule(0, new BoardCommand(0, troops, ship));
        RunUntil(sim, () => troops.All(t => Aboard(sim, t, ship)), 20 * Tps, "both to board");
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { ship }, 32, 30));
        sim.Run(3 * Tps);
        // Holed below the waterline, and enemy archers on the bank finish it.
        sim.Units.Hp[Index(sim, ship)] = 1;
        for (int k = 0; k < 3; k++) sim.SpawnUnit(1, "archer", 38, 26 + 2 * k);
        RunUntil(sim, () => !sim.Units.Exists(ship), 20 * Tps, "the ship to sink");
        sim.Run(3);
        Assert.All(troops, t => Assert.False(sim.Units.Exists(t)));
    }

    [Fact]
    public void ADockOnTheShoreLaunchesTheShipsItBuildsOntoTheWater()
    {
        var sim = River();
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 10, 10)!;
        store.Stock[(int)Good.Wood] = 500;
        store.Stock[(int)Good.Planks] = 500;
        store.Stock[(int)Good.Food] = 500;
        sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 10, 20);
        var dock = sim.Economy.PlaceFinished(0, sim.Content.Building("dock"), 25, 20)!;
        Assert.Equal(PlaceResult.NoWater, sim.Economy.CanPlace(0, sim.Content.Building("dock"), 10, 30, out _));
        sim.Schedule(0, new TrainCommand(0, dock.Id, "ship"));
        int ship = -1;
        RunUntil(sim, () =>
        {
            for (int i = 0; i < sim.Units.Count; i++)
                if (sim.Content.Units[sim.Units.Def[i]].IsBoat) { ship = sim.Units.Id[i]; return true; }
            return false;
        }, 60 * Tps, "the dock to build a ship");
        Assert.True(sim.Boats.Navigable(X(sim, ship), Z(sim, ship)), "launched onto the water");
        Assert.Equal(Ground + 1, sim.Units.Y[Index(sim, ship)]);
    }

    [Fact]
    public void UnloadPutsThemAshoreWhereTheShipIs()
    {
        var sim = River();
        int ship = sim.SpawnUnit(0, "ship", 30, 10);
        int spear = sim.SpawnUnit(0, "spearman", 28, 10);
        sim.Schedule(0, new BoardCommand(0, new[] { spear }, ship));
        RunUntil(sim, () => Aboard(sim, spear, ship), 20 * Tps, "the spearman to board");
        sim.Schedule(sim.Tick, new UnloadCommand(0, new[] { ship }));
        RunUntil(sim, () => !Aboard(sim, spear, ship), 10 * Tps, "the spearman to be put ashore");
        Assert.True(X(sim, spear) < RiverX0, "back on the west bank, by the ship");
    }

    [Fact]
    public void BoardAndUnloadAreKeptInTheCommandLog()
    {
        var log = CommandLog.Parse("rts-log 1\nseed 1\nworld 32 16 32\nticks 10\n5 board 0 9 3,4\n9 unload 1 9\n");
        Assert.Equal(new BoardCommand(0, new[] { 3, 4 }, 9), log.Commands[0].Command);
        Assert.Equal(new UnloadCommand(1, new[] { 9 }), log.Commands[1].Command);
        var copy = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands, copy.Commands);
    }

    [Fact]
    public void SameInputsSameHashWithShips()
    {
        ulong Play()
        {
            var sim = River();
            int ship = sim.SpawnUnit(0, "ship", 30, 10);
            var troops = Enumerable.Range(0, 3).Select(k => sim.SpawnUnit(0, "spearman", 26, 9 + k)).ToArray();
            sim.Schedule(0, new BoardCommand(0, troops, ship));
            sim.Schedule(15 * Tps, new MoveCommand(0, new[] { ship }, 42, 40));
            sim.Run(50 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
