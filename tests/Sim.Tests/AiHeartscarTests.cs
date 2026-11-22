using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Economy;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Computer players and the Heartscar (see <see cref="AwakeningSystem"/>): the smart levels dig nothing near the heart,
/// keep off the corrupted ground once the dark power wakes, and walk their units off it.
/// </summary>
public class AiHeartscarTests
{
    private const int Ground = 3;
    private const int Deep = 16; // ground deep enough for a quarry
    private const int Tps = Simulation.TicksPerSecond;
    private const int Size = 96; // the heart lies under the middle, (48, 48)

    private static Simulation WithStarts(bool awakening = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(Size, 24, Size, Ground));
        sim.Map.Starts.Add(new StartArea(0, 12, Ground + 1, 20));
        sim.Map.Starts.Add(new StartArea(1, 84, Ground + 1, 20));
        sim.PlaceStartingBuildings();
        sim.Awakening = awakening;
        return sim;
    }

    /// <summary>Kills every corrupted beast out (test mode), so that nothing fights on the Heartscar.</summary>
    private static void KillBeasts(Simulation sim)
    {
        var u = sim.Units;
        var beasts = Enumerable.Range(0, u.Count)
            .Where(i => u.Owner[i] == Simulation.WildPlayer && sim.Content.Units[u.Def[i]].Awakening > 0).Select(i => u.Id[i]).ToArray();
        if (beasts.Length == 0) return;
        sim.Schedule(sim.Tick, new SandboxUnitsCommand(0, beasts, SandboxAction.Kill));
        sim.Step();
    }

    [Theory]
    [InlineData(AiLevel.Classic)]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void ItKeepsOffTheCorruptedGroundOnlyOnceTheDarkPowerWakesOrIsAboutTo(AiLevel level)
    {
        var sim = WithStarts();
        var ai = new AiPlayer(0, 1, level);
        var dp = sim.DarkPower;
        Assert.False(ai.KeepsOff(sim, dp.HeartX, dp.HeartZ), "asleep, it keeps off nothing");
        Assert.Equal(0, ai.ScarReach(sim));

        dp.WakeTo(1);
        bool smart = level != AiLevel.Classic;
        Assert.Equal(smart, ai.KeepsOff(sim, dp.HeartX, dp.HeartZ));
        if (!smart) return;
        // The first stage's reach, its ragged edge and a margin; past that, ground it uses.
        int reach = dp.ReachOf(1) + AwakeningSystem.Ragged + AiPlayer.ScarMargin;
        Assert.Equal(reach, ai.ScarReach(sim));
        Assert.True(ai.KeepsOff(sim, dp.HeartX + reach, dp.HeartZ));
        Assert.False(ai.KeepsOff(sim, dp.HeartX + reach + 1, dp.HeartZ));
        // Corrupted ground is all inside it.
        for (int x = 0; x < Size; x++)
            if (dp.Corrupt(x, dp.HeartZ)) Assert.True(ai.KeepsOff(sim, x, dp.HeartZ));

        // In the warning before the next stage, it keeps off what that stage will reach.
        var next = WithStarts();
        next.DarkPower.WakeTo(1);
        next.Run((int)(AwakeningSystem.StageGapTicks - AwakeningSystem.WarnTicks) + 1);
        Assert.Equal(1, next.DarkPower.Stage);
        Assert.Equal(next.DarkPower.ReachOf(2) + AwakeningSystem.Ragged + AiPlayer.ScarMargin, ai.ScarReach(next));

        // With the Awakening off, it keeps off nothing.
        var off = WithStarts(awakening: false);
        off.DarkPower.WakeTo(1);
        Assert.False(ai.KeepsOff(off, off.DarkPower.HeartX, off.DarkPower.HeartZ));
    }

    [Theory]
    [InlineData(AiLevel.Classic)]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void ItWalksItsUnitsThatStandOnTheCorruptedGroundOutOfIt(AiLevel level)
    {
        var sim = WithStarts();
        sim.TestMode = true;
        var dp = sim.DarkPower;
        dp.WakeTo(1);
        sim.Step(); // the broods rise
        KillBeasts(sim);
        var ids = new List<int>
        {
            sim.SpawnUnit(0, "spearman", dp.HeartX, dp.HeartZ),
            sim.SpawnUnit(0, "archer", dp.HeartX + 3, dp.HeartZ - 2),
            sim.SpawnUnit(0, "worker", dp.HeartX - 2, dp.HeartZ + 3),
        };
        var u = sim.Units;
        Assert.All(ids, id => Assert.True(dp.OnCorruptGround(u.IndexOf(id))));
        var ai = new AiPlayer(0, 1, level);
        for (int t = 0; t < 30 * Tps; t++)
        {
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
        }
        if (level == AiLevel.Classic)
        {
            Assert.Equal(0, ai.LeftScar);
            Assert.DoesNotContain(ai.Journal, j => j.Text.Contains("off the corrupted ground"));
            return;
        }
        Assert.True(ai.LeftScar >= ids.Count, $"walked {ai.LeftScar} off");
        Assert.Contains(ai.Journal, j => j.Text.Contains("off the corrupted ground"));
        foreach (int id in ids)
        {
            int i = u.IndexOf(id);
            Assert.True(i >= 0, "it lived");
            Assert.False(dp.OnCorruptGround(i), $"unit {id} is still on the corrupted ground");
        }
    }

    /// <summary>A Hard town 36 columns from the heart, under the middle of the map, with workers and goods to quarry.</summary>
    private static (Simulation Sim, Building Hall) HardTown(bool awakening)
    {
        var sim = new Simulation(new RegionMap(1, TestWorlds.Flat(192, 40, 192, Deep)));
        sim.Map.Starts.Add(new StartArea(0, 60, Deep + 1, 96));
        sim.Map.Starts.Add(new StartArea(1, 170, Deep + 1, 96));
        sim.PlaceStartingBuildings();
        sim.Awakening = awakening;
        var hall = sim.Economy.TownHall(0)!;
        sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), hall.X - 9, hall.Z - 9);
        for (int k = 0; k < 20; k++) sim.SpawnUnit(0, "worker", hall.X + k % 7, hall.MaxZ + 2 + k / 7);
        hall.Stock[(int)Good.Food] = 600;
        hall.Stock[(int)Good.Wood] = 2000;
        hall.Stock[(int)Good.Stone] = 0;
        return (sim, hall);
    }

    [Fact]
    public void HardQuarriesOutOfTheHeartsReach()
    {
        foreach (bool awakening in new[] { false, true })
        {
            var (sim, _) = HardTown(awakening);
            var ai = new AiPlayer(0, 1, AiLevel.Hard);
            var digs = new List<DigAreaCommand>();
            for (int t = 0; t < 90 * Tps; t++)
            {
                ai.Think(sim, c =>
                {
                    if (c is DigAreaCommand d) digs.Add(d);
                    sim.Schedule(sim.Tick, c);
                });
                sim.Step();
            }
            if (!awakening)
            {
                Assert.True(ai.QuarryArea is not null, "with the dark power out of the match, it quarries by its town");
                Assert.NotEmpty(digs);
                // The same quarry would wake the dark power sooner: near the heart, and dug deep enough.
                var dp = sim.DarkPower;
                var d0 = digs[0];
                Assert.True(d0.Y <= dp.HeartY + AwakeningSystem.HeartHeight);
                int nx = Math.Clamp(dp.HeartX, d0.X1, d0.X2), nz = Math.Clamp(dp.HeartZ, d0.Z1, d0.Z2);
                Assert.True(IntMath.Distance(nx, nz, dp.HeartX, dp.HeartZ) <= AwakeningSystem.HeartReach);
                continue;
            }
            // It quarries further off instead, out of the heart's reach, and has not stirred it.
            Assert.NotEmpty(digs);
            foreach (var d in digs)
            {
                Assert.False(ai.NearHeart(sim, d.X1, d.Z1, d.X2, d.Z2, d.Y), $"a dig area at {d.X1},{d.Z1} to {d.X2},{d.Z2}");
                int nx = Math.Clamp(sim.DarkPower.HeartX, d.X1, d.X2), nz = Math.Clamp(sim.DarkPower.HeartZ, d.Z1, d.Z2);
                Assert.True(IntMath.Distance(nx, nz, sim.DarkPower.HeartX, sim.DarkPower.HeartZ) > AwakeningSystem.HeartReach);
            }
            Assert.Equal(0, sim.DarkPower.Unrest);
        }
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Hard)]
    public void ItSearchesNoRuinOnTheCorruptedGround(AiLevel level)
    {
        // A town in the west of a 256 by 256 map, the heart under its middle, a ruin by the heart and one further off.
        var sim = TestWorlds.Sim(TestWorlds.Flat(256, 16, 256, Ground));
        sim.TestMode = true;
        sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 128));
        sim.Map.Ruins.Add((120, Ground, 128));
        sim.Map.Ruins.Add((134, Ground, 200));
        sim.Ruins.Place();
        sim.PlaceStartingBuildings();
        sim.Awakening = true;
        var hall = sim.Economy.TownHall(0)!;
        for (int k = 0; k < 8; k++) sim.SpawnUnit(0, "spearman", 20 + k, hall.MaxZ + 2);
        sim.DarkPower.WakeTo(1);
        var ai = new AiPlayer(0, 1, level);
        Assert.True(ai.KeepsOff(sim, 120, 128));
        Assert.False(ai.KeepsOff(sim, 134, 200));

        var targets = new HashSet<int>();
        for (int t = 0; t < AiPlayer.RuinTicks + 30 * Tps; t++)
        {
            KillBeasts(sim);
            ai.Think(sim, c => sim.Schedule(sim.Tick, c));
            sim.Step();
            if (ai.RuinTarget >= 0) targets.Add(ai.RuinTarget);
        }
        Assert.DoesNotContain(sim.Ruins.Ruins[0].Index, targets);
        Assert.Contains(sim.Ruins.Ruins[1].Index, targets);
    }
}
