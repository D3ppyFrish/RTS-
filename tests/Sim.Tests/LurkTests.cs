using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The design doc's bog horrors that "ambush from water": a lurking lair's pack lies in the shallow pools by its den,
/// unseen until a unit comes close, and the first blow each strikes coming up out of the water is twice as hard.
/// </summary>
public class LurkTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4 (at 3 in a pool)
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0;

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.Fail($"Not done after {maxTicks} ticks: {what}");
    }

    /// <summary>
    /// A flat world with a pool one block deep from column 58 to 60 and row 44 to 52, and a bog horror lair with its
    /// den on the grass at (50, 48), 8 columns from the water.
    /// </summary>
    private static (Simulation Sim, Lair Lair) Swamp(bool fog = false)
    {
        var w = TestWorlds.Flat(96, 16, 96, Ground);
        TestWorlds.Box(w, 58, Ground, 44, 60, Ground, 52, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        sim.Fog = fog;
        var lair = sim.Monsters.Place(sim.Content.Unit("bog_horror"), 50, 48)!;
        return (sim, lair);
    }

    [Fact]
    public void ABogHorrorPairLiesInThePoolNearestItsDen()
    {
        var (sim, lair) = Swamp();
        Assert.Equal(new[] { (58, 3, 48), (58, 3, 46) }, lair.Pools);
        Assert.Equal(2, lair.Pack.Count);
        for (int k = 0; k < 2; k++)
        {
            int i = Index(sim, lair.Pack[k]);
            Assert.Equal((lair.Pools[k].X, lair.Pools[k].Z), (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt()));
            Assert.True(sim.Monsters.Submerged(i));
        }
        // Left alone, they stay in the water rather than walking up to the den.
        for (int t = 0; t < 10 * Tps; t++) sim.Step();
        foreach (int id in lair.Pack) Assert.True(sim.Monsters.Submerged(Index(sim, id)));

        // A lair with no water near its den waits on the bank as any other does.
        var dry = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        var bank = dry.Monsters.Place(dry.Content.Unit("bog_horror"), 50, 48)!;
        Assert.Empty(bank.Pools);
        Assert.Equal(2, bank.Pack.Count);
        Assert.False(dry.Monsters.Submerged(Index(dry, bank.Pack[0])));
    }

    /// <summary>
    /// With fog of war on, a horror lying in the water is not seen from 11 columns off, in plain sight, by a pathfinder
    /// (sight 14) standing out of the horror's own sight, and the pathfinder does not go after it; the player spots
    /// such a monster only within 3 columns of one of their units. With fog of war off it is seen anyway.
    /// </summary>
    [Fact]
    public void AHorrorInTheWaterIsSeenOnlyFromCloseBy()
    {
        var (sim, lair) = Swamp(fog: true);
        int horror = lair.Pack[0]; // in the pool at (58, 48)
        int pathfinder = sim.SpawnUnit(Settlers, "pathfinder", 69, 48);
        for (int t = 0; t < 3 * Tps; t++) sim.Step();
        int h = Index(sim, horror);
        Assert.True(sim.Vision.Sees(Settlers, 58, 48));
        Assert.True(sim.Monsters.Submerged(h));
        Assert.False(sim.Vision.SeesUnit(Settlers, h));
        Assert.Equal(0, sim.Units.Target[Index(sim, pathfinder)]);
        // Spotting it takes one of the player's units within 3 columns of it.
        Assert.True(sim.Vision.Close(Settlers, 69, 48));
        Assert.True(sim.Vision.Close(Settlers, 66, 48));
        Assert.False(sim.Vision.Close(Settlers, 65, 48));
        Assert.False(sim.Vision.Close(Settlers, 58, 48));

        var (open, openLair) = Swamp(fog: false);
        open.SpawnUnit(Settlers, "pathfinder", 69, 48);
        open.Step();
        Assert.True(open.Vision.SeesUnit(Settlers, Index(open, openLair.Pack[0])));
    }

    /// <summary>
    /// A worker wading in comes under attack: each horror's first blow out of the water is twice its usual damage, the
    /// next blows are not; once the worker is dead the horrors go back and lie in their pools again.
    /// </summary>
    [Fact]
    public void TheFirstBlowOutOfTheWaterIsTwiceAsHardAndThenTheyLieInWaitAgain()
    {
        var (sim, lair) = Swamp();
        int worker = sim.SpawnUnit(Settlers, "worker", 54, 48);
        var u = sim.Units;
        u.Hp[Index(sim, worker)] = 10_000; // lives through a few blows of each
        var first = new Dictionary<int, (int Dealt, int Usual)>();
        var later = new List<(int Dealt, int Usual)>();
        int beforeHash = 0;
        RunUntil(sim, () =>
        {
            foreach (var shot in sim.Combat.Shots)
            {
                if (shot.Tick != sim.Tick - 1 || !lair.Pack.Contains(shot.Attacker) || shot.Part is not { } part) continue;
                int a = Index(sim, shot.Attacker), t = Index(sim, worker);
                int usual = sim.Combat.HitDamage(a, sim.Content.Units[u.Def[a]], t, part);
                if (first.ContainsKey(shot.Attacker)) later.Add((shot.Damage, usual));
                else first[shot.Attacker] = (shot.Damage, usual);
            }
            beforeHash++;
            return first.Count == 2 && later.Count >= 2;
        }, 20 * Tps, "both horrors strike twice");
        foreach (var (dealt, usual) in first.Values) Assert.Equal(usual * 2, dealt);
        foreach (var (dealt, usual) in later) Assert.Equal(usual, dealt);
        foreach (int id in lair.Pack) Assert.False(sim.Monsters.Ambushing(Index(sim, id)));

        u.Hp[Index(sim, worker)] = 1;
        RunUntil(sim, () => !u.Exists(worker), 10 * Tps, "the horrors kill the worker");
        RunUntil(sim, () => lair.Pack.All(id => sim.Monsters.Submerged(Index(sim, id))
            && (u.X[Index(sim, id)].FloorToInt(), u.Z[Index(sim, id)].FloorToInt()) == (lair.Pools[lair.Pack.ToList().IndexOf(id)].X, lair.Pools[lair.Pack.ToList().IndexOf(id)].Z)),
            30 * Tps, "the horrors go back to their pools");
    }

    /// <summary>A horror walking on dry ground when it meets someone springs no ambush: its first blow is as usual.</summary>
    [Fact]
    public void AHorrorOnTheBankStrikesAsUsual()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        var lair = sim.Monsters.Place(sim.Content.Unit("bog_horror"), 50, 48)!;
        int worker = sim.SpawnUnit(Settlers, "worker", 54, 48);
        sim.Units.Hp[Index(sim, worker)] = 10_000;
        Shot? hit = null;
        RunUntil(sim, () =>
        {
            hit = sim.Combat.Shots.Where(s => lair.Pack.Contains(s.Attacker) && s.Part != null).Cast<Shot?>().FirstOrDefault();
            return hit != null;
        }, 10 * Tps, "a horror strikes");
        int a = Index(sim, hit!.Value.Attacker);
        Assert.Equal(sim.Combat.HitDamage(a, sim.Content.Units[sim.Units.Def[a]], Index(sim, worker), hit.Value.Part!.Value), hit.Value.Damage);
    }

    /// <summary>On the default map the swamp's bog horrors start in its pools, and the same seed lays them out the same.</summary>
    [Fact]
    public void TheDefaultMapsBogHorrorsLieInTheSwampPools()
    {
        static Simulation Match()
        {
            var sim = new Simulation(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
            sim.PlaceStartingBuildings();
            sim.SpawnStartingUnits(24);
            sim.SpawnLairs();
            sim.SpawnCities();
            return sim;
        }
        var sim = Match();
        var horrors = sim.Monsters.Lairs.Where(l => l.Monster.Id == "bog_horror").ToList();
        Assert.NotEmpty(horrors);
        Assert.Contains(horrors, l => l.Pools.Count == 2);
        foreach (var lair in horrors.Where(l => l.Pools.Count == 2))
            foreach (int id in lair.Pack) Assert.True(sim.Monsters.Submerged(Index(sim, id)));
        var again = Match();
        Assert.Equal(horrors.Select(l => l.Pools.ToArray()), again.Monsters.Lairs.Where(l => l.Monster.Id == "bog_horror").Select(l => l.Pools.ToArray()));
        Assert.Equal(sim.StateHash(), again.StateHash());
    }

    [Fact]
    public void AmbushNeedsLurkAndALurkingLairDoesNotRoam()
    {
        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        string text = files["units.json"];
        text = text.Replace("\"lurk\": true, \"ambush\": 100,", "\"ambush\": 100,");
        text = text.Replace("\"atStarts\": true, \"roam\": 32,", "\"atStarts\": true, \"roam\": 32, \"lurk\": true,");
        files["units.json"] = text;
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("unit 'bog_horror', lair") && m.Contains("\"ambush\" needs \"lurk\""));
        Assert.Contains(e.Errors, m => m.Contains("unit 'wolf', lair") && m.Contains("does not \"roam\""));
    }
}
