using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>The Awakening: the dark power under the Heartscar wakes late in the match (see <see cref="AwakeningSystem"/>).</summary>
public class AwakeningTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int P0 = 0, P1 = 1; // the Settlers and the Orcs
    private const int Tps = Simulation.TicksPerSecond;
    private const int Size = 96; // the heart lies under the middle, (48, 48)

    /// <summary>A flat world with no deep caves (so the heart is under the middle) and a start for each of two players.</summary>
    private static Simulation WithStarts(bool awakening = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(Size, 24, Size, Ground));
        sim.Map.Starts.Add(new StartArea(P0, 16, Ground + 1, 20));
        sim.Map.Starts.Add(new StartArea(P1, 80, Ground + 1, 20));
        sim.PlaceStartingBuildings();
        sim.Awakening = awakening;
        return sim;
    }

    private static Building Hall(Simulation sim, int player) =>
        sim.Buildings.All.First(b => b.Owner == player && b.Def.Kind == BuildingKind.TownHall);

    private static int Beasts(Simulation sim) =>
        Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == Simulation.WildPlayer && sim.Content.Units[sim.Units.Def[i]].Awakening > 0);

    [Fact]
    public void TheHeartLiesUnderTheMiddleWithoutDeepCaves()
    {
        var sim = WithStarts();
        Assert.Equal(Size / 2, sim.DarkPower.HeartX);
        Assert.Equal(Size / 2, sim.DarkPower.HeartZ);
        Assert.True(sim.DarkPower.HeartY < Ground);
    }

    [Fact]
    public void OnADeepMapTheHeartIsTheLowerChamberNearestTheMiddle()
    {
        var map = RegionGenerator.Generate(1234, RegionGenerator.DefaultSize, RegionGenerator.DefaultHeight, RegionGenerator.DefaultSize);
        Assert.NotEmpty(map.Deeps);
        var sim = new Simulation(map);
        var dp = sim.DarkPower;
        Assert.Contains((dp.HeartX, dp.HeartY, dp.HeartZ), map.Deeps);
        Assert.True(dp.HeartY <= sim.World.SizeY / 4, "the heart is on the deep caves' lower level");
        int c = sim.World.SizeX / 2;
        long d = (long)(dp.HeartX - c) * (dp.HeartX - c) + (long)(dp.HeartZ - c) * (dp.HeartZ - c);
        foreach (var n in map.Deeps.Where(n => n.Y <= sim.World.SizeY / 4))
            Assert.True((long)(n.X - c) * (n.X - c) + (long)(n.Z - c) * (n.Z - c) >= d);
    }

    [Fact]
    public void ItWarnsThenWakesOnTheClockAndTheCorruptionCreepsOut()
    {
        var sim = WithStarts();
        var dp = sim.DarkPower;
        Assert.Equal(0, dp.Stage);
        Assert.Equal(AwakeningSystem.FirstStageTicks, dp.TicksToNextStage);
        sim.Run((int)(AwakeningSystem.FirstStageTicks - AwakeningSystem.WarnTicks) + 1);
        Assert.Contains(sim.Events.All, e => e.Kind == GameEventKind.AwakeningNear && e.Player == P0);
        Assert.Equal(0, dp.Stage);
        Assert.False(dp.Corrupt(dp.HeartX, dp.HeartZ));
        sim.Run((int)(AwakeningSystem.FirstStageTicks - sim.Tick) + 1);
        Assert.Equal(1, dp.Stage);
        Assert.Contains(sim.Events.All, e => e.Kind == GameEventKind.AwakeningStage && e.Def == 1 && e.Player == P1);
        Assert.Equal(AwakeningSystem.StageGapTicks, dp.TicksToNextStage + 1);
        // It creeps out a column a second to the first stage's reach, and no further.
        int r = dp.Radius;
        sim.Run(5 * Tps);
        Assert.Equal(r + 5, dp.Radius);
        Assert.True(dp.Corrupt(dp.HeartX, dp.HeartZ));
        sim.Run(dp.ReachOf(1) * Tps);
        Assert.Equal(dp.ReachOf(1), dp.Radius);
        Assert.False(dp.Corrupt(dp.HeartX + dp.ReachOf(1) + AwakeningSystem.Ragged + 1, dp.HeartZ));
        Assert.True(dp.ReachOf(1) < dp.ReachOf(2) && dp.ReachOf(2) < dp.ReachOf(3));
    }

    [Fact]
    public void OffItNeverWakes()
    {
        var sim = WithStarts(awakening: false);
        sim.DarkPower.WakeTo(3);
        sim.Run(Tps);
        Assert.Equal(0, Beasts(sim));
        Assert.Empty(sim.DarkPower.Broods);
    }

    [Fact]
    public void DiggingNearTheHeartWakesItSooner()
    {
        var sim = WithStarts();
        var dp = sim.DarkPower;
        sim.Run(1);
        long before = dp.NextStageAt;
        // Three blocks dug by the heart, one far from it.
        sim.Schedule(sim.Tick, new DigCommand(P0, dp.HeartX, Ground, dp.HeartZ));
        sim.Schedule(sim.Tick, new DigCommand(P0, dp.HeartX + 10, Ground, dp.HeartZ));
        sim.Schedule(sim.Tick, new DigCommand(P0, dp.HeartX, Ground - 1, dp.HeartZ));
        sim.Schedule(sim.Tick, new DigCommand(P0, 2, Ground, 2));
        sim.Run(1);
        Assert.Equal(before - 3 * AwakeningSystem.UnrestTicks, dp.NextStageAt);
        Assert.Equal(3 * AwakeningSystem.UnrestTicks, dp.Unrest);
    }

    [Fact]
    public void CorruptedGroundHurtsPlayersUnitsButNotMonsters()
    {
        // No town halls, so no broods: only the ground.
        var sim = TestWorlds.Sim(TestWorlds.Flat(Size, 24, Size, Ground));
        sim.Awakening = true;
        var dp = sim.DarkPower;
        dp.WakeTo(1);
        // Workers do not go looking for fights, and the wolf is too far off to see them.
        int near = sim.SpawnUnit(P0, "worker", dp.HeartX + 2, dp.HeartZ + 2);
        int far = sim.SpawnUnit(P0, "worker", dp.HeartX + dp.ReachOf(1) + 10, dp.HeartZ);
        int wolf = sim.SpawnUnit(Simulation.WildPlayer, "wolf", dp.HeartX - 5, dp.HeartZ - 5);
        int hp = sim.Units.Hp[sim.Units.IndexOf(near)];
        sim.Run(1);
        Assert.Empty(dp.Broods);
        Assert.True(dp.OnCorruptGround(sim.Units.IndexOf(wolf)));
        sim.Run(2 * AwakeningSystem.CorruptInterval);
        Assert.True(sim.Units.IndexOf(near) >= 0);
        Assert.Equal(hp - 2 * AwakeningSystem.CorruptDamage, sim.Units.Hp[sim.Units.IndexOf(near)]);
        Assert.Equal(hp, sim.Units.Hp[sim.Units.IndexOf(far)]);
        int w = sim.Units.IndexOf(wolf);
        Assert.Equal(sim.Content.Unit("wolf").MaxHp, sim.Units.Hp[w]);
    }

    [Fact]
    public void EachStageSendsABroodAtEveryPlayersHall()
    {
        var sim = WithStarts();
        sim.DarkPower.WakeTo(1);
        sim.Run(1);
        Assert.Equal(2, sim.DarkPower.Broods.Count);
        foreach (var brood in sim.DarkPower.Broods)
        {
            Assert.Equal(Hall(sim, brood.Player).Id, brood.Building);
            Assert.Equal(4, brood.Beasts.Count);
            Assert.All(brood.Beasts, id => Assert.Equal("blight_hound", sim.Content.Units[sim.Units.Def[sim.Units.IndexOf(id)]].Id));
        }
        Assert.Contains(sim.Events.All, e => e.Kind == GameEventKind.BroodRising && e.Player == P0 && e.Def == 4);
        // With nobody to stop them they reach the hall and batter it.
        var hall = Hall(sim, P0);
        int health = hall.Health;
        sim.Run(60 * Tps);
        Assert.True(hall.Health < health, "the brood batters the hall");
    }

    [Fact]
    public void LaterStagesSendStrongerBeastsToo()
    {
        var sim = WithStarts();
        sim.DarkPower.WakeTo(3);
        sim.Run(1);
        var kinds = sim.DarkPower.Broods[0].Beasts.Select(id => sim.Content.Units[sim.Units.Def[sim.Units.IndexOf(id)]].Id).ToList();
        Assert.Equal(4, kinds.Count(k => k == "blight_hound"));
        Assert.Equal(2, kinds.Count(k => k == "blight_brute"));
        Assert.Equal(1, kinds.Count(k => k == "blight_horror"));
        Assert.Equal(-1, sim.DarkPower.TicksToNextStage);
    }

    [Fact]
    public void BroodsComeAgainUntilTooManyBeastsAreOut()
    {
        var sim = WithStarts();
        var dp = sim.DarkPower;
        // A ring of bedrock keeps the beasts in the Heartscar, away from the halls.
        for (int d = -16; d <= 16; d++)
            foreach (var (x, z) in new[] { (dp.HeartX + d, dp.HeartZ - 16), (dp.HeartX + d, dp.HeartZ + 16), (dp.HeartX - 16, dp.HeartZ + d), (dp.HeartX + 16, dp.HeartZ + d) })
                for (int y = Ground + 1; y <= Ground + 6; y++) sim.World.Set(x, y, z, BlockType.Bedrock);
        dp.WakeTo(3);
        sim.Run(1);
        Assert.Equal(14, Beasts(sim));
        sim.Run((int)AwakeningSystem.BroodTicks);
        Assert.Equal(28, Beasts(sim));
        sim.Run((int)AwakeningSystem.BroodTicks);
        Assert.Equal(42, Beasts(sim));
        // Now 21 are after each player, more than the most: no more come.
        sim.Run((int)AwakeningSystem.BroodTicks);
        Assert.Equal(42, Beasts(sim));
    }

    [Fact]
    public void KillingACorruptedBeastPaysItsBounty()
    {
        var sim = WithStarts();
        var hall = Hall(sim, P0);
        int crystal = hall.Stock[(int)Good.Crystal];
        int hound = sim.SpawnUnit(Simulation.WildPlayer, "blight_hound", 40, 60);
        for (int k = 0; k < 6; k++) sim.SpawnUnit(P0, "spearman", 38 + k, 58);
        sim.Run(30 * Tps);
        Assert.True(sim.Units.IndexOf(hound) < 0, "the spearmen kill the hound");
        Assert.Equal(crystal + 2, hall.Stock[(int)Good.Crystal]);
    }

    [Fact]
    public void TheBeastsAreDataWithAStageEach()
    {
        var db = ContentDb.Default;
        Assert.Equal(new[] { 1, 2, 3 }, new[] { "blight_hound", "blight_brute", "blight_horror" }.Select(id => db.Unit(id).Awakening));
        foreach (var id in new[] { "blight_hound", "blight_brute", "blight_horror" })
        {
            var u = db.Unit(id);
            Assert.Equal(UnitRole.Monster, u.Role);
            Assert.Null(u.Lair);
            Assert.True(u.Brood > 0);
            Assert.Contains(u.Bounty, g => g.Good == Good.Crystal);
        }
    }

    [Theory]
    [InlineData("\"awakening\": 1, \"brood\": 4,", "\"brood\": 4,")] // a stage without a brood
    [InlineData("\"awakening\": 1, \"brood\": 4,", "\"awakening\": 1, \"brood\": 4, \"cost\": { \"food\": 5 },")] // trained
    [InlineData("\"role\": \"monster\",\n    \"description\": \"A hound", "\"role\": \"cavalry\",\n    \"description\": \"A hound")] // not a monster
    public void TheLoaderChecksTheBeasts(string old, string replacement)
    {
        var files = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        string text = files["units.json"];
        int at = text.IndexOf(old, System.StringComparison.Ordinal);
        Assert.True(at >= 0, old);
        files["units.json"] = text[..at] + replacement + text[(at + old.Length)..];
        Assert.Throws<ContentException>(() => ContentLoader.Load(files));
    }

    [Fact]
    public void TheOptionIsInTheReplayHeaderAndReplaysTheSame()
    {
        var log = CommandLog.UsualStart(77, 256, 64, 256, awakening: true);
        var text = log.ToString();
        Assert.Contains("awakening 1", text);
        var read = CommandLog.Parse(text);
        Assert.True(read.Awakening);
        ulong Play()
        {
            var sim = read.CreateSimulation();
            Assert.True(sim.Awakening);
            sim.DarkPower.WakeTo(2);
            sim.Run(1);
            Assert.NotEmpty(sim.DarkPower.Broods);
            // The brood fights the starting units.
            sim.Run(40 * Tps);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
        Assert.DoesNotContain("awakening", CommandLog.UsualStart(77, 256, 64, 256).ToString());
    }
}
