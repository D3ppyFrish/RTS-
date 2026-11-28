using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Fey, tricksters of old magic (see "The Fey" in docs/rules.md): their own sprites, wisps and enchanters in place of
/// everyone's spearmen, riders, archers and mages; wisps and enchanters paid in mana (<see cref="UnitDef.Mana"/>); and
/// mana from nature instead of crystal: the fairy ring in place of the shrine, channelling from the living trees round
/// it with no keeper (<see cref="BuildingDef.Nature"/>), whose trees their automatic workers and computer players spare.
/// </summary>
public class FeyTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;
    private const int P = 0;

    private static Simulation Flat(int size = 64)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.SetFaction(P, "fey");
        return sim;
    }

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
    public void TheFeyAreTheEighthFaction()
    {
        var content = ContentDb.Default;
        var fey = content.Faction("fey");
        Assert.Equal(7, fey.Index);
        Assert.Equal("Fey", fey.Name);
        Assert.Equal(DuskTreeUse.Heal, fey.DuskTree);
        Assert.Equal(AiStyle.Waves, fey.AiStyle);
        Assert.Equal(new[] { ("spearman", "sprite"), ("rider", "sprite"), ("archer", "wisp"), ("mage", "enchanter") },
            fey.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        Assert.Equal("fairy_ring", fey.Own(content.Building("shrine")).Id);
        // Cheap and frail: a sprite costs half a spearman and has well under its hit points.
        var sprite = content.Unit("sprite");
        Assert.True(sprite.MaxHp < content.Unit("spearman").MaxHp && sprite.Damage < content.Unit("spearman").Damage);
        Assert.True(sprite.TrainTicks < content.Unit("spearman").TrainTicks);
        Assert.Equal(0, sprite.Mana);
        Assert.True(content.Unit("wisp").Range > content.Unit("archer").Range);
        Assert.True(content.Unit("wisp").Mana > 0 && content.Unit("enchanter").Mana > 0);
        var enchanter = content.Unit("enchanter");
        Assert.True(enchanter.EarthWall && enchanter.IceBridge && enchanter.Chasm && enchanter.GrowForest);
        var ring = content.Building("fairy_ring");
        Assert.Equal(BuildingKind.Shrine, ring.Kind);
        Assert.NotNull(ring.Nature);
        Assert.Equal(0, ring.WorkerSlots);
        Assert.Empty(ring.Requires);
        Assert.Null(content.Building("shrine").Nature);
        Assert.Equal("fey", content.Building("hollow_hill").Faction?.Id);
        Assert.Equal(BuildingKind.GreatWork, content.Building("hollow_hill").Kind);
        Assert.Equal("fey", content.Tech("fey_high_court").Faction?.Id);
    }

    [Fact]
    public void TheFeyStartWithTheirOwnUnitsAndBuildFairyRingsNotShrines()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
        sim.SetFaction(1, "fey");
        sim.Map.Starts.Add(new StartArea(0, 20, Stand, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Stand, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "mage") + Count(sim, 1, "rider"));
        Assert.Equal(Count(sim, 0, "spearman") + Count(sim, 0, "rider"), Count(sim, 1, "sprite"));
        Assert.Equal(Count(sim, 0, "archer"), Count(sim, 1, "wisp"));
        Assert.Equal(Count(sim, 0, "mage"), Count(sim, 1, "enchanter"));
        Assert.True(sim.Economy.Unlocked(1, sim.Content.Building("fairy_ring")));
        Assert.False(sim.Economy.Unlocked(1, sim.Content.Building("shrine")));
        Assert.False(sim.Economy.Unlocked(0, sim.Content.Building("fairy_ring")));
        Assert.False(sim.Economy.Unlocked(0, sim.Content.Unit("wisp")));
    }

    [Fact]
    public void AFairyRingChannelsManaFromTheTreesRoundItWithNoKeeper()
    {
        var sim = Flat();
        var ring = Place(sim, P, "fairy_ring", 30, 30);
        var nature = ring.Def.Nature!;
        int cycle = ring.Def.ChannelTicks;
        var p = sim.Economy.Player(P);

        // No trees: nothing.
        int start = p.Mana;
        sim.Run(3 * cycle);
        Assert.Equal(0, sim.Economy.NatureTrees(ring));
        Assert.Equal(start, p.Mana);

        // Three trees within its reach, one just beyond it: three trees' mana a round.
        TestWorlds.Tree(sim.World, 26, 30, 4);
        TestWorlds.Tree(sim.World, 30, 26, 4);
        TestWorlds.Tree(sim.World, 32 + nature.Reach, 34, 4);
        TestWorlds.Tree(sim.World, 33 + nature.Reach, 31, 4); // one column too far
        Assert.Equal(3, sim.Economy.NatureTrees(ring));
        start = p.Mana;
        sim.Run(4 * cycle);
        Assert.Equal(4 * 3 * ring.Def.Mana, p.Mana - start);

        // A felled tree is gone from its count.
        for (int y = Stand; y < Stand + 4; y++) sim.World.Set(26, y, 30, BlockType.Air);
        Assert.Equal(2, sim.Economy.NatureTrees(ring));

        // A wood bigger than it counts gives its most, no more.
        for (int z = 22; z <= 40; z += 2)
        for (int x = 22; x <= 40; x += 4)
            if (sim.Buildings.AtColumn(x, z) == null && sim.World.Get(x, Stand, z) == BlockType.Air) TestWorlds.Tree(sim.World, x, z, 4);
        Assert.Equal(nature.Trees, sim.Economy.NatureTrees(ring));
        start = p.Mana;
        sim.Run(2 * cycle);
        Assert.Equal(2 * nature.Trees * ring.Def.Mana, p.Mana - start);
        Assert.True(p.ManaChannelled > 0);
    }

    [Fact]
    public void WildWoodsDoublesTheTreesAFairyRingCountsAtMost()
    {
        var sim = Flat();
        var ring = Place(sim, P, "fairy_ring", 30, 30);
        for (int z = 22; z <= 40; z += 2)
        for (int x = 22; x <= 40; x += 2)
            if (sim.Buildings.AtColumn(x, z) == null && sim.World.Get(x, Stand, z) == BlockType.Air) TestWorlds.Tree(sim.World, x, z, 4);
        int most = ring.Def.Nature!.Trees;
        Assert.Equal(most, sim.Economy.NatureTrees(ring));
        var tech = sim.Content.Tech("fey_wild_woods");
        Assert.Equal("fey", tech.Faction?.Id);
        Assert.Contains(tech, sim.Content.Tech("fey_high_court").Requires);
        sim.Economy.Grant(P, tech);
        Assert.Equal(2 * most, sim.Economy.MostTrees(ring.Def, P));
        Assert.Equal(2 * most, sim.Economy.NatureTrees(ring));
        int start = sim.Economy.Player(P).Mana;
        sim.Run(ring.Def.ChannelTicks + 1);
        Assert.Equal(2 * most * ring.Def.Mana, sim.Economy.Player(P).Mana - start);
    }

    [Fact]
    public void AFairyRingStillBeingBuiltChannelsNothing()
    {
        var sim = Flat();
        TestWorlds.Tree(sim.World, 26, 30, 4);
        var store = Place(sim, P, "storehouse", 10, 10);
        foreach (var g in Goods.All) store.Stock[(int)g] = 500;
        sim.Schedule(0, new BuildCommand(P, Array.Empty<int>(), "fairy_ring", 30, 30));
        sim.Step();
        var ring = sim.Buildings.All.Single(b => b.Def.Id == "fairy_ring");
        Assert.Equal(BuildingState.Site, ring.State);
        int start = sim.Economy.Player(P).Mana;
        sim.Run(3 * ring.Def.ChannelTicks);
        Assert.Equal(start, sim.Economy.Player(P).Mana);
    }

    [Fact]
    public void WispsAndEnchantersArePaidForInMana()
    {
        var sim = Flat();
        sim.Economy.Grant(P, sim.Content.Tech("basic_magic"));
        var barracks = Place(sim, P, "barracks", 20, 20);
        var store = Place(sim, P, "storehouse", 10, 10);
        foreach (var g in Goods.All) store.Stock[(int)g] = 500;
        var p = sim.Economy.Player(P);
        var wisp = sim.Content.Unit("wisp");
        var enchanter = sim.Content.Unit("enchanter");

        // Not enough mana: nothing is queued and no goods are taken.
        p.Mana = wisp.Mana - 1;
        int food = store.Stock[(int)Good.Food];
        sim.Schedule(sim.Tick, new TrainCommand(P, barracks.Id, "wisp"));
        sim.Step();
        Assert.Empty(barracks.Queue);
        Assert.Equal(food, store.Stock[(int)Good.Food]);

        // Enough: queued, and the mana is spent with the goods.
        p.Mana = wisp.Mana + enchanter.Mana;
        sim.Schedule(sim.Tick, new TrainCommand(P, barracks.Id, "wisp"));
        sim.Schedule(sim.Tick, new TrainCommand(P, barracks.Id, "enchanter"));
        sim.Step();
        Assert.Equal(new[] { wisp.Index, enchanter.Index }, barracks.Queue);
        Assert.Equal(0, p.Mana);
        // A sprite costs no mana.
        sim.Schedule(sim.Tick, new TrainCommand(P, barracks.Id, "sprite"));
        sim.Step();
        Assert.Equal(3, barracks.Queue.Count);
    }

    [Fact]
    public void AutomaticWorkersLeaveTheTreesRoundAFairyRingStanding()
    {
        var sim = Flat(96);
        var store = Place(sim, P, "storehouse", 10, 10);
        var ring = Place(sim, P, "fairy_ring", 20, 20);
        TestWorlds.Tree(sim.World, 18, 18, 4);           // by the ring, near the storehouse
        TestWorlds.Tree(sim.World, 40, 40, 4);           // further out, beyond the ring's reach
        int w = sim.SpawnUnit(P, "worker", 16, 16);
        sim.Schedule(0, new AutomateCommand(P, new[] { w }, true));
        sim.Run(2 * Tps);
        int i = sim.Units.IndexOf(w);
        Assert.Equal(UnitJob.Gather, sim.Units.Job[i]);
        Assert.Equal(40, sim.Units.JobX[i]);
        Assert.Equal(40, sim.Units.JobZ[i]);
        Assert.True(BlockInfo.IsTrunk(sim.World.Get(18, Stand, 18)));
        Assert.Equal(1, sim.Economy.NatureTrees(ring));

        // Another faction's worker cuts the nearer one: it is the Fey's ring, not theirs.
        var other = Flat(96);
        Place(other, 1, "storehouse", 10, 10);
        Place(other, P, "fairy_ring", 20, 20);
        TestWorlds.Tree(other.World, 18, 18, 4);
        TestWorlds.Tree(other.World, 40, 40, 4);
        int theirs = other.SpawnUnit(1, "worker", 16, 16);
        other.Schedule(0, new AutomateCommand(1, new[] { theirs }, true));
        other.Run(2 * Tps);
        Assert.Equal(18, other.Units.JobX[other.Units.IndexOf(theirs)]);
    }

    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void AComputerPlayerLaysOutAFairyRingInTheWoods(AiLevel level)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
        sim.SetFaction(0, "fey");
        sim.Map.Starts.Add(new StartArea(0, 64, Stand, 64));
        sim.PlaceStartingBuildings();
        sim.Fog = false;
        var hall = sim.Economy.TownHall(0)!;
        // A wood south-east of the town hall, nothing else round it.
        for (int z = hall.MaxZ + 12; z <= hall.MaxZ + 24; z += 3)
        for (int x = hall.MaxX + 10; x <= hall.MaxX + 22; x += 3)
            TestWorlds.Tree(sim.World, x, z, 4);
        var ai = new AiPlayer(0, 1, level);
        var log = new List<Command>();
        Assert.True(AiTests.PlayLogged(sim, new[] { ai }, log, () => log.OfType<BuildCommand>().Any(c => c.Building == "fairy_ring"), 60 * Tps),
            $"{level}: a fairy ring");
        var build = log.OfType<BuildCommand>().First(c => c.Building == "fairy_ring");
        Assert.True(sim.Economy.TreesAround(sim.Content.Building("fairy_ring"), build.X, build.Z, 0) >= AiPlayer.RingLeast,
            $"{level}: the ring at {build.X},{build.Z} stands among trees");
        Assert.DoesNotContain(log, c => c is BuildCommand b && b.Building == "shrine");
    }
}
