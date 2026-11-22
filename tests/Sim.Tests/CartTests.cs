using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Carts (the design doc's logistics rule, "carts carry four times more"): a hauler unit that only does supply
/// duty, twenty goods a load, and finds that duty on its own.
/// </summary>
public class CartTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>A town hall at (6, 6) holding 100 stone and 200 food, and a storehouse 30 columns away at (40, 6).</summary>
    private static (Simulation Sim, Building Hall, Building Store) Town()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 6, 6);
        Array.Clear(hall.Stock);
        hall.Stock[(int)Good.Stone] = 100;
        hall.Stock[(int)Good.Food] = 200;
        var store = Place(sim, 0, "storehouse", 40, 6);
        return (sim, hall, store);
    }

    private static int OnDuty(Simulation sim, Building b)
    {
        var u = sim.Units;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Job[i] == UnitJob.Supply && u.JobTarget[i] == b.Id) n++;
        return n;
    }

    [Fact]
    public void TheCartIsInTheDataAsAHaulerPaidForInPlanks()
    {
        var cart = ContentLoader.LoadEmbedded().Unit("cart");
        Assert.Equal(UnitRole.Hauler, cart.Role);
        Assert.True(cart.IsHauler);
        Assert.Equal(4 * EconomySystem.CarryCapacity, cart.Carry);
        Assert.False(cart.CanAttack);
        Assert.Equal("town_hall", cart.TrainedAt!.Id);
        Assert.Contains(cart.Cost, c => c.Good == Good.Planks);
        // Matches do not start with carts.
        Assert.DoesNotContain(cart, ContentLoader.LoadEmbedded().StartUnits);
    }

    [Fact]
    public void ACartHaulsFourWorkersLoadsAtATime()
    {
        var (sim, hall, store) = Town();
        int cart = sim.SpawnUnit(0, "cart", 20, 14);
        int ci = sim.Units.IndexOf(cart);
        Assert.Equal(20, sim.Economy.LoadOf(ci));
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 60));
        sim.Schedule(1, new WorkCommand(0, new[] { cart }, store.Id));

        // Its first load is twenty stone in one go.
        int most = 0;
        RunUntil(sim, () =>
        {
            most = Math.Max(most, sim.Units.CarryAmount[sim.Units.IndexOf(cart)]);
            return store.Stock[(int)Good.Stone] > 0;
        }, 2 * 60 * Tps, "the first load to arrive");
        Assert.Equal(20, most);
        Assert.Equal(20, store.Stock[(int)Good.Stone]);

        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 60, 4 * 60 * Tps, "the storehouse to hold 60 stone");
        sim.Run(30 * Tps);
        Assert.Equal(60, store.Stock[(int)Good.Stone]);
        Assert.Equal(40, hall.Stock[(int)Good.Stone]);
        // The player put it on duty: it waits there for more.
        Assert.Equal(1, OnDuty(sim, store));
    }

    [Fact]
    public void ACartBringsNoMoreThanIsMissing()
    {
        var (sim, hall, store) = Town();
        int cart = sim.SpawnUnit(0, "cart", 20, 14);
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 7));
        sim.Schedule(1, new WorkCommand(0, new[] { cart }, store.Id));
        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 7, 3 * 60 * Tps, "the storehouse to hold 7 stone");
        sim.Run(30 * Tps);
        Assert.Equal(7, store.Stock[(int)Good.Stone]);
        Assert.Equal(93, hall.Stock[(int)Good.Stone]);
        Assert.Equal(0, sim.Units.CarryAmount[sim.Units.IndexOf(cart)]);
    }

    [Fact]
    public void ACartWinsOverAWorkerOnTheSameRun()
    {
        // The same 30-column run with 80 stone to move: the cart takes four trips, a worker sixteen.
        int Moved(string unit)
        {
            var (sim, _, store) = Town();
            int id = sim.SpawnUnit(0, unit, 20, 14);
            sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 80));
            sim.Schedule(1, new WorkCommand(0, new[] { id }, store.Id));
            sim.Run(90 * Tps);
            return store.Stock[(int)Good.Stone];
        }
        int byCart = Moved("cart"), byWorker = Moved("worker");
        Assert.True(byCart >= 2 * byWorker, $"a cart moved {byCart} in 90 seconds, a worker {byWorker}");
    }

    [Fact]
    public void ACartOnlyHauls()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 6, 6);
        hall.Stock[(int)Good.Wood] = 500;
        hall.Stock[(int)Good.Stone] = 500;
        var farm = Place(sim, 0, "farm", 30, 30);
        int cart = sim.SpawnUnit(0, "cart", 20, 20);
        int i = sim.Units.IndexOf(cart);
        int stoneX = 25, stoneZ = 10;
        sim.World.Set(stoneX, Ground, stoneZ, BlockType.Stone);
        sim.Schedule(0, new GatherCommand(0, new[] { cart }, stoneX, Ground, stoneZ));
        sim.Schedule(0, new WorkCommand(0, new[] { cart }, farm.Id));
        sim.Schedule(0, new BuildCommand(0, new[] { cart }, "house", 40, 40));
        // A storage building with no supply rules has nothing for an empty cart either.
        sim.Schedule(0, new WorkCommand(0, new[] { cart }, hall.Id));
        sim.Run(5 * Tps);
        Assert.Equal(UnitJob.None, sim.Units.Job[i]);
        Assert.Equal(BlockType.Stone, sim.World.Get(stoneX, Ground, stoneZ));
    }

    [Fact]
    public void ATrainedCartComesOutAutomaticAndFindsSupplyWorkOnItsOwn()
    {
        var (sim, hall, store) = Town();
        hall.Stock[(int)Good.Wood] = 100;
        hall.Stock[(int)Good.Planks] = 50;
        var cartDef = sim.Content.Unit("cart");
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 40));
        sim.Schedule(0, new TrainCommand(0, hall.Id, "cart"));
        sim.Step();
        Assert.Equal(35, hall.Stock[(int)Good.Planks]);

        int Cart()
        {
            var u = sim.Units;
            for (int k = 0; k < u.Count; k++)
                if (u.Def[k] == cartDef.Index) return k;
            return -1;
        }
        RunUntil(sim, () => Cart() >= 0, 60 * Tps, "the cart to be trained");
        Assert.True(sim.Units.Auto[Cart()]);

        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 40, 4 * 60 * Tps, "the cart to stock the storehouse");
        Assert.Equal(40, store.Stock[(int)Good.Stone]);
        // With nothing left to fetch, an automatic cart lets the duty go, and takes it again when there is some.
        sim.Run(10 * Tps);
        Assert.Equal(0, OnDuty(sim, store));
        store.Stock[(int)Good.Stone] = 10;
        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 40, 4 * 60 * Tps, "the cart to top the storehouse up again");
        Assert.Equal(40, store.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void CartsAreNotWorkers()
    {
        // Automatic workers' houses and farms, and computer players, count workers: a cart is not one, and
        // making every worker automatic leaves it to its own duty.
        var (sim, _, store) = Town();
        int cart = sim.SpawnUnit(0, "cart", 20, 14);
        int worker = sim.SpawnUnit(0, "worker", 22, 14);
        sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 30));
        sim.Schedule(0, new AutomateCommand(0, new[] { cart, worker }, true));
        RunUntil(sim, () => store.Stock[(int)Good.Stone] >= 30, 4 * 60 * Tps, "the storehouse to be stocked");
        Assert.True(sim.Units.Auto[sim.Units.IndexOf(cart)]);
        Assert.DoesNotContain(sim.Content.Unit("cart"), sim.Content.BaseUnits);
    }

    [Fact]
    public void OnlyACartHasACarry()
    {
        var files = ContentTests.Files();
        files["units.json"] = files["units.json"].Replace("\"speed\": 3, \"hp\": 90, \"model\": \"cart\", \"carry\": 20,", "\"speed\": 3, \"hp\": 90, \"model\": \"cart\",")
            .Replace("{ \"id\": \"worker\", \"name\": \"Worker\", \"role\": \"worker\",", "{ \"id\": \"worker\", \"name\": \"Worker\", \"role\": \"worker\", \"carry\": 10,");
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.StartsWith("units.json: unit 'cart': is a cart (role hauler): it needs a \"carry\""));
        Assert.Contains("units.json: unit 'worker': only a cart (role hauler) has a \"carry\"", e.Errors);
    }

    [Fact]
    public void SameInputsGiveTheSameHashWithCarts()
    {
        ulong Run()
        {
            var (sim, hall, store) = Town();
            hall.Stock[(int)Good.Wood] = 100;
            hall.Stock[(int)Good.Planks] = 50;
            sim.Schedule(0, new SupplyCommand(0, store.Id, Good.Stone, 50));
            sim.Schedule(0, new TrainCommand(0, hall.Id, "cart"));
            sim.Schedule(0, new TrainCommand(0, hall.Id, "cart"));
            sim.Run(3 * 60 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
