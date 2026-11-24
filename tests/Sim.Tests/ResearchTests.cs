using System;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Researching techs, what they unlock and what their effects change.</summary>
public class ResearchTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    /// <summary>A finished town hall holding plenty of everything.</summary>
    private static Building Hall(Simulation sim, int owner, int x, int z, int amount = 500)
    {
        var hall = sim.Economy.PlaceFinished(owner, sim.Content.Building("town_hall"), x, z) ?? throw new InvalidOperationException("no room");
        foreach (var g in Goods.All) hall.Stock[(int)g] = amount;
        return hall;
    }

    private static TechDef Tech(Simulation sim, string id) => sim.Content.Tech(id);

    private static void Grant(Simulation sim, int owner, params string[] techs)
    {
        foreach (var t in techs) sim.Economy.Grant(owner, Tech(sim, t));
    }

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
    public void ResearchCostsGoodsAndTakesItsTime()
    {
        var sim = Flat();
        var hall = Hall(sim, 0, 10, 10);
        var farming = Tech(sim, "farming");
        Assert.Equal(ResearchResult.Ok, sim.Economy.CanResearch(0, farming));
        sim.Schedule(0, new ResearchCommand(0, "farming"));
        sim.Step();
        Assert.Same(farming, sim.Economy.Researching(0));
        Assert.Equal(500 - 40, hall.Stock[(int)Good.Wood]);
        Assert.Equal(ResearchResult.Busy, sim.Economy.CanResearch(0, Tech(sim, "tools")));

        // One at a time: a second command while busy does nothing and costs nothing.
        sim.Schedule(sim.Tick, new ResearchCommand(0, "tools"));
        sim.Run(farming.ResearchTicks - 2);
        Assert.Equal(500 - 40, hall.Stock[(int)Good.Wood]);
        Assert.False(sim.Economy.HasTech(0, farming));
        Assert.Equal(farming.ResearchTicks - 1, sim.Economy.ResearchTicks(0));
        sim.Step();
        Assert.True(sim.Economy.HasTech(0, farming));
        Assert.Null(sim.Economy.Researching(0));
        Assert.Equal(ResearchResult.Researched, sim.Economy.CanResearch(0, farming));
        Assert.False(sim.Economy.HasTech(1, farming));
    }

    [Fact]
    public void PrerequisitesAndFactionsDecideWhatCanBeResearched()
    {
        var sim = Flat();
        var hall = Hall(sim, 0, 10, 10);
        Hall(sim, 1, 40, 40);
        Assert.Equal("settlers", sim.Economy.Faction(0).Id);
        Assert.Equal("orcs", sim.Economy.Faction(1).Id);
        Assert.Equal("deepfolk", sim.Economy.Faction(2).Id); // in data order, without a choice
        var econ = sim.Economy;
        Assert.Equal(ResearchResult.NeedsTechs, econ.CanResearch(0, Tech(sim, "crop_rotation")));
        Assert.Equal(ResearchResult.OtherFaction, econ.CanResearch(0, Tech(sim, "orc_war_drums")));
        Assert.Equal(ResearchResult.Ok, econ.CanResearch(1, Tech(sim, "orc_war_drums")));
        Assert.Equal(ResearchResult.OtherFaction, econ.CanResearch(1, Tech(sim, "settler_guilds")));

        sim.Schedule(0, new ResearchCommand(0, "crop_rotation"));
        sim.Schedule(0, new ResearchCommand(0, "orc_war_drums"));
        sim.Schedule(0, new ResearchCommand(0, "no_such_tech"));
        sim.Step();
        Assert.Null(econ.Researching(0));
        Assert.Equal(500, hall.Stock[(int)Good.Food]);

        Grant(sim, 0, "farming");
        Assert.Equal(ResearchResult.Ok, econ.CanResearch(0, Tech(sim, "crop_rotation")));
        Assert.Equal(ResearchResult.NeedsTechs, econ.CanResearch(0, Tech(sim, "settler_drill")));
        Grant(sim, 0, "tools", "masonry", "smelting");
        Assert.Equal(ResearchResult.Ok, econ.CanResearch(0, Tech(sim, "settler_drill")));
    }

    [Fact]
    public void ResearchNeedsAFinishedTownHallAndTheGoods()
    {
        var sim = Flat();
        var farming = Tech(sim, "farming");
        Assert.Equal(ResearchResult.NoTownHall, sim.Economy.CanResearch(0, farming));
        var store = sim.Economy.PlaceFinished(0, sim.Content.Building("storehouse"), 30, 30)!;
        store.Stock[(int)Good.Wood] = 39;
        Assert.Equal(ResearchResult.NoTownHall, sim.Economy.CanResearch(0, farming));
        Hall(sim, 0, 10, 10, amount: 0);
        Assert.Equal(ResearchResult.CannotAfford, sim.Economy.CanResearch(0, farming));
        Assert.Equal(ResearchResult.Ok, sim.Economy.CanResearch(0, farming, checkCost: false));
        store.Stock[(int)Good.Wood] = 40;
        sim.Schedule(0, new ResearchCommand(0, "farming"));
        sim.Step();
        Assert.Same(farming, sim.Economy.Researching(0));
        Assert.Equal(0, store.Stock[(int)Good.Wood]);
    }

    [Fact]
    public void BuildingsAndUnitsWaitForTheirTechs()
    {
        var sim = Flat();
        var hall = Hall(sim, 0, 4, 4);
        int worker = sim.SpawnUnit(0, "worker", 30, 40);
        var farm = sim.Content.Building("farm");
        Assert.False(sim.Economy.Unlocked(0, farm));
        Assert.Equal(PlaceResult.Locked, sim.Economy.CanPlace(0, farm, 30, 20, out _));
        sim.Schedule(0, new BuildCommand(0, new[] { worker }, "farm", 30, 20));
        sim.Step();
        Assert.Single(sim.Buildings.All);
        Grant(sim, 0, "farming");
        Assert.Equal(PlaceResult.Ok, sim.Economy.CanPlace(0, farm, 30, 20, out _));
        sim.Schedule(sim.Tick, new BuildCommand(0, new[] { worker }, "farm", 30, 20));
        sim.Step();
        Assert.Equal(2, sim.Buildings.Count);

        var barracks = sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 20, 4)!;
        Assert.False(sim.Economy.Unlocked(0, sim.Content.Unit("archer")));
        sim.Schedule(sim.Tick, new TrainCommand(0, barracks.Id, "archer"));
        sim.Schedule(sim.Tick, new TrainCommand(0, hall.Id, "spearman")); // the hall only trains workers
        sim.Step();
        Assert.Empty(barracks.Queue);
        Assert.Empty(hall.Queue);
        sim.Schedule(sim.Tick, new TrainCommand(0, barracks.Id, "spearman"));
        sim.Step();
        Grant(sim, 0, "archery");
        sim.Schedule(sim.Tick, new TrainCommand(0, barracks.Id, "archer"));
        sim.Step();
        Assert.Equal(new[] { sim.Content.Unit("spearman").Index, sim.Content.Unit("archer").Index }, barracks.Queue);
    }

    [Fact]
    public void TheBarracksTrainsSoldiers()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        var barracks = sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 20, 4)!;
        var spearman = sim.Content.Unit("spearman");
        sim.Schedule(0, new TrainCommand(0, barracks.Id, "spearman"));
        RunUntil(sim, () => sim.Units.Count == 1, spearman.TrainTicks + 5, "the spearman");
        Assert.Equal(spearman.Index, sim.Units.Def[0]);
        Assert.Equal(0, sim.Units.Owner[0]);
        Assert.True(barracks.ColumnDistance(sim.Units.X[0].FloorToInt(), sim.Units.Z[0].FloorToInt()) <= 2);
    }

    [Fact]
    public void RoadsNeedTheRoadsTech()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        Assert.False(sim.Economy.CanLayRoads(0));
        sim.Schedule(0, new RoadCommand(0, 20, 30, 30, 30));
        sim.Step();
        Assert.Equal(BlockType.Grass, sim.World.Get(25, Ground, 30));
        Grant(sim, 0, "roads");
        Assert.True(sim.Economy.CanLayRoads(0));
        sim.Schedule(sim.Tick, new RoadCommand(0, 20, 30, 30, 30));
        sim.Step();
        Assert.Equal(BlockType.Road, sim.World.Get(25, Ground, 30));
    }

    [Fact]
    public void EconomyEffectsChangeTheirNumbers()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 20, 4);
        var econ = sim.Economy;
        // Stone takes 25 ticks, everyone a seventh-or-so quicker (#150: Craft guilds' 15% went to everyone).
        int stone = 25 * 100 / (100 + EconomySystem.BaseGatherPercent);
        Assert.Equal(stone, econ.WorkTicks(0, BlockType.Stone));
        Assert.Equal(5, econ.Capacity(0));
        Assert.Equal(6, econ.FarmYieldOf(0));
        Assert.Equal(30, econ.Housing(0));

        Grant(sim, 0, "tools");                  // tools, not faster gathering (#146)
        Assert.Equal(stone, econ.WorkTicks(0, BlockType.Stone));
        Grant(sim, 0, "settler_guilds");         // repair, not faster gathering (#150)
        Assert.Equal(stone, econ.WorkTicks(0, BlockType.Stone));
        Assert.True(econ.CanRepair(0));
        Grant(sim, 0, "roads", "carts");         // pack ponies, not bigger loads (#149)
        Assert.Equal(5, econ.Capacity(0));
        Grant(sim, 0, "farming", "crop_rotation"); // orchards, not richer harvests (#148)
        Assert.Equal(6, econ.FarmYieldOf(0));
        Grant(sim, 0, "masonry");                // murder holes, not roomier houses (#147)
        Assert.Equal(30, econ.Housing(0));

        // Nothing changed for the other player.
        Assert.Equal(stone, econ.WorkTicks(1, BlockType.Stone));
        Assert.Equal(5, econ.Capacity(1));
    }

    [Fact]
    public void FasterGatheringReallyIsFaster()
    {
        // No tech in the game's data gathers faster any more (techs give tools: #10), so this one is made up for the test.
        var files = ContentTests.Files();
        const string farming = "\"cost\": { \"wood\": 40 }, \"researchTime\": 20 }";
        Assert.Contains(farming, files["techs.json"]);
        files["techs.json"] = files["techs.json"].Replace(farming,
            "\"cost\": { \"wood\": 40 }, \"researchTime\": 20, \"effects\": [ { \"type\": \"gather_speed\", \"percent\": 15 } ] }");
        var content = ContentLoader.Load(files);
        int TicksToDig(bool tools)
        {
            var sim = new Simulation(new RegionMap(1, TestWorlds.Flat(64, 16, 64, Ground)), content);
            Hall(sim, 0, 4, 4);
            if (tools) Grant(sim, 0, "farming");
            sim.World.Set(30, Ground + 1, 30, BlockType.Stone);
            int w = sim.SpawnUnit(0, "worker", 28, 30);
            sim.Schedule(0, new GatherCommand(0, new[] { w }, 30, Ground + 1, 30));
            return RunUntil(sim, () => sim.World.Get(30, Ground + 1, 30) == BlockType.Air, 400, "the dig");
        }
        int b = EconomySystem.BaseGatherPercent;
        Assert.Equal(25 * 100 / (100 + b) - 25 * 100 / (115 + b), TicksToDig(false) - TicksToDig(true));
    }

    [Fact]
    public void HitPointsGoUpForLivingAndNewUnits()
    {
        // Hardened chitin (line infantry +10) is the hit point tech left since Beast pens became the pen (issue #152).
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        var barracks = sim.Economy.PlaceFinished(0, sim.Content.Building("barracks"), 20, 4)!;
        int spear = sim.SpawnUnit(0, "spearman", 30, 30);
        int worker = sim.SpawnUnit(0, "worker", 31, 30);
        int enemy = sim.SpawnUnit(1, "spearman", 40, 40);
        Grant(sim, 0, "deep_chitin"); // line infantry +10
        var u = sim.Units;
        Assert.Equal(90, u.Hp[u.IndexOf(spear)]);
        Assert.Equal(40, u.Hp[u.IndexOf(worker)]);
        Assert.Equal(80, u.Hp[u.IndexOf(enemy)]);
        Assert.Equal(90, sim.Economy.MaxHp(0, sim.Content.Unit("spearman")));
        Assert.Equal(90, u.Hp[u.IndexOf(sim.SpawnUnit(0, "spearman", 32, 30))]);
        sim.Schedule(0, new TrainCommand(0, barracks.Id, "spearman"));
        RunUntil(sim, () => u.Count == 5, 600, "the trained spearman");
        Assert.Equal(90, u.Hp[4]);
    }

    [Fact]
    public void BeastPensNoLongerToughensMounts()
    {
        // Issue #152: the flat +30 for cavalry is gone; Beast pens builds the beast pen (see StampedeTests).
        var sim = Flat();
        Hall(sim, 1, 4, 4);
        var rider = sim.Content.Unit("wolf_rider");
        int before = sim.Economy.MaxHp(1, rider);
        Grant(sim, 1, "horse_breeding", "orc_beast_pens");
        Assert.Equal(before, sim.Economy.MaxHp(1, rider));
        Assert.True(sim.Economy.Unlocked(1, sim.Content.Building("beast_pen")));
    }

    [Fact]
    public void WarDrumsTrainsTheDrummerAndNoLongerSpeedsUpTheOrcs()
    {
        // Issue #151: the flat 15% is gone; War drums unlocks the war drummer at the barracks (see DrumTests).
        var sim = Flat();
        Hall(sim, 1, 4, 4);
        var spear = sim.Content.Unit("spearman");
        Grant(sim, 1, "orc_war_drums");
        Assert.Equal(spear.SpeedPerTick, sim.Economy.SpeedPerTick(1, spear));
        var drummer = sim.Content.Unit("war_drummer");
        Assert.Contains(sim.Content.Tech("orc_war_drums"), drummer.Requires);
        Assert.Contains("war_drummer", sim.Content.Building("barracks").Trains);
        Assert.Empty(sim.Content.Tech("orc_war_drums").Effects);
    }

    [Fact]
    public void ResearchIsPartOfTheStateHash()
    {
        var a = Flat();
        var b = Flat();
        Hall(a, 0, 4, 4);
        Hall(b, 0, 4, 4);
        a.Step();
        b.Step();
        Assert.Equal(a.StateHash(), b.StateHash());
        Grant(a, 0, "archery"); // nothing but the tech itself changes
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Grant(b, 0, "archery");
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Schedule(a.Tick, new ResearchCommand(0, "farming"));
        b.Schedule(b.Tick, new ResearchCommand(0, "farming"));
        a.Step();
        b.Step();
        Assert.Equal(a.StateHash(), b.StateHash());
        b.Step();
        a.Run(1);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void LogsRecordResearchAndTheirData()
    {
        var log = new CommandLog { Seed = 5, SizeX = 64, SizeY = 32, SizeZ = 64, Ticks = 100, StartBuildings = true, ContentHash = ContentDb.Default.Hash };
        log.Add(3, new ResearchCommand(0, "farming"));
        string text = log.ToString();
        Assert.Contains("\n3 research 0 farming\n", text);
        Assert.Contains($"\ncontent {ContentDb.Default.Hash:x16}\n", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(log.ContentHash, copy.ContentHash);
        Assert.Equal(text, copy.ToString());

        var other = CommandLog.Parse(text.Replace($"content {ContentDb.Default.Hash:x16}", "content 0123456789abcdef"));
        var e = Assert.Throws<InvalidOperationException>(() => other.CreateSimulation());
        Assert.Contains("different game data", e.Message);
        Assert.Null(CommandLog.Parse(text.Replace($"content {ContentDb.Default.Hash:x16}\n", "")).ContentHash);
    }

    [Fact]
    public void ScriptedPlayersResearchAndTrainSoldiers()
    {
        // Small maps have no room for town halls, so this one is mid-sized; it runs for four minutes, long
        // enough for a barracks to go up and train.
        var log = ScriptedMatch.Generate(11, 256, 64, 256, ticks: 4800, startUnits: 8);
        int research = 0, soldiers = 0;
        foreach (var (_, c) in log.Commands)
        {
            if (c is ResearchCommand) research++;
            if (c is TrainCommand t && t.Unit != "worker") soldiers++;
        }
        Assert.True(research >= 4, $"{research} research commands");
        Assert.True(soldiers >= 1, $"{soldiers} soldiers trained");
    }
}
