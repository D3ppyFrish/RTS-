using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Day, night, weather and light, and what they do to sight and accuracy (see <see cref="Conditions"/>).</summary>
public class ConditionsTests
{
    private const int Ground = 3;
    /// <summary>The first tick of midnight: 16 hours after the 8:00 start.</summary>
    private const long Midnight = (long)(1440 - Conditions.StartMinute) * Conditions.DayTicks / 1440;

    /// <summary>A flat world; with day and night, clear weather for two days unless <paramref name="weather"/> says otherwise.</summary>
    private static Simulation Flat(bool dayNight = true, bool fog = true, Weather weather = Weather.Clear)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        sim.Fog = fog;
        sim.DayNight = dayNight;
        sim.Conditions.SetWeather(weather, 2L * Conditions.DayTicks);
        return sim;
    }

    private static void RunTo(Simulation sim, long tick)
    {
        while (sim.Tick < tick) sim.Step();
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    [Fact]
    public void TheClockRunsFromEightInTheMorningAroundTheDay()
    {
        Assert.Equal(8 * 60, Conditions.MinuteAt(0));
        Assert.Equal(DayPart.Day, Conditions.PartAt(Conditions.MinuteAt(0)));
        Assert.Equal(20 * 60, Conditions.MinuteAt(Conditions.DayTicks / 2));
        Assert.Equal(DayPart.Twilight, Conditions.PartAt(20 * 60));
        Assert.Equal(0, Conditions.MinuteAt(Midnight));
        Assert.Equal(DayPart.Night, Conditions.PartAt(0));
        Assert.Equal(DayPart.Twilight, Conditions.PartAt(6 * 60));
        Assert.Equal(8 * 60, Conditions.MinuteAt(Conditions.DayTicks)); // a day later
    }

    [Fact]
    public void TheWeatherRollsInAndOutAsTheMatchGoesOn()
    {
        var sim = new Simulation(TestWorlds.Sim(TestWorlds.Flat(16, 8, 16)).Map) { DayNight = true };
        Assert.Equal(Weather.Clear, sim.Conditions.Weather);
        Assert.Equal(Conditions.FirstSpellHours * Conditions.HourTicks, sim.Conditions.ChangesAt);
        var seen = new System.Collections.Generic.SortedSet<Weather>();
        int changes = 0;
        var last = sim.Conditions.Weather;
        for (int day = 0; day < 4; day++)
        for (int t = 0; t < Conditions.DayTicks; t++)
        {
            long changesAt = sim.Conditions.ChangesAt;
            var next = sim.Conditions.Next;
            sim.Step();
            var w = sim.Conditions.Weather;
            seen.Add(w);
            if (sim.Tick - 1 == changesAt) Assert.Equal(next, w); // what was coming is what came
            if (w != last) changes++;
            last = w;
            if (w == Weather.Fog) Assert.InRange(sim.Conditions.Minute, 3 * 60, 11 * 60);
            Assert.InRange(sim.Conditions.ChangesAt - sim.Tick, 0, Conditions.MaxSpellHours * Conditions.HourTicks);
        }
        Assert.True(changes >= 4, $"only {changes} changes of weather in four days");
        Assert.True(seen.Count >= 3);
    }

    [Fact]
    public void WhatWeatherComesNextLeansOnTheWeatherNow()
    {
        int Count(Weather now, Weather want, int minute) =>
            Enumerable.Range(0, 100).Count(roll => Conditions.Follow(now, roll, minute) == want);
        Assert.Equal(0, Count(Weather.Clear, Weather.Storm, 600)); // no storm out of a clear sky
        Assert.True(Count(Weather.Rain, Weather.Storm, 600) > 0);
        Assert.True(Count(Weather.Storm, Weather.Rain, 600) > Count(Weather.Storm, Weather.Clear, 600));
        Assert.True(Count(Weather.Clear, Weather.Fog, 5 * 60) > 0);
        Assert.Equal(0, Count(Weather.Clear, Weather.Fog, 14 * 60)); // fog only in the early morning
        foreach (var w in Enum.GetValues<Weather>())
            Assert.Equal(100, Enum.GetValues<Weather>().Sum(n => Count(w, n, 5 * 60)));
    }

    [Fact]
    public void TheSameMatchHasTheSameWeatherButMatchesDiffer()
    {
        string Play(int units)
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(32, 8, 32));
            sim.DayNight = true;
            for (int k = 0; k < units; k++) sim.SpawnUnit(0, "worker", 4 + k, 4);
            var sb = new System.Text.StringBuilder();
            for (int t = 0; t < 2 * Conditions.DayTicks; t += Conditions.HourTicks)
            {
                sim.Run(Conditions.HourTicks);
                sb.Append((int)sim.Conditions.Weather);
            }
            return sb.ToString();
        }
        Assert.Equal(Play(1), Play(1));
        Assert.True(Enumerable.Range(2, 6).Any(n => Play(n) != Play(1)));
    }

    [Fact]
    public void AStormPutsOutTorchesButNotLanterns()
    {
        var sim = Flat(weather: Weather.Storm);
        sim.DayStart = 0;
        Place(sim, 1, "torch", 20, 20);
        Place(sim, 1, "house", 40, 40);
        sim.Run(Lights.UpdateTicks);
        Assert.Equal(DayPart.Night, sim.Conditions.Part);
        Assert.False(sim.Conditions.Lights.Lit(20, 20));
        Assert.True(sim.Conditions.Lights.Lit(42, 42));
        // When the storm passes the torch burns again.
        sim.Conditions.SetWeather(Weather.Rain, Conditions.HourTicks);
        sim.Run(Lights.UpdateTicks);
        Assert.True(sim.Conditions.Lights.Lit(20, 20));
    }

    [Fact]
    public void LavaLightsTheGroundAroundItAndNothingStandsOnIt()
    {
        var w = TestWorlds.Flat(48, 12, 48, Ground);
        w.Set(20, Ground, 20, BlockType.Lava);
        var sim = TestWorlds.Sim(w);
        sim.DayNight = true;
        sim.DayStart = 0;
        sim.Conditions.SetWeather(Weather.Clear, Conditions.DayTicks);
        sim.Run(Lights.UpdateTicks);
        Assert.True(sim.Conditions.Lights.Lit(20, 20));
        Assert.True(sim.Conditions.Lights.Lit(20 + Lights.LavaLight, 20));
        Assert.False(sim.Conditions.Lights.Lit(20 + Lights.LavaLight + 1, 20));
        Assert.False(Units.Walk.CanStand(w, 20, Ground + 1, 20));
        Assert.True(Units.Walk.CanStand(w, 21, Ground + 1, 20));
        Assert.False(BlockInfo.IsDiggable(BlockType.Lava));
    }

    [Fact]
    public void GeneratedMapsHaveLavaInTheHills()
    {
        var map = RegionGenerator.Generate(1234, 256, 64, 256);
        int lava = 0;
        for (int z = 0; z < 256; z++)
        for (int x = 0; x < 256; x++)
            for (int y = 63; y >= 1; y--)
            {
                var t = map.World.Get(x, y, z);
                if (t == BlockType.Air) continue;
                if (t == BlockType.Lava)
                {
                    lava++;
                    Assert.Equal(Biome.Hills, map.BiomeAt(x, z));
                }
                break;
            }
        Assert.True(lava > 0);
        // Well away from the starts.
        foreach (var s in map.Starts)
            for (int dz = -RegionGenerator.StartResourceRadius; dz <= RegionGenerator.StartResourceRadius; dz++)
            for (int dx = -RegionGenerator.StartResourceRadius; dx <= RegionGenerator.StartResourceRadius; dx++)
            {
                int x = s.X + dx, z = s.Z + dz;
                if (x < 0 || z < 0 || x >= 256 || z >= 256) continue;
                for (int y = 1; y < 64; y++) Assert.NotEqual(BlockType.Lava, map.World.Get(x, y, z));
            }
    }

    [Fact]
    public void WithDayAndNightOffNothingChanges()
    {
        var sim = Flat(dayNight: false);
        RunTo(sim, Midnight);
        Assert.Equal(Weather.Clear, sim.Conditions.Weather);
        Assert.Equal(DayPart.Day, sim.Conditions.Part);
        Assert.Equal(100, sim.Conditions.SightPercent(10, 10, false));
        Assert.Equal(100, sim.Conditions.HitChance(10, 10));
    }

    [Fact]
    public void UnitsSeeLessFarAtNight()
    {
        var sim = Flat();
        RunTo(sim, Midnight);
        Assert.Equal(DayPart.Night, sim.Conditions.Part);
        Assert.Equal(Conditions.NightSight, sim.Conditions.SightPercent(20, 20, false));
        sim.SpawnUnit(0, "spearman", 20, 20); // sight 8: by night 4.8
        sim.Run(VisionSystem.UpdateTicks);
        Assert.Equal(Sight.Visible, sim.Vision.At(0, 24, 20));
        Assert.Equal(Sight.Unseen, sim.Vision.At(0, 25, 20));
        Assert.Equal(Sight.Unseen, sim.Vision.At(0, 28, 20)); // seen by day
    }

    [Fact]
    public void BadWeatherShortensSightAndStacksWithTheDark()
    {
        Assert.Equal(Conditions.RainSight, Conditions.WeatherSight(Weather.Rain));
        var sim = Flat(weather: Weather.Storm);
        sim.Step();
        Assert.Equal(Weather.Storm, sim.Conditions.Weather);
        Assert.Equal(Conditions.StormSight, sim.Conditions.SightPercent(20, 20, false));
        Assert.Equal(100 - Conditions.StormMiss, sim.Conditions.HitChance(20, 20));
        // Never below the least sight, however dark and wet.
        Assert.True(Conditions.FogSight * Conditions.NightSight / 100 * Conditions.MistSight / 100 < Conditions.MinSight);
    }

    [Fact]
    public void TheSwampsMistShortensSightAtNightAndInTheMorning()
    {
        var sim = Flat();
        for (int x = 40; x < 60; x++)
        for (int z = 40; z < 60; z++)
            sim.Map.SetBiome(x, z, Biome.Swamp);
        sim.Step();
        // 8:00: the mist still lies in the swamp, not outside it.
        Assert.True(sim.Conditions.InMist(50, 50));
        Assert.False(sim.Conditions.InMist(20, 20));
        Assert.Equal(Conditions.MistSight, sim.Conditions.SightPercent(50, 50, false));
        Assert.Equal(100, sim.Conditions.SightPercent(20, 20, false));
        // By 10:00 it has lifted.
        RunTo(sim, 2L * Conditions.DayTicks / 24);
        Assert.False(sim.Conditions.InMist(50, 50));
    }

    [Fact]
    public void LightsShineFromDuskToDawnAndLetUnitsSeeFarIntoTheLight()
    {
        var sim = Flat();
        var torch = Place(sim, 1, "torch", 26, 20); // player 1's torch, 6 columns east of the scout
        Assert.Equal(7, torch.Def.Light);
        sim.Run(VisionSystem.UpdateTicks);
        Assert.False(sim.Conditions.Lights.Lit(26, 20)); // not by day
        RunTo(sim, Midnight);
        Assert.True(sim.Conditions.Lights.Lit(26, 20));
        Assert.True(sim.Conditions.Lights.Lit(33, 20));
        Assert.False(sim.Conditions.Lights.Lit(34, 20));

        sim.SpawnUnit(0, "spearman", 20, 20);
        sim.Run(VisionSystem.UpdateTicks);
        // Into the dark it sees 4.8 columns, but the lit ground as far as by day (8 columns).
        Assert.Equal(Sight.Visible, sim.Vision.At(0, 28, 20));
        Assert.Equal(Sight.Unseen, sim.Vision.At(0, 20, 28)); // the same distance north, unlit
        Assert.Equal(100, sim.Conditions.HitChance(28, 20));
        Assert.Equal(100 - Conditions.NightMiss, sim.Conditions.HitChance(20, 28));
    }

    [Fact]
    public void TorchesAndCampfiresAreSolidPostsThatAreBuilt()
    {
        var db = Content.ContentDb.Default;
        var torch = db.Building("torch");
        Assert.Equal(BuildingKind.Light, torch.Kind);
        Assert.Equal(2, torch.Blocks.Count);
        Assert.All(torch.Blocks, b => Assert.Equal(BlockType.Planks, b.Block));
        var fire = db.Building("campfire");
        Assert.Equal(9, fire.Light);
        Assert.Single(fire.Blocks);
        Assert.True(db.Building("house").Light > 0);
        Assert.Equal(0, db.Building("wall").Light);
    }

    /// <summary>Archers at night, out of the light: some shots miss. With day and night off, none do.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RangedShotsMissInTheDark(bool dayNight)
    {
        var sim = Flat(dayNight, fog: false);
        RunTo(sim, Midnight);
        for (int k = 0; k < 4; k++) sim.SpawnUnit(0, "archer", 20 + k, 20);
        for (int k = 0; k < 4; k++) sim.SpawnUnit(1, "spearman", 20 + k, 24);
        // Spearmen hold, so the archers get many shots in before the lines meet.
        sim.Run(20 * Simulation.TicksPerSecond);
        if (dayNight) Assert.True(sim.Combat.Misses > 0);
        else Assert.Equal(0, sim.Combat.Misses);
    }

    [Fact]
    public void WhetherAShotLandsIsTheSameOnEveryMachine()
    {
        var a = Flat();
        var b = Flat();
        RunTo(a, Midnight);
        RunTo(b, Midnight);
        int landed = 0;
        for (int id = 1; id <= 400; id++)
        {
            bool x = a.Conditions.ShotLands(id, 10, 10);
            Assert.Equal(x, b.Conditions.ShotLands(id, 10, 10));
            if (x) landed++;
        }
        // About the hit chance: 80 of 100.
        Assert.InRange(landed, 280, 360);
    }

    [Fact]
    public void TheSameMatchWithDayAndNightGivesTheSameHash()
    {
        ulong Play()
        {
            var sim = new Simulation(5, 96, 32, 96) { Fog = true, DayNight = true };
            sim.SetUpUsualStart();
            sim.Run(Conditions.DayTicks / 24 * 14); // into the evening
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void ReplaysKeepDayAndNight()
    {
        var log = CommandLog.UsualStart(5, 64, 32, 64, dayNight: true, dayStart: 22 * 60);
        var text = new System.IO.StringWriter();
        log.Write(text);
        Assert.Contains("daynight 1", text.ToString());
        var back = CommandLog.Read(new System.IO.StringReader(text.ToString()));
        Assert.True(back.DayNight);
        var sim = back.CreateSimulation();
        Assert.True(sim.DayNight);
        Assert.Equal(22 * 60, sim.DayStart);
        Assert.Equal(DayPart.Night, sim.Conditions.Part);
    }

    [Fact]
    public void AMatchCanStartAtAnyTimeOfDay()
    {
        var sim = Flat(dayNight: true);
        sim.DayStart = 23 * 60 + 30;
        Assert.Equal(DayPart.Night, sim.Conditions.Part);
        sim.Step();
        Assert.Throws<InvalidOperationException>(() => sim.DayStart = 600);
        RunTo(sim, Conditions.DayTicks / 48); // half an hour on
        Assert.Equal(0, sim.Conditions.Minute);
    }

    /// <summary>Computer players fight through a night (with fog of war, in the dark): the match replays the same.</summary>
    [Fact]
    public void AComputerMatchThroughTheNightReplaysTheSame()
    {
        var result = AI.AiMatch.Play(3, 256, 64, 256, 4 * 60 * Simulation.TicksPerSecond,
            levels: new[] { AI.AiLevel.Normal, AI.AiLevel.Easy }, fog: true, dayNight: true, dayStart: 19 * 60);
        Assert.True(result.Log.DayNight);
        var text = CommandLog.Parse(result.Log.ToString());
        var first = MatchReplay.Run(text, 600, out var sim1);
        var second = MatchReplay.Run(text, 600, out var sim2);
        Assert.Equal(-1, MatchReplay.FirstMismatch(first, second));
        Assert.Equal(sim1.StateHash(), sim2.StateHash());
        Assert.Equal(DayPart.Night, sim1.Conditions.Part);
    }
}
