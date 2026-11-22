using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Planks: wood sawn into planks at a sawmill, carried to storage, and spent on bridges.</summary>
public class SawmillTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

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

    [Fact]
    public void PlanksAreAGoodAndTheSawmillIsInTheData()
    {
        Assert.Contains(Good.Planks, Goods.All);
        Assert.Equal(Goods.All.Length, Goods.Count);
        Assert.Equal("Planks", Goods.Name(Good.Planks));
        Assert.Equal(Good.Planks, Goods.Parse("Planks"));

        var sim = Flat();
        var def = sim.Content.Building("sawmill");
        Assert.Equal(BuildingKind.Workshop, def.Kind);
        Assert.Equal((Good.Wood, Good.Planks), (def.Takes, def.Makes));
        Assert.True(def.MakeTicks > 0);
        Assert.Equal(50, def.Keep);
        Assert.True(def.WorkerSlots >= 1);
        Assert.Empty(def.Requires); // no tech: an early building
        Assert.Equal(Good.Planks, EconomySystem.Makes(def));
        Assert.Equal(Good.Iron, EconomySystem.Makes(sim.Content.Building("smelter")));
        Assert.Null(EconomySystem.Makes(sim.Content.Building("shrine")));
        // Bridges are laid with planks, not raw wood.
        Assert.Equal(new[] { Good.Planks }, sim.Content.Building("bridge").Cost.Select(c => c.Good));
    }

    [Fact]
    public void ASawyerSawsWoodFromStorageIntoPlanksAndCarriesThemBack()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 12;
        var mill = Place(sim, P, "sawmill", 24, 10);
        int w = sim.SpawnUnit(P, "worker", 18, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, mill.Id));
        sim.Step();
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);

        RunUntil(sim, () => store.Stock[(int)Good.Planks] == 12, 6000, "twelve planks stored");
        Assert.Equal(0, store.Stock[(int)Good.Wood]);
        Assert.Equal(0, mill.Stock[(int)Good.Wood]);
        Assert.Equal(0, mill.Stock[(int)Good.Planks]);

        // Nothing left to saw: the sawyer stays on the job and waits.
        sim.Run(200);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);
        Assert.Equal(12, store.Stock[(int)Good.Planks]);
    }

    [Fact]
    public void SawingOnePlankTakesTheSawTime()
    {
        var sim = Flat();
        var mill = Place(sim, P, "sawmill", 24, 10);
        mill.Stock[(int)Good.Wood] = 2;
        sim.Step(); // starts the first plank
        Assert.Equal(1, mill.Stock[(int)Good.Wood]);
        sim.Run(mill.Def.MakeTicks - 1);
        Assert.Equal(0, mill.Stock[(int)Good.Planks]);
        sim.Step();
        Assert.Equal(1, mill.Stock[(int)Good.Planks]);
        Assert.Equal(0, mill.Stock[(int)Good.Wood]); // the second one is under the saw
        sim.Run(mill.Def.MakeTicks);
        Assert.Equal(2, mill.Stock[(int)Good.Planks]);
    }

    [Fact]
    public void TheSawyerStopsFetchingWoodOnceThereArePlanksEnough()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 100;
        store.Stock[(int)Good.Planks] = sim.Content.Building("sawmill").Keep;
        var mill = Place(sim, P, "sawmill", 24, 10);
        int w = sim.SpawnUnit(P, "worker", 18, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, mill.Id));
        sim.Run(30 * Tps);
        Assert.Equal(100, store.Stock[(int)Good.Wood]); // the town's wood is left for building

        // Planks spent: it saws again.
        store.Stock[(int)Good.Planks] = 0;
        RunUntil(sim, () => store.Stock[(int)Good.Planks] >= 5, 60 * Tps, "planks sawn once they ran short");
        Assert.True(store.Stock[(int)Good.Wood] < 100);
    }

    [Fact]
    public void AnAutomaticWorkerKeepsASawmillOnlyWhilePlanksAreShort()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        store.Stock[(int)Good.Planks] = sim.Content.Building("sawmill").Keep;
        store.Stock[(int)Good.Wood] = 40;
        var mill = Place(sim, P, "sawmill", 20, 10);
        TestWorlds.Tree(sim.World, 30, 30, 4);
        TestWorlds.Tree(sim.World, 36, 36, 4);
        int a = sim.SpawnUnit(P, "worker", 12, 20), b = sim.SpawnUnit(P, "worker", 13, 20);
        sim.Schedule(sim.Tick, new AutomateCommand(P, new[] { a }, true));
        sim.Run(2 * Tps);
        Assert.NotEqual(UnitJob.Haul, sim.Units.Job[Index(sim, a)]); // planks enough: nothing to saw

        store.Stock[(int)Good.Planks] = 0;
        sim.Schedule(sim.Tick, new AutomateCommand(P, new[] { b }, true));
        sim.Run(2 * Tps);
        var sawyers = new[] { a, b }.Where(id => sim.Units.Job[Index(sim, id)] == UnitJob.Haul).ToList();
        Assert.Single(sawyers);
        Assert.Equal(mill.Id, sim.Units.JobTarget[Index(sim, sawyers[0])]);
        RunUntil(sim, () => store.Stock[(int)Good.Planks] >= 10, 90 * Tps, "planks sawn by the automatic worker");
    }

    [Fact]
    public void PlanksFromTheSawmillLayABridge()
    {
        // A deep river across flat ground; a town with only wood saws planks, then bridges it.
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        TestWorlds.Box(w, 30, 1, 0, 35, Ground, 63, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        var store = Place(sim, P, "storehouse", 10, 10);
        store.Stock[(int)Good.Wood] = 12;
        var mill = Place(sim, P, "sawmill", 18, 10);
        int sawyer = sim.SpawnUnit(P, "worker", 16, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { sawyer }, mill.Id));
        RunUntil(sim, () => store.Stock[(int)Good.Planks] == 12, 6000, "planks for the bridge");

        int builder = sim.SpawnUnit(P, "worker", 26, 32);
        sim.Schedule(sim.Tick, new BridgeCommand(P, new[] { builder }, "bridge", 28, 32, 37, 32));
        sim.Step();
        var bridge = sim.Buildings.All.Where(b => b.Def.IsBridge).ToList();
        Assert.Equal(6, bridge.Count);
        Assert.Equal(0, store.Stock[(int)Good.Planks]);
        RunUntil(sim, () => bridge.All(b => b.Active), 120 * Tps, "the bridge to be built");
    }

    [Fact]
    public void AWorkshopNeedsWhatItTakesMakesAndItsTimeAndNothingElseHasThem()
    {
        static Dictionary<string, string> With(string building)
        {
            var files = new Dictionary<string, string>();
            foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
            files["buildings.json"] = files["buildings.json"].TrimEnd().TrimEnd(']') + ", " + building + "]";
            return files;
        }
        const string Pit = "{ \"id\": \"saw_pit\", \"name\": \"Saw pit\", \"kind\": \"workshop\", \"size\": [3, 3], \"cost\": { \"wood\": 1 }, \"buildTime\": 5";
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(Pit + ", \"workerSlots\": 1, \"takes\": \"wood\", \"makes\": \"planks\" }")));
        Assert.Contains("a workshop needs \"makeTime\"", e.Message);
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(Pit + ", \"workerSlots\": 1, \"makes\": \"planks\", \"makeTime\": 3 }")));
        Assert.Contains("\"takes\"", e.Message);
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(Pit + ", \"workerSlots\": 1, \"takes\": \"wood\", \"makes\": \"wood\", \"makeTime\": 3 }")));
        Assert.Contains("takes and makes the same good (wood)", e.Message);
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(
            "{ \"id\": \"hut\", \"name\": \"Hut\", \"kind\": \"house\", \"size\": [3, 3], \"cost\": { \"wood\": 1 }, \"buildTime\": 5, \"makeTime\": 5 }")));
        Assert.Contains("only a workshop has", e.Message);
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(Pit + ", \"takes\": \"wood\", \"makes\": \"planks\", \"makeTime\": 3 }")));
        Assert.Contains("needs \"workerSlots\"", e.Message);
    }

    [Fact]
    public void PlanksInAMillCountInTheStateHash()
    {
        var a = Flat();
        var b = Flat();
        var ma = Place(a, P, "sawmill", 24, 10);
        var mb = Place(b, P, "sawmill", 24, 10);
        Assert.Equal(a.StateHash(), b.StateHash());
        mb.Stock[(int)Good.Planks] = 1;
        Assert.NotEqual(a.StateHash(), b.StateHash());
        _ = ma;
    }
}
