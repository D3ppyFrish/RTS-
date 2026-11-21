using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Discovery techs (the design doc's techs unlocked only by exploring): Wardens' charts after searching a ruin,
/// Beast lore after clearing a lair, Trail maps after taking a city-state's town.
/// </summary>
public class DiscoveryTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1, Wild = Simulation.WildPlayer;

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static TechDef Tech(Simulation sim, string id) => sim.Content.Techs.First(t => t.Id == id);

    private static Building Hall(Simulation sim, int owner, int x, int z)
    {
        var b = sim.Economy.PlaceFinished(owner, sim.Content.Building("town_hall"), x, z) ?? throw new InvalidOperationException("the hall does not fit");
        b.Stock[(int)Good.Food] = 500;
        b.Stock[(int)Good.Wood] = 500;
        b.Stock[(int)Good.Iron] = 500;
        return b;
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

    private static int[] Squad(Simulation sim, int owner, string unit, int x, int z, int count) =>
        Enumerable.Range(0, count).Select(k => sim.SpawnUnit(owner, unit, x + k % 4, z + k / 4 * 2)).ToArray();

    /// <summary>Researches the tech at the player's town hall and runs until it is done.</summary>
    private static void Research(Simulation sim, int player, TechDef tech)
    {
        Assert.Equal(ResearchResult.Ok, sim.Economy.CanResearch(player, tech));
        sim.Schedule(sim.Tick, new ResearchCommand(player, tech.Id));
        RunUntil(sim, () => sim.Economy.HasTech(player, tech), tech.ResearchTicks + 2 * Tps, $"{tech.Name} is researched");
    }

    private static bool TechDiscoveredEvent(Simulation sim, int player, TechDef tech) =>
        sim.Events.All.Any(e => e.Player == player && e.Kind == GameEventKind.TechDiscovered && e.Def == tech.Index);

    // --- Ruins: Wardens' charts ------------------------------------------------------------------

    /// <summary>A flat world with two ruins far apart, fog of war on, and a town hall for each side.</summary>
    private static Simulation TwoRuins()
    {
        var map = new RegionMap(1, TestWorlds.Flat(160, 16, 160, Ground));
        map.Ruins.Add((40, Ground, 40));
        map.Ruins.Add((130, Ground, 130));
        var sim = new Simulation(map) { Fog = true };
        Hall(sim, Settlers, 8, 8);
        Hall(sim, Orcs, 8, 140);
        return sim;
    }

    /// <summary>
    /// Wardens' charts cannot be researched until the player's units have searched a ruin. Then the player hears
    /// they can, and once it is researched the other ruin, far out in the fog, shows as seen ground (not in sight),
    /// for them alone.
    /// </summary>
    [Fact]
    public void SearchingARuinOpensWardensChartsWhichShowTheOtherRuins()
    {
        var sim = TwoRuins();
        var charts = Tech(sim, "wardens_charts");
        Assert.Equal(Discovery.Ruin, charts.Discovery);
        sim.Step();
        Assert.Equal(ResearchResult.NotDiscovered, sim.Economy.CanResearch(Settlers, charts));
        Assert.False(sim.Economy.Discovered(Settlers, Discovery.Ruin));

        int spear = sim.SpawnUnit(Settlers, "spearman", 30, 40, Ground + 1);
        sim.Schedule(sim.Tick, new SearchRuinCommand(Settlers, new[] { spear }, 0));
        RunUntil(sim, () => sim.Ruins.Ruins[0].Searched, 40 * Tps, "the first ruin is searched");
        Assert.True(sim.Economy.Discovered(Settlers, Discovery.Ruin));
        Assert.False(sim.Economy.Discovered(Orcs, Discovery.Ruin));
        Assert.True(TechDiscoveredEvent(sim, Settlers, charts));
        Assert.Equal(ResearchResult.NotDiscovered, sim.Economy.CanResearch(Orcs, charts));

        var far = sim.Ruins.Ruins[1];
        Assert.Equal(Sight.Unseen, sim.Vision.At(Settlers, far.X, far.Z));
        Research(sim, Settlers, charts);
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, far.X, far.Z));
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, far.X + EconomySystem.RevealRadius, far.Z));
        Assert.Equal(Sight.Unseen, sim.Vision.At(Settlers, far.X + EconomySystem.RevealRadius + 2, far.Z));
        Assert.Equal(Sight.Unseen, sim.Vision.At(Orcs, far.X, far.Z));
        // Seen, not in sight: nothing there is shown as it is now.
        sim.Run(VisionSystem.UpdateTicks * 2);
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, far.X, far.Z));
    }

    // --- Lairs: Beast lore -----------------------------------------------------------------------

    /// <summary>Settlers who clear a wolf lair discover Beast lore; the Orcs, who did not, have not.</summary>
    [Fact]
    public void ClearingALairOpensBeastLore()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        Hall(sim, Settlers, 10, 40);
        var lair = sim.Monsters.Place(sim.Content.Unit("wolf"), 60, 48)!;
        var lore = Tech(sim, "beast_lore");
        sim.Step();
        Assert.Equal(ResearchResult.NotDiscovered, sim.Economy.CanResearch(Settlers, lore));

        var spearmen = Squad(sim, Settlers, "spearman", 36, 46, 8);
        sim.Schedule(sim.Tick, new AttackMoveCommand(Settlers, spearmen, 60, 48));
        RunUntil(sim, () => lair.Cleared, 60 * Tps, "the settlers clear the lair");
        Assert.True(sim.Economy.Discovered(Settlers, Discovery.Lair));
        Assert.False(sim.Economy.Discovered(Orcs, Discovery.Lair));
        Assert.True(TechDiscoveredEvent(sim, Settlers, lore));
        foreach (int id in spearmen) if (sim.Units.Exists(id)) sim.Units.Remove(id);
        Research(sim, Settlers, lore);
        Assert.True(sim.Economy.SparesWorkers(Settlers));
    }

    /// <summary>
    /// With Beast lore, wolves leave a worker beside their den alone (it is inside their guard radius and their
    /// sight), but still go after a spearman there; the other side's worker beside it is attacked as before.
    /// </summary>
    [Fact]
    public void WithBeastLoreWolvesLeaveWorkersAloneButNotSoldiers()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        var lair = sim.Monsters.Place(sim.Content.Unit("wolf"), 60, 48)!;
        sim.Economy.Grant(Settlers, Tech(sim, "beast_lore"));
        int spared = sim.SpawnUnit(Settlers, "worker", 64, 50);
        int hp = sim.Units.Hp[Index(sim, spared)];
        sim.Run(10 * Tps);
        Assert.All(lair.Pack, w => Assert.NotEqual(spared, sim.Units.Target[Index(sim, w)]));
        Assert.Equal(hp, sim.Units.Hp[Index(sim, spared)]);

        int other = sim.SpawnUnit(Orcs, "worker", 64, 52);
        RunUntil(sim, () => lair.Pack.Any(w => sim.Units.Target[Index(sim, w)] == other), 3 * Tps,
            "the pack goes for the other side's worker");
        RunUntil(sim, () => !sim.Units.Exists(other), 20 * Tps, "the wolves kill it");
        sim.Run(10 * Tps);
        Assert.Equal(hp, sim.Units.Hp[Index(sim, spared)]);

        int spear = sim.SpawnUnit(Settlers, "spearman", 64, 46);
        RunUntil(sim, () => lair.Pack.Any(w => sim.Units.Target[Index(sim, w)] == spear), 3 * Tps,
            "the pack goes for the spearman");
        Assert.Equal(hp, sim.Units.Hp[Index(sim, spared)]);
    }

    // --- Towns: Trail maps -----------------------------------------------------------------------

    /// <summary>
    /// Taking the goblin town opens Trail maps, which shows a lair far off in the fog and the other town as seen
    /// ground.
    /// </summary>
    [Fact]
    public void TakingATownOpensTrailMapsWhichShowLairsAndTowns()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(200, 24, 200, Ground));
        sim.Fog = true;
        Hall(sim, Settlers, 8, 76);
        var goblins = sim.Cities.Place(sim.Content.CityState("goblins"), 131, 79)!;
        var ogres = sim.Cities.Place(sim.Content.CityState("ogres"), 60, 180)!;
        var lair = sim.Monsters.Place(sim.Content.Unit("wolf"), 180, 20)!;
        var maps = Tech(sim, "trail_maps");
        sim.Step();
        Assert.Equal(ResearchResult.NotDiscovered, sim.Economy.CanResearch(Settlers, maps));

        var squad = Squad(sim, Settlers, "spearman", goblins.X - 2, goblins.Z + 3, 12);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(Settlers, squad, sim.Cities.Totem(goblins)!.Id));
        RunUntil(sim, () => goblins.Holder == Settlers, 90 * Tps, "the squad takes the town");
        Assert.True(sim.Economy.Discovered(Settlers, Discovery.Town));
        Assert.True(TechDiscoveredEvent(sim, Settlers, maps));
        foreach (int id in squad) if (sim.Units.Exists(id)) sim.Units.Remove(id);

        Assert.Equal(Sight.Unseen, sim.Vision.At(Settlers, lair.X, lair.Z));
        Assert.Equal(Sight.Unseen, sim.Vision.At(Settlers, ogres.X, ogres.Z));
        Research(sim, Settlers, maps);
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, lair.X, lair.Z));
        Assert.Equal(Sight.Seen, sim.Vision.At(Settlers, ogres.X, ogres.Z));
    }

    // --- Rules ------------------------------------------------------------------------------------

    /// <summary>A discovery is the player's for the match and in the state hash; making it twice tells them once.</summary>
    [Fact]
    public void ADiscoveryIsKeptHashedAndToldOnce()
    {
        Simulation Make() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        var a = Make();
        var b = Make();
        Hall(a, Settlers, 8, 8);
        Hall(b, Settlers, 8, 8);
        Assert.Equal(a.StateHash(), b.StateHash());
        a.Economy.Discover(Settlers, Discovery.Lair);
        a.Economy.Discover(Settlers, Discovery.Lair);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        Assert.Single(a.Events.All, e => e.Kind == GameEventKind.TechDiscovered);
        // The wild side and nobody make no discoveries.
        a.Economy.Discover(Wild, Discovery.Ruin);
        a.Economy.Discover(-1, Discovery.Ruin);
        Assert.False(a.Economy.Discovered(Wild, Discovery.Ruin));
    }

    [Fact]
    public void DiscoveryTechsAreData()
    {
        var content = ContentLoader.LoadEmbedded();
        var charts = content.Techs.First(t => t.Id == "wardens_charts");
        Assert.Equal(Discovery.Ruin, charts.Discovery);
        Assert.Equal(TechEffectType.Reveal, Assert.Single(charts.Effects).Type);
        Assert.Equal(MapFeature.Ruins, charts.Effects[0].Reveal);
        Assert.Equal(TechEffectType.SpareWorkers, Assert.Single(content.Techs.First(t => t.Id == "beast_lore").Effects).Type);
        Assert.Equal(Discovery.None, content.Techs.First(t => t.Id == "farming").Discovery);

        var files = new Dictionary<string, string>();
        foreach (var f in ContentLoader.Files) files[f] = ContentLoader.EmbeddedText(f)!;
        files["techs.json"] = "[ { \"id\": \"x\", \"name\": \"X\", \"researchTime\": 5, \"discovery\": \"cave\"," +
            " \"effects\": [ { \"type\": \"reveal\", \"what\": \"dragons\" }, { \"type\": \"reveal\" } ] } ]";
        var e = Assert.Throws<ContentException>(() => ContentLoader.Load(files));
        Assert.Contains(e.Errors, m => m.Contains("\"discovery\" is \"cave\""));
        Assert.Contains(e.Errors, m => m.Contains("\"what\" is \"dragons\""));
        Assert.Contains(e.Errors, m => m.Contains("effect 2") && m.Contains("\"what\""));
    }
}
