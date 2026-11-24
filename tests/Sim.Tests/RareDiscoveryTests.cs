using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The rare discovery techs (issue #154): Giant's marrow after beating a pass's stone giant (30% more hit points for
/// every fighting unit), Cave guides after setting foot in the deep caves (the delver), Crystal lens after digging
/// crystal (a tower that shows a far circle through the fog for a while).
/// </summary>
public class RareDiscoveryTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;

    private static readonly Discovery[] Rare = { Discovery.Giant, Discovery.Deeps, Discovery.Crystal };

    private static TechDef Tech(Simulation sim, string id) => sim.Content.Techs.First(t => t.Id == id);

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

    /// <summary>The rare discoveries the player has made, in order.</summary>
    private static Discovery[] Made(Simulation sim, int player) => Rare.Where(d => sim.Economy.Discovered(player, d)).ToArray();

    private static bool Heard(Simulation sim, int player, TechDef tech) =>
        sim.Events.All.Any(e => e.Player == player && e.Kind == GameEventKind.TechDiscovered && e.Def == tech.Index);

    // --- Giant's marrow --------------------------------------------------------------------------------

    /// <summary>Clearing a wolf lair opens no rare tech; beating a stone giant's lair (a pass's) opens Giant's marrow.</summary>
    [Fact]
    public void BeatingAPassGiantOpensGiantsMarrowAndAWolfLairDoesNot()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, 3));
        var marrow = Tech(sim, "giants_marrow");
        Assert.Equal(Discovery.Giant, marrow.Discovery);
        Assert.True(sim.Content.Unit("stone_giant").Lair!.Pass);

        var wolves = sim.Monsters.Place(sim.Content.Unit("wolf"), 30, 30)!;
        var spears = Enumerable.Range(0, 8).Select(k => sim.SpawnUnit(Settlers, "spearman", 14 + k % 4, 28 + k / 4 * 2)).ToArray();
        sim.Schedule(sim.Tick, new AttackMoveCommand(Settlers, spears, 30, 30));
        RunUntil(sim, () => wolves.Cleared, 60 * Tps, "the wolves are beaten");
        Assert.True(sim.Economy.Discovered(Settlers, Discovery.Lair));
        Assert.Empty(Made(sim, Settlers));
        Assert.Equal(ResearchResult.NotDiscovered, sim.Economy.CanResearch(Settlers, marrow, checkCost: false));

        var giant = sim.Monsters.Place(sim.Content.Unit("stone_giant"), 90, 90)!;
        var army = Enumerable.Range(0, 20).Select(k => sim.SpawnUnit(Settlers, k % 2 == 0 ? "spearman" : "archer", 70 + k % 5, 80 + k / 5 * 2)).ToArray();
        sim.Schedule(sim.Tick, new AttackMoveCommand(Settlers, army, 90, 90));
        RunUntil(sim, () => giant.Cleared, 120 * Tps, "the stone giant is beaten");
        Assert.Equal(new[] { Discovery.Giant }, Made(sim, Settlers));
        Assert.Empty(Made(sim, Orcs));
        Assert.True(Heard(sim, Settlers, marrow));
    }

    /// <summary>
    /// Giant's marrow gives every fighting unit 30% more hit points, those already alive too, and only its player's;
    /// workers are left as they were.
    /// </summary>
    [Fact]
    public void GiantsMarrowRaisesEveryFightingUnitsHitPointsByAThird()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, 3));
        int spear = sim.SpawnUnit(Settlers, "spearman", 10, 10);
        int worker = sim.SpawnUnit(Settlers, "worker", 12, 10);
        int theirs = sim.SpawnUnit(Orcs, "spearman", 40, 40);
        var spearDef = sim.Content.Unit("spearman");
        Assert.Equal(80, spearDef.MaxHp);

        sim.Economy.Grant(Settlers, Tech(sim, "giants_marrow"));
        Assert.Equal(104, sim.Units.Hp[sim.Units.IndexOf(spear)]);
        Assert.Equal(104, sim.Economy.MaxHp(Settlers, spearDef));
        Assert.Equal(50 * 130 / 100, sim.Economy.MaxHp(Settlers, sim.Content.Unit("archer")));
        Assert.Equal(160 * 130 / 100, sim.Economy.MaxHp(Settlers, sim.Content.Unit("catapult")));
        Assert.Equal(40, sim.Units.Hp[sim.Units.IndexOf(worker)]);
        Assert.Equal(40, sim.Economy.MaxHp(Settlers, sim.Content.Unit("worker")));
        Assert.Equal(80, sim.Units.Hp[sim.Units.IndexOf(theirs)]);
        Assert.Equal(80, sim.Economy.MaxHp(Orcs, spearDef));

        int fresh = sim.SpawnUnit(Settlers, "spearman", 14, 10);
        Assert.Equal(104, sim.Units.Hp[sim.Units.IndexOf(fresh)]);
    }

    // --- Cave guides and the delver --------------------------------------------------------------------

    /// <summary>
    /// A map tall enough for deep rock (100 high: 36 layers of it) with the land at 70; a chamber is dug out of the
    /// deep rock under (30..40, 30..40), its floor at 20, and a shaft from it up to the surface at (35, 41..44).
    /// </summary>
    private static Simulation Deep()
    {
        var w = TestWorlds.Flat(80, 100, 80, 70);
        Assert.Equal(36, RegionGenerator.DeepLayers(w.SizeY));
        TestWorlds.Box(w, 30, 20, 30, 40, 24, 40, BlockType.Air);
        var sim = TestWorlds.Sim(w);
        return sim;
    }

    /// <summary>Units on the land open nothing; the first one to stand in the deep caves opens Cave guides for its player.</summary>
    [Fact]
    public void SettingFootInTheDeepCavesOpensCaveGuides()
    {
        var sim = Deep();
        var guides = Tech(sim, "cave_guides");
        Assert.Equal(Discovery.Deeps, guides.Discovery);
        sim.SpawnUnit(Settlers, "spearman", 10, 10);
        sim.SpawnUnit(Orcs, "spearman", 60, 60);
        sim.Run(Tps);
        Assert.Empty(Made(sim, Settlers));

        // A wild monster down there opens nothing for anyone.
        sim.SpawnUnit(Simulation.WildPlayer, "wolf", 31, 31, 20);
        sim.Run(Tps);
        Assert.Empty(Made(sim, Settlers));
        Assert.Empty(Made(sim, Orcs));

        sim.SpawnUnit(Settlers, "worker", 35, 35, 20);
        sim.Step();
        Assert.Equal(new[] { Discovery.Deeps }, Made(sim, Settlers));
        Assert.Empty(Made(sim, Orcs));
        Assert.True(Heard(sim, Settlers, guides));
    }

    /// <summary>Cave guides lets the town hall train the delver, which needs it.</summary>
    [Fact]
    public void CaveGuidesUnlocksTheDelverAtTheTownHall()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, 3));
        var delver = sim.Content.Unit("delver");
        Assert.Same(sim.Content.Building("town_hall"), delver.TrainedAt);
        Assert.True(delver.DarkSight && delver.CaveWise && delver.Hearing > 0);
        Assert.False(sim.Economy.Unlocked(Settlers, delver));
        sim.Economy.Grant(Settlers, Tech(sim, "cave_guides"));
        Assert.True(sim.Economy.Unlocked(Settlers, delver));
        Assert.False(sim.Economy.Unlocked(Orcs, delver));
    }

    /// <summary>
    /// A delver on the surface hears an enemy unit in a tunnel under rock well beyond a spearman's two columns, as a
    /// listening post would, so its player sees that unit; a spearman in its place does not.
    /// </summary>
    [Fact]
    public void ADelverHearsAUnitUnderground()
    {
        foreach (var (unit, hears) in new[] { ("delver", true), ("spearman", false) })
        {
            var w = TestWorlds.Flat(96, 24, 96, 10);
            TestWorlds.Box(w, 20, 4, 40, 60, 5, 40, BlockType.Air);
            var sim = TestWorlds.Sim(w);
            sim.Fog = true;
            int sapper = sim.SpawnUnit(Orcs, "sapper", 40, 40, 4);
            sim.SpawnUnit(Settlers, unit, 40, 52); // 12 columns off, on the surface over the rock
            sim.Run(VisionSystem.UpdateTicks + 1);
            int s = sim.Units.IndexOf(sapper);
            Assert.True(sim.Vision.Underground(s));
            Assert.Equal(hears, sim.Vision.Hears(Settlers, 40, 40));
            Assert.Equal(hears, sim.Vision.SeesUnit(Settlers, s));
        }
    }

    /// <summary>Underground a delver sees twice as far as its sight; a spearman down there sees only its own.</summary>
    [Fact]
    public void ADelverSeesTwiceAsFarUnderground()
    {
        var w = TestWorlds.Flat(96, 24, 96, 10);
        TestWorlds.Box(w, 10, 4, 10, 80, 6, 80, BlockType.Air); // a wide cave, roofed over
        var sim = TestWorlds.Sim(w);
        sim.Fog = true;
        sim.SpawnUnit(Settlers, "delver", 30, 30, 4);
        sim.SpawnUnit(Orcs, "spearman", 30, 30, 4);
        sim.Run(VisionSystem.UpdateTicks + 1);
        // The delver's sight is 10: underground it reaches 20 columns.
        Assert.True(sim.Vision.Sees(Settlers, 30 + 19, 30));
        Assert.False(sim.Vision.Sees(Settlers, 30 + 22, 30));
        // The spearman's is 8.
        Assert.True(sim.Vision.Sees(Orcs, 30 + 8, 30));
        Assert.False(sim.Vision.Sees(Orcs, 30 + 10, 30));
    }

    /// <summary>Lava's heat hurts a spearman standing by it, not a delver.</summary>
    [Fact]
    public void LavaDoesNotHurtADelver()
    {
        var w = TestWorlds.Flat(64, 16, 64, 3);
        w.Set(20, 3, 20, BlockType.Lava);
        w.Set(40, 3, 40, BlockType.Lava);
        var sim = TestWorlds.Sim(w);
        int delver = sim.SpawnUnit(Settlers, "delver", 21, 20);
        int spear = sim.SpawnUnit(Settlers, "spearman", 41, 40);
        sim.Run(3 * Tps);
        Assert.Equal(60, sim.Units.Hp[sim.Units.IndexOf(delver)]);
        Assert.True(sim.Units.Hp[sim.Units.IndexOf(spear)] < 80);
    }

    /// <summary>
    /// In the deep caves a pack leaves a delver by its den alone, and goes after a spearman there; on the land above,
    /// the same pack goes after a delver as after anyone.
    /// </summary>
    [Fact]
    public void DeepNestsLeaveADelverAloneInTheDeepCaves()
    {
        foreach (var (ground, spared) in new[] { (20, true), (70, false) })
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(96, 100, 96, ground));
            var lair = sim.Monsters.Place(sim.Content.Unit("cave_crawler"), 50, 50)!;
            Assert.Equal(spared, sim.Economy.DeepGround(ground + 1));
            // Holding its ground out of reach of the pack, so it does not start the fight itself.
            int delver = sim.SpawnUnit(Settlers, "delver", 50, 57);
            sim.Schedule(sim.Tick, new StanceCommand(Settlers, new[] { delver }, UnitStance.Hold));
            sim.Run(3 * Tps);
            int d = sim.Units.IndexOf(delver);
            bool goneAfter = lair.Pack.Any(m => sim.Units.IndexOf(m) is var i && i >= 0 && sim.Units.Target[i] == delver);
            Assert.Equal(!spared, goneAfter || d < 0 || sim.Units.Hp[d] < 60);
            if (!spared) continue;
            int spear = sim.SpawnUnit(Orcs, "spearman", 54, 48);
            RunUntil(sim, () => lair.Pack.Any(m => sim.Units.IndexOf(m) is var i && i >= 0 && sim.Units.Target[i] == spear), 3 * Tps,
                "the crawlers go after the spearman");
            Assert.Equal(60, sim.Units.Hp[sim.Units.IndexOf(delver)]);
        }
    }

    // --- Crystal lens ----------------------------------------------------------------------------------

    /// <summary>A worker digging stone opens nothing; digging a block of crystal opens Crystal lens.</summary>
    [Fact]
    public void DiggingCrystalOpensCrystalLens()
    {
        var w = TestWorlds.Flat(64, 16, 64, 3);
        w.Set(20, 4, 20, BlockType.Stone);
        w.Set(30, 4, 30, BlockType.Crystal);
        var sim = TestWorlds.Sim(w);
        sim.Economy.PlaceFinished(Settlers, sim.Content.Building("storehouse"), 10, 10);
        var lens = Tech(sim, "crystal_lens");
        Assert.Equal(Discovery.Crystal, lens.Discovery);
        int worker = sim.SpawnUnit(Settlers, "worker", 18, 18);
        sim.Schedule(sim.Tick, new GatherCommand(Settlers, new[] { worker }, 20, 4, 20));
        RunUntil(sim, () => w.Get(20, 4, 20) == BlockType.Air, 30 * Tps, "the stone is dug");
        Assert.Empty(Made(sim, Settlers));

        sim.Schedule(sim.Tick, new GatherCommand(Settlers, new[] { worker }, 30, 4, 30));
        RunUntil(sim, () => w.Get(30, 4, 30) == BlockType.Air, 30 * Tps, "the crystal is dug");
        sim.Step();
        Assert.Equal(new[] { Discovery.Crystal }, Made(sim, Settlers));
        Assert.True(Heard(sim, Settlers, lens));
        Assert.Contains("Crystal lens", sim.Content.UnlockedBy(lens));
    }

    /// <summary>
    /// A finished crystal lens aimed far off shows its player a circle 30 columns round that spot through the fog for
    /// 20 seconds, for 25 mana; then the ground there is only seen, and the lens waits 3 minutes before it gazes again.
    /// Another player cannot aim it, and with too little mana it does not gaze.
    /// </summary>
    [Fact]
    public void TheLensShowsAFarCircleThroughTheFogForItsTimeOnly()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(200, 16, 200, 3));
        sim.Fog = true;
        var lens = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("crystal_lens"), 10, 10)!;
        var def = lens.Def.Lens!;
        Assert.Equal((180 * Tps, 30, 20 * Tps, 25), (def.EveryTicks, def.Radius, def.Ticks, def.Mana));
        sim.Run(VisionSystem.UpdateTicks + 1);
        const int fx = 150, fz = 150;
        Assert.Equal(Sight.Unseen, sim.Vision.At(Settlers, fx, fz));
        int mana = sim.Economy.Player(Settlers).Mana;

        // Not theirs to aim.
        sim.Schedule(sim.Tick, new GazeCommand(Orcs, lens.Id, fx, fz));
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.Empty(sim.Economy.Gazes);
        Assert.Equal(Sight.Unseen, sim.Vision.At(Orcs, fx, fz));

        Assert.Equal(GazeResult.Ok, sim.Economy.CanGaze(Settlers, lens));
        sim.Schedule(sim.Tick, new GazeCommand(Settlers, lens.Id, fx, fz));
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.Equal(mana - 25, sim.Economy.Player(Settlers).Mana);
        Assert.Single(sim.Economy.Gazes);
        Assert.True(sim.Vision.Sees(Settlers, fx, fz));
        Assert.True(sim.Vision.Sees(Settlers, fx + 30, fz));
        Assert.True(sim.Vision.Sees(Settlers, fx, fz - 30));
        Assert.False(sim.Vision.Sees(Settlers, fx + 32, fz));
        Assert.Equal(Sight.Unseen, sim.Vision.At(Orcs, fx, fz));
        Assert.Equal(GazeResult.NotReady, sim.Economy.CanGaze(Settlers, lens));

        // An enemy standing there is seen.
        int foe = sim.SpawnUnit(Orcs, "spearman", fx + 5, fz);
        sim.Run(VisionSystem.UpdateTicks);
        Assert.True(sim.Vision.SeesUnit(Settlers, sim.Units.IndexOf(foe)));

        // After its 20 seconds the circle is only seen ground.
        sim.Run(20 * Tps);
        Assert.Empty(sim.Economy.Gazes);
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, fx, fz));
        Assert.False(sim.Vision.SeesUnit(Settlers, sim.Units.IndexOf(foe)));

        // Ready again 3 minutes after it began, if there is the mana.
        sim.Run(def.EveryTicks - 20 * Tps);
        sim.Economy.Player(Settlers).Mana = 10;
        Assert.Equal(GazeResult.NoMana, sim.Economy.CanGaze(Settlers, lens));
        sim.Schedule(sim.Tick, new GazeCommand(Settlers, lens.Id, fx, fz));
        sim.Run(2);
        Assert.Empty(sim.Economy.Gazes);
        sim.Economy.Player(Settlers).Mana = 30;
        Assert.Equal(GazeResult.Ok, sim.Economy.CanGaze(Settlers, lens));
    }

    /// <summary>A lens that is destroyed ends its gaze.</summary>
    [Fact]
    public void ALostLensEndsItsGaze()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(200, 16, 200, 3));
        sim.Fog = true;
        sim.TestMode = true;
        var lens = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("crystal_lens"), 10, 10)!;
        sim.Step();
        sim.Schedule(sim.Tick, new GazeCommand(Settlers, lens.Id, 150, 150));
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.True(sim.Vision.Sees(Settlers, 150, 150));
        // Wrecked (test mode's damage, as a siege would): its blocks are knocked out until it falls.
        for (int x = lens.X; x < lens.X + lens.Def.SizeX; x++)
        for (int z = lens.Z; z < lens.Z + lens.Def.SizeZ; z++)
            sim.Schedule(sim.Tick, new SandboxDamageCommand(Settlers, x, z, 1000));
        RunUntil(sim, () => sim.Buildings.Get(lens.Id) == null, 5 * Tps, "the lens falling");
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.Empty(sim.Economy.Gazes);
        Assert.False(sim.Vision.Sees(Settlers, 150, 150));
    }

    /// <summary>The same commands give the same hash, gazes and all.</summary>
    [Fact]
    public void GazesAreDeterministic()
    {
        ulong Run()
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, 3));
            sim.Fog = true;
            var lens = sim.Economy.PlaceFinished(Settlers, sim.Content.Building("crystal_lens"), 10, 10)!;
            sim.Schedule(5, new GazeCommand(Settlers, lens.Id, 100, 100));
            sim.Run(40);
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    // --- Computer players ------------------------------------------------------------------------------

    private static bool Play(Simulation sim, AI.AiPlayer ai, System.Collections.Generic.List<Command> log, Func<bool> done, int maxTicks)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c =>
            {
                log.Add(c);
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return done();
    }

    /// <summary>With Crystal lens, a computer player lays out a crystal lens by its town hall.</summary>
    [Theory]
    [InlineData(AI.AiLevel.Easy)]
    [InlineData(AI.AiLevel.Hard)]
    public void ComputerPlayersBuildACrystalLens(AI.AiLevel level)
    {
        var sim = AiTests.WithStarts(128, (40, 40), (100, 100));
        var hall = sim.Economy.TownHall(0)!;
        foreach (var g in new[] { Good.Food, Good.Wood, Good.Stone, Good.Iron, Good.Crystal }) hall.Stock[(int)g] = 800;
        sim.Economy.Grant(0, Tech(sim, "crystal_lens"));
        var ai = new AI.AiPlayer(0, 1, level);
        var log = new System.Collections.Generic.List<Command>();
        Assert.True(Play(sim, ai, log, () => sim.Buildings.All.Any(b => b.Owner == 0 && b.Def.Kind == BuildingKind.Lens), 60 * Tps),
            "a crystal lens to be laid out");
        Assert.Single(sim.Buildings.All, b => b.Owner == 0 && b.Def.Kind == BuildingKind.Lens);
    }

    /// <summary>A computer player with a crystal lens aims it at where its wave is going as the wave sets out.</summary>
    [Fact]
    public void AComputerPlayerAimsItsLensWhereItsWaveIsGoing()
    {
        var sim = AiTests.WithStarts(96, (20, 48), (76, 48));
        sim.Fog = true;
        for (int k = 0; k < 20; k++) sim.SpawnUnit(0, "spearman", 10 + k % 10, 56 + k / 10 * 2);
        sim.SpawnUnit(1, "spearman", 70, 56);
        sim.Economy.PlaceFinished(0, sim.Content.Building("watchtower"), 58, 40); // keeps their town in its sight
        var lens = sim.Economy.PlaceFinished(0, sim.Content.Building("crystal_lens"), 10, 30)!;
        var ai = new AI.AiPlayer(0, 1, AI.AiLevel.Easy);
        var log = new System.Collections.Generic.List<Command>();
        Assert.True(Play(sim, ai, log, () => ai.Waves > 0, AI.AiPlayer.SmartFirstWaveTicks + 60 * Tps), "a wave to set out: " + string.Join("; ", ai.Journal.Select(j => j.Text)));
        var gaze = Assert.Single(log.OfType<GazeCommand>());
        Assert.Equal(lens.Id, gaze.Building);
        var theirs = sim.Economy.TownHall(1)!;
        Assert.True(Math.Abs(gaze.X - (theirs.X + theirs.MaxX) / 2) <= 8 && Math.Abs(gaze.Z - (theirs.Z + theirs.MaxZ) / 2) <= 8,
            $"the gaze at {gaze.X},{gaze.Z} to be on their town at {theirs.X},{theirs.Z}");
        sim.Run(VisionSystem.UpdateTicks + 1);
        Assert.True(sim.Vision.Sees(0, gaze.X, gaze.Z));
    }

    /// <summary>
    /// With Cave guides, a computer player facing a rival's great work trains a delver and sends it to the great work
    /// ahead of the strike force, not in it.
    /// </summary>
    [Fact]
    public void AComputerPlayerSendsADelverAheadToARivalsGreatWork()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(192, 128, 192, 3));
        sim.Map.Starts.Add(new StartArea(0, 170, 4, 24));
        sim.Map.Starts.Add(new StartArea(1, 40, 4, 96));
        sim.PlaceStartingBuildings();
        var hall = sim.Economy.TownHall(1)!;
        foreach (var g in new[] { Good.Food, Good.Wood }) hall.Stock[(int)g] = 800;
        for (int k = 0; k < 12; k++) sim.SpawnUnit(1, "spearman", 36 + k % 8, hall.MaxZ + 2 + k / 8);
        sim.Economy.Grant(1, Tech(sim, "cave_guides"));
        var work = sim.Economy.PlaceFinished(0, sim.Content.Building("hall_of_legends"), 110, 160)!;
        var ai = new AI.AiPlayer(1, 0, AI.AiLevel.Easy);
        var log = new System.Collections.Generic.List<Command>();
        Assert.True(Play(sim, ai, log, () => log.OfType<TrainCommand>().Any(c => c.Unit == "delver"), 10 * Tps), "a delver to be trained");
        var u = sim.Units;
        int Delver() => Enumerable.Range(0, u.Count).Where(i => u.Owner[i] == 1 && u.Def[i] == sim.Content.Unit("delver").Index).Select(i => u.Id[i]).FirstOrDefault();
        Assert.True(Play(sim, ai, log, () => Delver() != 0, 30 * Tps), "the delver to step out");
        int id = Delver();
        int wx = (work.X + work.MaxX) / 2, wz = (work.Z + work.MaxZ) / 2;
        Assert.True(Play(sim, ai, log, () => u.IndexOf(id) is var i && i >= 0
            && Math.Abs(u.X[i].FloorToInt() - wx) <= 8 && Math.Abs(u.Z[i].FloorToInt() - wz) <= 8, 90 * Tps), "the delver to reach the great work");
        Assert.DoesNotContain(id, ai.StrikeForce);
        Assert.Single(log.OfType<TrainCommand>(), c => c.Unit == "delver");
    }
}
