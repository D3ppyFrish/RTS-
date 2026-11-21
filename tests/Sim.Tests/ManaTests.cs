using System;
using System.Collections.Generic;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Crystal and mana: crystal dug from the caverns, channelled into mana at a shrine, and spells that cost mana.</summary>
public class ManaTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Spawn(Simulation sim, int owner, string unit, int x, int z) => sim.SpawnUnit(owner, unit, x, z, Stand);

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

    private static void Wall(Simulation sim, int[] units, int x1, int z1, int x2, int z2)
    {
        sim.Schedule(sim.Tick, new EarthWallCommand(P, units, x1, z1, x2, z2));
        sim.Step();
    }

    [Fact]
    public void EveryPlayerStartsWithSomeMana()
    {
        var sim = Flat();
        Assert.Equal(EconomySystem.StartingMana, sim.Economy.Player(P).Mana);
        Assert.Equal(EconomySystem.StartingMana, sim.Economy.Player(1).Mana);
        Assert.True(EconomySystem.StartingMana >= SpellSystem.ManaCost(Spell.EarthWall), "a spell or two before the first shrine");
    }

    [Fact]
    public void DiggingCrystalGivesCrystalCarriedToStorage()
    {
        Assert.True(Harvest.Yields(BlockType.Crystal, out var good));
        Assert.Equal(Good.Crystal, good);
        Assert.True(Harvest.Repeats(BlockType.Crystal), "a worker moves on to the next crystal nearby");

        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        for (int x = 24; x < 27; x++) sim.World.Set(x, Stand, 30, BlockType.Crystal);
        int w = sim.SpawnUnit(P, "worker", 20, 30);
        sim.Schedule(0, new GatherCommand(P, new[] { w }, 24, Stand, 30));
        RunUntil(sim, () => store.Stock[(int)Good.Crystal] == 3, 6000, "three crystal stored");
        Assert.Equal(3, sim.Economy.Player(P).Gathered[(int)Good.Crystal]);
        Assert.Equal(BlockType.Air, sim.World.Get(25, Stand, 30));
    }

    [Fact]
    public void AShrineChannelsCrystalFromStorageIntoMana()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        store.Stock[(int)Good.Crystal] = 3;
        var shrine = Place(sim, P, "shrine", 24, 10);
        Assert.Equal(BuildingKind.Shrine, shrine.Def.Kind);
        int w = sim.SpawnUnit(P, "worker", 18, 18);
        sim.Schedule(0, new WorkCommand(P, new[] { w }, shrine.Id));
        int start = sim.Economy.Player(P).Mana;
        RunUntil(sim, () => sim.Economy.Player(P).Mana == start + 3 * shrine.Def.Mana, 4000, "three crystal channelled");
        Assert.Equal(0, store.Stock[(int)Good.Crystal]);
        Assert.Equal(0, shrine.Stock[(int)Good.Crystal]);
        Assert.Equal(3 * shrine.Def.Mana, sim.Economy.Player(P).ManaChannelled);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);
        // Nothing left to channel: the keeper waits by the shrine, and mana stays where it is.
        sim.Run(200);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, w)]);
        Assert.Equal(start + 3 * shrine.Def.Mana, sim.Economy.Player(P).Mana);
    }

    [Fact]
    public void AShrineTakesItsChannelTimeForEachCrystal()
    {
        var sim = Flat();
        var shrine = Place(sim, P, "shrine", 24, 10);
        shrine.Stock[(int)Good.Crystal] = 2;
        int start = sim.Economy.Player(P).Mana;
        sim.Step(); // the first crystal starts
        sim.Run(shrine.Def.ChannelTicks - 2);
        Assert.Equal(start, sim.Economy.Player(P).Mana);
        sim.Run(2);
        Assert.Equal(start + shrine.Def.Mana, sim.Economy.Player(P).Mana);
        Assert.Equal(0, shrine.Stock[(int)Good.Crystal]); // the second is being channelled
        sim.Run(shrine.Def.ChannelTicks);
        Assert.Equal(start + 2 * shrine.Def.Mana, sim.Economy.Player(P).Mana);
    }

    [Fact]
    public void TheCrystalSanctumChannelsTwiceAsFast()
    {
        var sim = Flat();
        var sanctum = Place(sim, P, "sanctum", 24, 10);
        Assert.Equal(sim.Content.Building("shrine").Upgrade, sanctum.Def);
        Assert.Equal(200, sanctum.Def.Speed);
        sanctum.Stock[(int)Good.Crystal] = 1;
        int start = sim.Economy.Player(P).Mana;
        sim.Step();
        sim.Run(sanctum.Def.ChannelTicks / 2);
        Assert.Equal(start + sanctum.Def.Mana, sim.Economy.Player(P).Mana);
    }

    [Fact]
    public void ASpellCostsItsManaAndWithTooLittleNothingIsCast()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        var p = sim.Economy.Player(P);
        p.Mana = SpellSystem.ManaCost(Spell.EarthWall) + 5;
        Wall(sim, new[] { mage }, 26, 27, 26, 33);
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(5, p.Mana);

        // Its wait over, it has the spell but not the mana: nothing rises, and it is not made to wait again.
        sim.Run(SpellSystem.CooldownTicks);
        Assert.True(sim.Spells.CanCast(Index(sim, mage)));
        Wall(sim, new[] { mage }, 30, 27, 30, 33);
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(5, p.Mana);
        Assert.True(sim.Spells.CanCast(Index(sim, mage)));
    }

    [Fact]
    public void AGroupSpellCostsManaForEachCasterAndTakesOnlyThoseItCovers()
    {
        var sim = Flat();
        var three = new int[3];
        for (int k = 0; k < 3; k++) three[k] = Spawn(sim, P, "mage", 28 + k, 22);
        var p = sim.Economy.Player(P);
        int cost = SpellSystem.ManaCost(Spell.EarthWall);
        p.Mana = 2 * cost + 1;
        Wall(sim, three, 20, 30, 45, 30);
        Assert.Single(sim.Spells.Walls);
        Assert.Equal(1, p.Mana);
        // Two cast (a wall two rows thick); the third is still ready.
        int ready = 0;
        foreach (int id in three)
            if (sim.Spells.CanCast(Index(sim, id))) ready++;
        Assert.Equal(1, ready);
        Assert.Equal(SpellSystem.WallLength(2) * SpellSystem.WallThickness(2) * SpellSystem.Height, sim.Spells.Walls[0].Blocks.Count);
    }

    [Fact]
    public void ManaSpentWhileTheGroupWalksLeavesOutTheCastersItNoLongerCovers()
    {
        var sim = Flat();
        int a = Spawn(sim, P, "mage", 26, 20), b = Spawn(sim, P, "mage", 26, 50);
        var p = sim.Economy.Player(P);
        int cost = SpellSystem.ManaCost(Spell.EarthWall);
        p.Mana = 2 * cost;
        // b walks into range of the line; a waits for it.
        Wall(sim, new[] { a, b }, 24, 22, 28, 22);
        Assert.Empty(sim.Spells.Walls);
        Assert.True(sim.Spells.Casting(a) && sim.Spells.Casting(b));
        p.Mana = cost; // spent elsewhere meanwhile
        RunUntil(sim, () => sim.Spells.Walls.Count == 1, 60 * Tps, "the wall raised");
        Assert.Equal(0, p.Mana);
        Assert.Equal(5 * SpellSystem.Height, sim.Spells.Walls[0].Blocks.Count); // one caster: one row
    }

    [Fact]
    public void ASpellThatTakesNothingCostsNothing()
    {
        var sim = Flat();
        int mage = Spawn(sim, P, "mage", 20, 30);
        var p = sim.Economy.Player(P);
        int start = p.Mana;
        // No water on this flat ground: an ice bridge freezes nothing.
        sim.Schedule(sim.Tick, new IceBridgeCommand(P, new[] { mage }, 24, 30, 30, 30));
        sim.Step();
        Assert.Empty(sim.Spells.IceBridges);
        Assert.Equal(start, p.Mana);
    }

    [Fact]
    public void ManaIsInTheHash()
    {
        var a = Flat();
        var b = Flat();
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Economy.Player(P).Mana += 1;
        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AnAutomaticWorkerKeepsAShrineOnlyOnceThereIsCrystal()
    {
        var sim = Flat();
        var store = Place(sim, P, "storehouse", 10, 10);
        var shrine = Place(sim, P, "shrine", 20, 10);
        TestWorlds.Tree(sim.World, 30, 30, 4);
        TestWorlds.Tree(sim.World, 36, 36, 4);
        int a = sim.SpawnUnit(P, "worker", 12, 20), b = sim.SpawnUnit(P, "worker", 13, 20);
        sim.Schedule(sim.Tick, new AutomateCommand(P, new[] { a }, true));
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[Index(sim, a)]); // no crystal: nothing to channel

        store.Stock[(int)Good.Crystal] = 2;
        sim.Schedule(sim.Tick, new AutomateCommand(P, new[] { b }, true));
        sim.Run(2 * Tps);
        Assert.Equal(UnitJob.Haul, sim.Units.Job[Index(sim, b)]);
        Assert.Equal(shrine.Id, sim.Units.JobTarget[Index(sim, b)]);
        int start = sim.Economy.Player(P).Mana;
        RunUntil(sim, () => sim.Economy.Player(P).Mana == start + 2 * shrine.Def.Mana, 60 * Tps, "the crystal channelled");
    }

    [Fact]
    public void AShrineNeedsItsManaAndChannelTimeAndNothingElseHasThem()
    {
        static Dictionary<string, string> With(string building)
        {
            var files = new Dictionary<string, string>();
            foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
            files["buildings.json"] = files["buildings.json"].TrimEnd().TrimEnd(']') + ", " + building + "]";
            return files;
        }
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(
            "{ \"id\": \"altar\", \"name\": \"Altar\", \"kind\": \"shrine\", \"size\": [3, 3], \"cost\": { \"stone\": 1 }, \"buildTime\": 5, \"workerSlots\": 1 }")));
        Assert.Contains("a shrine needs \"mana\"", e.Message);
        e = Assert.Throws<ContentException>(() => ContentLoader.Load(With(
            "{ \"id\": \"hut\", \"name\": \"Hut\", \"kind\": \"house\", \"size\": [3, 3], \"cost\": { \"wood\": 1 }, \"buildTime\": 5, \"mana\": 5 }")));
        Assert.Contains("only a shrine has \"mana\"", e.Message);
    }
}
