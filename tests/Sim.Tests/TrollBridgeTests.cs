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
/// The troll bridge: a city-state on a river bank with a stone bridge it built over the river, which takes a toll in
/// gold from everyone who comes onto its ground, and sells trolls whose wounds close only out of the daylight.
/// </summary>
public class TrollBridgeTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;
    private const int TownX = 131, TownZ = 79;
    private const int HallX = 8, HallZ = 76;

    private static Simulation Flat(int size = 160) => TestWorlds.Sim(TestWorlds.Flat(size, 24, size, Ground));

    private static CityState PlaceBridge(Simulation sim) =>
        sim.Cities.Place(sim.Content.CityState("trolls"), TownX, TownZ) ?? throw new InvalidOperationException("no ground for the town");

    private static Building Hall(Simulation sim, int owner, int x = HallX, int z = HallZ, int food = 0, int gold = 0)
    {
        var b = sim.Economy.PlaceFinished(owner, sim.Content.Building("town_hall"), x, z) ?? throw new InvalidOperationException("the hall does not fit");
        b.Stock[(int)Good.Food] = food;
        b.Stock[(int)Good.Gold] = gold;
        return b;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
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

    private static bool Hurt(Simulation sim, int id) =>
        !sim.Units.Exists(id) || sim.Units.Hp[Index(sim, id)] < sim.Content.Units[sim.Units.Def[Index(sim, id)]].MaxHp;

    /// <summary>
    /// The player's spearmen (many, as trolls are tough) break the troll stone, and it stands again in their hands;
    /// the spearmen left are removed.
    /// </summary>
    private static void Conquer(Simulation sim, CityState town, int player, int spearmen = 40)
    {
        var squad = Enumerable.Range(0, spearmen).Select(k => sim.SpawnUnit(player, "spearman", town.X - 4 + k % 8, town.Z + 3 + k / 8)).ToArray();
        Do(sim, new AttackBuildingCommand(player, squad, sim.Cities.Totem(town)!.Id));
        RunUntil(sim, () => town.Holder == player && town.TotemId != 0, 120 * Tps, "the squad breaks the stone and it stands again");
        foreach (int id in squad) sim.Units.Remove(id);
        sim.Step();
    }

    [Fact]
    public void TheTrollsAreData()
    {
        var db = ContentDb.Default;
        var trolls = db.CityState("trolls");
        Assert.Equal(3, trolls.Index); // placed last, after the kobolds
        Assert.True(trolls.Crossing);
        Assert.False(trolls.Deep);
        Assert.Equal((2, 60 * Tps), (trolls.TollGold, trolls.FeudTicks));
        Assert.Equal(new[] { new GoodAmount(Good.Gold, 2) }, trolls.TollCost);
        Assert.Same(db.Building("troll_stone"), trolls.Totem);
        Assert.Equal(BuildingKind.Totem, trolls.Totem!.Kind);
        Assert.Equal(new[] { "troll", "troll", "troll", "troll" }, trolls.Guards.Select(u => u.Id));
        Assert.Equal((Good.Iron, 20), (trolls.TributeGood, trolls.TributeAmount));
        Assert.Equal((Good.Food, Good.Iron, 6), (trolls.Buys, trolls.Sells, trolls.Price));
        Assert.Equal(new[] { "troll" }, trolls.Mercenaries.Select(u => u.Id));
        Assert.Null(trolls.Lair);
        Assert.Equal(BlockType.Stone, trolls.Ground);
        // Heavy infantry, tougher than an ogre brute, that heals fast, but only out of the daylight.
        var troll = db.Unit("troll");
        Assert.Equal(UnitRole.HeavyInfantry, troll.Role);
        Assert.True(troll.MaxHp > db.Unit("ogre_brute").MaxHp);
        Assert.Equal(2, troll.Regen);
        Assert.True(troll.NightRegen);
        Assert.Same(trolls, troll.HiredFrom);
        Assert.All(db.Units.Where(u => u != troll), u => Assert.False(u.NightRegen));
        // The other towns take no toll and lay no bridge.
        Assert.All(db.CityStates.Where(c => c != trolls), c => Assert.Equal((0, false), (c.TollGold, c.Crossing)));
    }

    [Fact]
    public void TollDataIsChecked()
    {
        var files = ContentLoader.Files.ToDictionary(f => f, f => ContentLoader.EmbeddedText(f)!);
        string Errors(string old, string replacement)
        {
            var edited = new Dictionary<string, string>(files) { ["citystates.json"] = files["citystates.json"].Replace(old, replacement) };
            Assert.NotEqual(files["citystates.json"], edited["citystates.json"]);
            var ex = Assert.Throws<ContentException>(() => ContentLoader.Load(edited));
            return string.Join("\n", ex.Errors);
        }
        Assert.Contains("gold", Errors("\"toll\": { \"gold\": 2,", "\"toll\": { \"gold\": 0,"));
        Assert.Contains("tolls", Errors("\"toll\": { \"gold\": 2,", "\"toll\": { \"tolls\": 2,"));
        Assert.Contains("deep", Errors("\"id\": \"trolls\", \"name\": \"Troll bridge\", \"crossing\": true,",
            "\"id\": \"trolls\", \"name\": \"Troll bridge\", \"crossing\": true, \"deep\": true,"));
    }

    /// <summary>
    /// On the default map the trolls' town stands on a bank of the river, fair to both starts, with a stone bridge three
    /// columns wide from the edge of its square over the river to the far bank: a unit walks from the market across it
    /// to the other side, and nobody can build on it.
    /// </summary>
    [Theory]
    [InlineData(1234UL)]
    [InlineData(7UL)]
    public void TheTrollBridgeSpansTheRiver(ulong seed)
    {
        var sim = CavernTests.DefaultMatch(seed);
        var w = sim.World;
        Assert.Equal(new[] { "goblins", "ogres", "kobolds", "trolls", "centaurs", "merfolk" }, sim.Cities.Towns.Select(t => t.Def.Id));
        var town = sim.Cities.Towns[3];
        Assert.True(town.HasBridge);
        Assert.Equal("troll_stone", sim.Cities.Totem(town)!.Def.Id);
        Assert.Equal(4, town.Guards.Count());
        var d = sim.Map.Starts.Select(s => IntMath.Distance(s.X, s.Z, town.X, town.Z)).ToArray();
        Assert.InRange(Math.Abs(d[0] - d[1]), 0, 40);
        Assert.All(d, x => Assert.True(x >= w.SizeX * 3 / 16 * 3 / 2, $"the bridge is {x} blocks from a start"));
        foreach (var other in sim.Cities.Towns.Where(t => t != town && !t.Deep))
            Assert.True(IntMath.Distance(town.X, town.Z, other.X, other.Z) > 100, $"{other.Def.Id} is near the bridge");

        // Three wide, out from the square's edge, over the river.
        bool alongX = town.BridgeZ1 - town.BridgeZ0 == 2;
        Assert.Equal(2, alongX ? town.BridgeZ1 - town.BridgeZ0 : town.BridgeX1 - town.BridgeX0);
        Assert.Equal(1, Math.Min(town.ColumnDistance(town.BridgeX0, town.BridgeZ0), town.ColumnDistance(town.BridgeX1, town.BridgeZ1)));
        bool river = false;
        for (int z = town.BridgeZ0; z <= town.BridgeZ1; z++)
        for (int x = town.BridgeX0; x <= town.BridgeX1; x++)
        {
            Assert.Equal(BlockType.StoneBrick, w.Get(x, town.Y - 1, z));
            for (int y = town.Y; y < town.Y + 3; y++) Assert.Equal(BlockType.Air, w.Get(x, y, z));
            if (sim.Map.BiomeAt(x, z) == Biome.River) river = true;
            Assert.Equal(PlaceResult.Crowded, sim.Economy.CanPlace(0, sim.Content.Building("house"), x, z, out _, checkPlayer: false));
        }
        Assert.True(river, "the bridge does not cross the river");

        // On foot from the market over the bridge, to past its far end.
        int dx = alongX ? (town.BridgeX0 > town.X ? 1 : -1) : 0, dz = alongX ? 0 : (town.BridgeZ0 > town.Z ? 1 : -1);
        int farX = alongX ? (dx > 0 ? town.BridgeX1 : town.BridgeX0) + dx : town.X;
        int farZ = alongX ? town.Z : (dz > 0 ? town.BridgeZ1 : town.BridgeZ0) + dz;
        long Key(int x, int y, int z) => ((long)x * w.SizeY + y) * w.SizeZ + z;
        var start = (town.X + 3 * dx, town.Y, town.Z + 3 * dz);
        var seen = new HashSet<long> { Key(start.Item1, start.Item2, start.Item3) };
        var open = new Queue<(int X, int Y, int Z)>();
        open.Enqueue(start);
        bool across = false;
        while (open.Count > 0 && !across && seen.Count < 20000)
        {
            var (x, y, z) = open.Dequeue();
            if (x == farX && z == farZ) across = true;
            foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + sx, nz = z + sz;
                // Keep to the bridge's row (or column) and the square, so it does not wade a ford round it.
                if (alongX ? Math.Abs(nz - town.Z) > 1 : Math.Abs(nx - town.X) > 1) continue;
                if (w.InBounds(nx, 0, nz) && Walk.TryStep(w, x, z, y, nx, nz, out int ny) && seen.Add(Key(nx, ny, nz))) open.Enqueue((nx, ny, nz));
            }
        }
        Assert.True(across, $"no way across the bridge from the market to {farX},{farZ}");
        Assert.True(town.OnTollGround(farX - dx, town.Y, farZ - dz), "the bridge's far end is not toll ground");
    }

    [Fact]
    public void AMapWithoutARiverHasTheTrollsWithoutABridge()
    {
        var sim = Flat(320);
        var trolls = sim.Cities.PlaceAll().Single(t => t.Def.Id == "trolls");
        Assert.False(trolls.HasBridge);
        Assert.Equal(int.MaxValue, trolls.BridgeDistance(trolls.X, trolls.Z));
    }

    /// <summary>
    /// The toll: a Settlers' worker that walks onto the trolls' ground is paid for (2 gold from the Settlers' storage)
    /// once, and walks through unhurt; so does a spearman, as it paid. Gone, they would pay again. With no gold left, the
    /// next worker cannot be paid for: the Settlers hear so and the trolls set on it.
    /// </summary>
    [Fact]
    public void UnitsThatPayTheTollCrossAndThoseThatCannotAreFought()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, gold: 4);
        var town = PlaceBridge(sim);
        Assert.False(town.HasBridge); // no river here
        int z = town.Z + 3;
        int worker = sim.SpawnUnit(Settlers, "worker", town.X - 16, z);
        int spearman = sim.SpawnUnit(Settlers, "spearman", town.X - 16, z + 1);
        Do(sim, new MoveCommand(Settlers, new[] { worker, spearman }, town.X + 16, z));
        RunUntil(sim, () => town.PaidToll(worker) && town.PaidToll(spearman), 20 * Tps, "both are paid for");
        Assert.Equal(0, hall.Stock[(int)Good.Gold]);
        Assert.Equal(4, town.Tolls); // the trolls keep it while nobody holds the bridge
        RunUntil(sim, () => sim.Units.X[Index(sim, worker)] > Fix64.FromInt(town.X + 14) && sim.Units.X[Index(sim, spearman)] > Fix64.FromInt(town.X + 14),
            40 * Tps, "they cross the town");
        sim.Run(2 * Tps);
        Assert.False(Hurt(sim, worker));
        Assert.False(Hurt(sim, spearman));
        Assert.False(town.PaidToll(worker)); // off the toll ground: next time it pays again
        Assert.Equal(0, sim.Events.All.Count(e => e.Kind == GameEventKind.TollUnpaid));

        // No gold: the next one is fought, worker though it is.
        int poor = sim.SpawnUnit(Settlers, "worker", town.X - 16, z);
        Do(sim, new MoveCommand(Settlers, new[] { poor }, town.X + 16, z));
        RunUntil(sim, () => town.OwesToll(poor), 20 * Tps, "the worker owes the toll");
        var alert = Assert.Single(sim.Events.All, e => e.Kind == GameEventKind.TollUnpaid);
        Assert.Equal((Settlers, poor, town.Def.Index), (alert.Player, alert.Subject, alert.Def));
        RunUntil(sim, () => Hurt(sim, poor), 20 * Tps, "the trolls set on the worker");
    }

    /// <summary>
    /// Once the Settlers hold the bridge, their units cross free and the Orcs pay them: an Orc worker's toll lands in
    /// the Settlers' storage. The Orcs' soldiers who paid are let through by the Settlers' trolls.
    /// </summary>
    [Fact]
    public void TheHolderCrossesFreeAndTakesTheToll()
    {
        var sim = Flat();
        var hall = Hall(sim, Settlers, food: 1000, gold: 10);
        var orcHall = Hall(sim, Orcs, HallX, 20, gold: 10);
        var town = PlaceBridge(sim);
        Conquer(sim, town, Settlers);
        Assert.All(town.Guards, id => Assert.Equal(Settlers, sim.Units.Owner[Index(sim, id)]));
        Assert.Equal(0, town.Tolls);
        int gold = hall.Stock[(int)Good.Gold];

        int own = sim.SpawnUnit(Settlers, "worker", town.X - 16, town.Z + 3);
        int orc = sim.SpawnUnit(Orcs, "worker", town.X - 16, town.Z - 3);
        int orcSpear = sim.SpawnUnit(Orcs, "spearman", town.X - 16, town.Z - 4);
        Do(sim, new MoveCommand(Settlers, new[] { own }, town.X + 16, town.Z + 3));
        Do(sim, new MoveCommand(Orcs, new[] { orc, orcSpear }, town.X + 16, town.Z - 3));
        RunUntil(sim, () => town.PaidToll(orc) && town.PaidToll(orcSpear), 20 * Tps, "the Orcs pay");
        Assert.Equal(6, orcHall.Stock[(int)Good.Gold]);
        Assert.Equal(gold + 4, hall.Stock[(int)Good.Gold]);
        Assert.Equal(4, town.Tolls);
        Assert.False(town.PaidToll(own));
        RunUntil(sim, () => sim.Units.X[Index(sim, orcSpear)] > Fix64.FromInt(town.X + 14), 40 * Tps, "the Orc spearman crosses");
        Assert.False(Hurt(sim, orcSpear));
        Assert.False(Hurt(sim, orc));
        Assert.Equal(gold + 4, hall.Stock[(int)Good.Gold]); // the Settlers' own worker paid nothing
    }

    /// <summary>
    /// A unit that paid and then strikes a troll sets its owner at feud with the bridge: the trolls fight it, and the
    /// owner's worker that comes on after is not let pay but fought too; a minute after the last blow the feud is over.
    /// </summary>
    [Fact]
    public void StrikingTheTrollsSetsYouAtFeud()
    {
        var sim = Flat();
        var hall = Hall(sim, Orcs, HallX, 20, gold: 20);
        var town = PlaceBridge(sim);
        int spear = sim.SpawnUnit(Orcs, "spearman", town.X - 16, town.Z + 3);
        Do(sim, new MoveCommand(Orcs, new[] { spear }, town.X - 4, town.Z + 3));
        RunUntil(sim, () => town.PaidToll(spear), 20 * Tps, "the spearman is paid for");
        Assert.Equal(18, hall.Stock[(int)Good.Gold]);
        Do(sim, new AttackCommand(Orcs, new[] { spear }, town.Guards.First()));
        RunUntil(sim, () => town.FeudWith(Orcs) > 0, 20 * Tps, "the Orcs are at feud");
        RunUntil(sim, () => Hurt(sim, spear), 20 * Tps, "the trolls fight back");

        int worker = sim.SpawnUnit(Orcs, "worker", town.X - 16, town.Z - 3);
        Do(sim, new MoveCommand(Orcs, new[] { worker }, town.X + 16, town.Z - 3));
        RunUntil(sim, () => town.OwesToll(worker), 20 * Tps, "the worker is not let pay");
        Assert.Equal(18, hall.Stock[(int)Good.Gold]);
        RunUntil(sim, () => Hurt(sim, worker), 20 * Tps, "the trolls set on the worker");
        Assert.Contains(sim.Events.All, e => e.Kind == GameEventKind.TollUnpaid && e.Player == Orcs);

        // The feud runs out a minute after the last blow.
        RunUntil(sim, () => !sim.Units.Exists(spear) && !sim.Units.Exists(worker), 60 * Tps, "the trolls kill both");
        RunUntil(sim, () => town.FeudWith(Orcs) == 0, town.Def.FeudTicks + Tps, "the feud is over");
        int again = sim.SpawnUnit(Orcs, "worker", town.X - 16, town.Z - 3);
        Do(sim, new MoveCommand(Orcs, new[] { again }, town.X + 16, town.Z - 3));
        RunUntil(sim, () => town.PaidToll(again), 20 * Tps, "the next worker is paid for");
        Assert.Equal(16, hall.Stock[(int)Good.Gold]);
    }

    /// <summary>
    /// A troll's wounds close 2% a second even in a fight, but not under the open sky by day: by day in the open it
    /// does not regenerate, at night it does, and by day under a roof it does too.
    /// </summary>
    [Theory]
    [InlineData(12 * 60, false, false)] // noon, in the open: no regeneration
    [InlineData(1 * 60, false, true)]   // 1 in the night
    [InlineData(12 * 60, true, true)]   // noon, under a roof
    public void TrollsRegenerateOnlyOutOfTheDaylight(int minute, bool roof, bool heals)
    {
        var sim = Flat(64);
        sim.DayNight = true;
        sim.DayStart = minute;
        int troll = sim.SpawnUnit(Settlers, "troll", 20, 20);
        if (roof)
            for (int z = 16; z <= 24; z++)
            for (int x = 16; x <= 24; x++)
                sim.World.Set(x, Ground + 6, z, BlockType.Stone);
        var u = sim.Units;
        u.Hp[Index(sim, troll)] = 100;
        for (int t = 0; t < 10 * Tps; t++)
        {
            u.LastFight[Index(sim, troll)] = sim.Tick; // still fighting
            sim.Step();
        }
        // 2% of 300 a second is 6: 60 hit points in ten seconds, give or take a second's worth.
        if (heals) Assert.InRange(u.Hp[Index(sim, troll)], 100 + 54, 100 + 60);
        else Assert.Equal(100, u.Hp[Index(sim, troll)]);
    }

    /// <summary>With day and night off it is always a clear day, which changes nothing: trolls regenerate as ogres do.</summary>
    [Fact]
    public void WithoutDayAndNightTrollsAlwaysRegenerate()
    {
        var sim = Flat(64);
        int troll = sim.SpawnUnit(Settlers, "troll", 20, 20);
        sim.Units.Hp[Index(sim, troll)] = 100;
        for (int t = 0; t < 5 * Tps; t++)
        {
            sim.Units.LastFight[Index(sim, troll)] = sim.Tick;
            sim.Step();
        }
        Assert.True(sim.Units.Hp[Index(sim, troll)] > 100);
    }

    /// <summary>The toll and its feuds are in the state hash.</summary>
    [Fact]
    public void TheTollIsInTheHash()
    {
        var a = Flat();
        var b = Flat();
        Hall(a, Settlers, gold: 10);
        Hall(b, Settlers, gold: 10);
        var ta = PlaceBridge(a);
        PlaceBridge(b);
        int wa = a.SpawnUnit(Settlers, "worker", ta.X - 2, ta.Z + 3);
        b.SpawnUnit(Settlers, "worker", ta.X - 2, ta.Z + 3);
        a.Step();
        b.Step();
        Assert.True(ta.PaidToll(wa));
        Assert.Equal(a.Cities.ComputeHash(), b.Cities.ComputeHash());
    }
}
