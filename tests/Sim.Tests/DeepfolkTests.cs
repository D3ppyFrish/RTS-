using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Deepfolk, the insect hive (see "The Deepfolk" in docs/rules.md): their own units and brood chamber in place of
/// everyone's (<see cref="FactionDef.UnitReplacements"/>, <see cref="FactionDef.BuildingReplacements"/>), swarms
/// hatching in batches (<see cref="UnitDef.Batch"/>), burrowers that tunnel fast, and units that strike harder for a
/// few seconds after they burst up out of the ground (<see cref="UnitDef.Burst"/>, <see cref="CombatSystem.StepBurst"/>).
/// </summary>
public class DeepfolkTests
{
    private const int Ground = 10; // grass at 10, units on the surface stand at 11
    private const int Tunnel = 4;  // a tunnel's floor: units in it stand at 4, under rock from 6 to 10
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 24, size, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Count(Simulation sim, int owner, string unit)
    {
        int def = sim.Content.Unit(unit).Index, n = 0;
        for (int i = 0; i < sim.Units.Count; i++)
            if (sim.Units.Owner[i] == owner && sim.Units.Def[i] == def) n++;
        return n;
    }

    [Fact]
    public void TheDeepfolkAreTheThirdFaction()
    {
        var content = ContentDb.Default;
        var deep = content.Faction("deepfolk");
        Assert.Equal(2, deep.Index);
        Assert.Equal("Deepfolk", deep.Name);
        Assert.Null(deep.Experience);
        Assert.False(deep.TamesMonsters);
        Assert.Contains(("barracks", "brood_chamber"), deep.BuildingReplacements.Select(r => (r.From.Id, r.To.Id)));
        Assert.Contains(("sapper", "burrower"), deep.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        // The other factions replace nothing.
        Assert.Empty(content.Faction("settlers").UnitReplacements);
        Assert.Empty(content.Faction("orcs").BuildingReplacements);
    }

    [Fact]
    public void EachFactionStartsWithItsOwnUnits()
    {
        var content = ContentDb.Default;
        // Everyone's start units are those of no faction; the Settlers and Orcs start with just those, as before.
        Assert.DoesNotContain(content.StartUnits, u => u.Faction != null);
        Assert.Equal(content.StartUnits, content.Faction("settlers").StartUnits);
        Assert.Equal(content.StartUnits, content.Faction("orcs").StartUnits);
        // The Deepfolk start with as many, their own in place of those they replace.
        var deep = content.Faction("deepfolk").StartUnits;
        Assert.Equal(content.StartUnits.Count, deep.Count);
        Assert.Equal(new[] { "worker", "skitterling", "spitter", "skitterling", "burrower", "spitter" }, deep.Select(u => u.Id));

        var sim = Flat(96);
        sim.SetFaction(1, "deepfolk");
        sim.Map.Starts.Add(new StartArea(0, 20, Ground + 1, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Ground + 1, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(2, Count(sim, 0, "spearman"));
        Assert.Equal(0, Count(sim, 0, "skitterling"));
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "sapper") + Count(sim, 1, "mage") + Count(sim, 1, "rider"));
        Assert.Equal(2, Count(sim, 1, "worker"));
        Assert.Equal(4, Count(sim, 1, "skitterling"));
        Assert.Equal(4, Count(sim, 1, "spitter"));
        Assert.Equal(2, Count(sim, 1, "burrower"));
    }

    [Fact]
    public void TheDeepfolkBuildBroodChambersInsteadOfBarracks()
    {
        var sim = Flat();
        sim.SetFaction(1, "deepfolk");
        var econ = sim.Economy;
        var barracks = sim.Content.Building("barracks");
        var great = sim.Content.Building("great_barracks");
        var brood = sim.Content.Building("brood_chamber");
        Assert.False(econ.Unlocked(1, barracks));
        Assert.True(econ.Unlocked(1, brood));
        Assert.True(econ.Unlocked(0, barracks));
        Assert.False(econ.Unlocked(0, brood)); // another faction's
        Assert.True(sim.FactionOf(1).Replaces(great)); // an upgrade of what they replace
        Assert.Equal(new[] { "skitterling", "spitter", "burrower" }, brood.Trains);
        foreach (var id in new[] { "spearman", "archer", "rider", "sapper", "mage" })
        {
            Assert.False(econ.Unlocked(1, sim.Content.Unit(id)), id);
            Assert.True(econ.Unlocked(0, sim.Content.Unit(id)) || sim.Content.Unit(id).Requires.Count > 0, id);
        }
        Assert.True(econ.Unlocked(1, sim.Content.Unit("worker")));
        Assert.False(econ.Unlocked(0, sim.Content.Unit("skitterling")));
    }

    [Fact]
    public void ACapturedBarracksTrainsNothingForTheDeepfolk()
    {
        var sim = Flat();
        sim.SetFaction(1, "deepfolk");
        Place(sim, 1, "town_hall", 10, 10);
        var barracks = Place(sim, 1, "barracks", 30, 10); // as if taken from the Settlers
        var hall = sim.Economy.TownHall(1)!;
        hall.Stock[(int)Good.Food] = 500;
        hall.Stock[(int)Good.Wood] = 500;
        sim.Schedule(0, new TrainCommand(1, barracks.Id, "spearman"));
        sim.Schedule(0, new TrainCommand(1, barracks.Id, "healer"));
        sim.Step();
        Assert.Empty(barracks.Queue);
        Assert.Equal(500, hall.Stock[(int)Good.Food]);
    }

    [Fact]
    public void SkitterlingsHatchFourAtATimeForOneCost()
    {
        var sim = Flat();
        sim.SetFaction(0, "deepfolk");
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var brood = Place(sim, 0, "brood_chamber", 30, 10);
        var skitter = sim.Content.Unit("skitterling");
        Assert.Equal(4, skitter.Batch);
        hall.Stock[(int)Good.Food] = skitter.Cost.Single(c => c.Good == Good.Food).Amount;
        sim.Schedule(0, new TrainCommand(0, brood.Id, "skitterling"));
        sim.Run(skitter.TrainTicks + 2);
        Assert.Equal(4, Count(sim, 0, "skitterling"));
        Assert.Equal(0, hall.Stock[(int)Good.Food]);
        Assert.Empty(brood.Queue);
        // They step out side by side at the door, not on top of each other.
        var spots = Enumerable.Range(0, sim.Units.Count)
            .Where(i => sim.Units.Def[i] == skitter.Index)
            .Select(i => (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt())).Distinct().Count();
        Assert.True(spots >= 3, $"{spots} spots");
        Assert.All(Enumerable.Range(0, sim.Units.Count).Where(i => sim.Units.Def[i] == skitter.Index),
            i => Assert.True(brood.ColumnDistance(sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()) <= 3));
    }

    [Fact]
    public void ABatchWaitsForHousingForAllOfIt()
    {
        var sim = Flat();
        sim.SetFaction(0, "deepfolk");
        var hall = Place(sim, 0, "town_hall", 10, 10);
        var brood = Place(sim, 0, "brood_chamber", 30, 10);
        hall.Stock[(int)Good.Food] = 500;
        int housing = sim.Economy.Housing(0);
        // Room for three more, not four.
        for (int k = 0; k < housing - 3; k++) Assert.True(sim.SpawnUnit(0, "worker", 4 + k % 40, 30 + k / 40 * 2) > 0);
        var skitter = sim.Content.Unit("skitterling");
        sim.Schedule(0, new TrainCommand(0, brood.Id, "skitterling"));
        sim.Run(skitter.TrainTicks * 2);
        Assert.Equal(0, Count(sim, 0, "skitterling"));
        Assert.Single(brood.Queue);

        // A house makes room, and the whole batch hatches (training starts once there is room, as for any unit).
        Place(sim, 0, "house", 10, 50);
        sim.Run(skitter.TrainTicks + Tps);
        Assert.Equal(4, Count(sim, 0, "skitterling"));
    }

    [Fact]
    public void ABurrowerTunnelsTwiceAsFastAsASapper()
    {
        // Two parallel tunnels through deep rock, one dug by a sapper and one by a burrower, for the same time.
        var w = TestWorlds.Flat(64, 24, 64, Ground);
        TestWorlds.Box(w, 10, Tunnel, 20, 12, Tunnel + 1, 20, BlockType.Air);
        TestWorlds.Box(w, 10, Tunnel, 40, 12, Tunnel + 1, 40, BlockType.Air);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(1, "deepfolk");
        int sapper = sim.SpawnUnit(0, "sapper", 11, 20, Tunnel);
        int burrower = sim.SpawnUnit(1, "burrower", 11, 40, Tunnel);
        sim.Schedule(0, new TunnelCommand(0, new[] { sapper }, 50, Tunnel, 20));
        sim.Schedule(0, new TunnelCommand(1, new[] { burrower }, 50, Tunnel, 40));
        sim.Run(40 * Tps);
        int dugBySapper = sim.Units.X[sim.Units.IndexOf(sapper)].FloorToInt() - 11;
        int dugByBurrower = sim.Units.X[sim.Units.IndexOf(burrower)].FloorToInt() - 11;
        Assert.True(dugBySapper >= 2, $"the sapper dug {dugBySapper}");
        Assert.True(dugByBurrower >= dugBySapper * 3 / 2, $"the burrower dug {dugByBurrower}, the sapper {dugBySapper}");
    }

    [Fact]
    public void BurstingUpOutOfTheGroundMakesTheirBlowsHarderForAFewSeconds()
    {
        // A tunnel through the rock that comes up a ramp into the open at x = 30.
        var w = TestWorlds.Flat(64, 24, 64, Ground);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(0, "deepfolk");
        var combat = sim.Combat;
        var skitter = sim.Content.Unit("skitterling");
        Assert.Equal(50, skitter.Burst);
        TestWorlds.Box(w, 10, Tunnel, 30, 20, Tunnel + 1, 30, BlockType.Air);
        int bug = sim.SpawnUnit(0, "skitterling", 15, 30, Tunnel);
        int surface = sim.SpawnUnit(0, "skitterling", 40, 40);
        sim.Run(CombatSystem.BurstLook * 2);
        int b = sim.Units.IndexOf(bug), s = sim.Units.IndexOf(surface);
        Assert.True(sim.Units.Burrowed[b]);
        Assert.False(sim.Units.Burrowed[s]);
        Assert.False(combat.Bursting(b) || combat.Bursting(s));
        Assert.Equal(skitter.Damage, combat.AttackOf(b, skitter));

        // The ground over it is dug away: it is out in the open, and strikes half as hard again for a few seconds.
        TestWorlds.Box(w, 13, Tunnel + 2, 30, 17, Ground, 30, BlockType.Air);
        sim.Run(CombatSystem.BurstLook);
        b = sim.Units.IndexOf(bug);
        Assert.True(combat.Bursting(b));
        Assert.False(sim.Units.Burrowed[b]);
        Assert.Equal(skitter.Damage * 150 / 100, combat.AttackOf(b, skitter));
        Assert.Equal(skitter.Damage, combat.AttackOf(sim.Units.IndexOf(surface), skitter)); // never went under
        sim.Run(CombatSystem.BurstSeconds * Tps);
        b = sim.Units.IndexOf(bug);
        Assert.False(combat.Bursting(b));
        Assert.Equal(skitter.Damage, combat.AttackOf(b, skitter));

        // Units without burst never get it, wherever they come up from.
        Assert.Equal(0, sim.Content.Unit("sapper").Burst);
    }

    /// <summary>
    /// A computer player goes by what its faction can build and train, so one playing the Deepfolk builds a brood
    /// chamber and hatches hive units, never a barracks or spearmen; and the match replays the same.
    /// </summary>
    [Fact]
    public void AComputerPlayerPlaysTheDeepfolkWithTheirOwnUnits()
    {
        int hive = 0, everyones = 0;
        bool brood = false, replaced = false;
        var result = AiMatch.Play(3, 256, 64, 256, 4 * 60 * Tps, levels: new[] { AiLevel.Normal }, fog: true,
            factions: new[] { "deepfolk", "settlers" }, watch: (sim, _) =>
            {
                if (sim.Tick % Tps != 0) return;
                hive = Math.Max(hive, Count(sim, 0, "skitterling") + Count(sim, 0, "spitter"));
                everyones += Count(sim, 0, "spearman") + Count(sim, 0, "archer") + Count(sim, 0, "sapper");
                brood |= sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Id == "brood_chamber");
                replaced |= sim.Buildings.All.Any(b => b.Owner == 0 && sim.FactionOf(0).Replaces(b.Def));
            });
        Assert.True(brood, "it built a brood chamber");
        Assert.False(replaced, "it built a barracks");
        Assert.Equal(0, everyones);
        Assert.True(hive > 16, $"it hatched more hive units than it started with ({hive})");
        var samples = MatchReplay.Run(CommandLog.Parse(result.Log.ToString()), 1200, out var replayed);
        var again = MatchReplay.Run(CommandLog.Parse(result.Log.ToString()), 1200, out _);
        Assert.Equal(samples.Select(h => h.Hash), again.Select(h => h.Hash));
        Assert.Equal("deepfolk", replayed.FactionOf(0).Id);
    }

    [Fact]
    public void ADeepfolkMatchPlaysTheSameTwice()
    {
        static ulong Play()
        {
            var w = TestWorlds.Flat(64, 24, 64, Ground);
            TestWorlds.Box(w, 10, Tunnel, 30, 20, Tunnel + 1, 30, BlockType.Air);
            var sim = TestWorlds.Sim(w);
            sim.SetFaction(0, "deepfolk");
            var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 2, 2)!;
            var brood = sim.Economy.PlaceFinished(0, sim.Content.Building("brood_chamber"), 30, 2)!;
            hall.Stock[(int)Good.Food] = 500;
            hall.Stock[(int)Good.Wood] = 500;
            int digger = sim.SpawnUnit(0, "burrower", 12, 30, Tunnel);
            sim.SpawnUnit(1, "spearman", 40, 30);
            sim.Schedule(0, new TrainCommand(0, brood.Id, "skitterling"));
            sim.Schedule(0, new TrainCommand(0, brood.Id, "spitter"));
            sim.Schedule(0, new TunnelCommand(0, new[] { digger }, 40, Ground + 1, 30));
            sim.Run(60 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
