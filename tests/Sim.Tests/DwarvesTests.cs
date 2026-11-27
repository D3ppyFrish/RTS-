using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Dwarves, the mountain clans (see "The Dwarves" in docs/rules.md): their own ironshields, crossbowmen and runesmiths
/// in place of everyone's spearmen and riders, archers and mages, all seeing in the dark; stonework in place of the
/// barrier, a stone rampart from the first day; and the Book of Grudges (<see cref="TechEffectType.Grudges"/>): an enemy
/// that kills one of theirs is marked for life, takes more from their blows and shows through the fog of war.
/// </summary>
public class DwarvesTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, bool fog = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = fog;
        return sim;
    }

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit at {x}, {z}");

    private static Building Hall(Simulation sim, int owner, int x, int z, int amount = 500)
    {
        var hall = Place(sim, owner, "town_hall", x, z);
        for (int g = 0; g < hall.Stock.Length; g++) hall.Stock[g] = amount;
        return hall;
    }

    private static int Count(Simulation sim, int owner, string unit)
    {
        int def = sim.Content.Unit(unit).Index, n = 0;
        for (int i = 0; i < sim.Units.Count; i++)
            if (sim.Units.Owner[i] == owner && sim.Units.Def[i] == def) n++;
        return n;
    }

    private static int Run(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return t;
            sim.Step();
        }
        Assert.True(done(), $"timed out waiting for {what}");
        return maxTicks;
    }

    /// <summary>
    /// Player 1's spearman kills a worker of player 0's (a Dwarf player, with the Book of Grudges if
    /// <paramref name="grudges"/>). The spearman's id.
    /// </summary>
    private static int KillAWorker(Simulation sim, bool grudges)
    {
        sim.SetFaction(0, "dwarves");
        Hall(sim, 0, 4, 4);
        Hall(sim, 1, 4, 54);
        if (grudges) sim.Economy.Grant(0, sim.Content.Tech("dwarf_grudges"));
        int worker = sim.SpawnUnit(0, "worker", 30, 30);
        int spear = sim.SpawnUnit(1, "spearman", 31, 30);
        sim.Units.Hp[sim.Units.IndexOf(worker)] = 1;
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { spear }, worker));
        Run(sim, () => sim.Units.IndexOf(worker) < 0, 10 * Tps, "the worker to be killed");
        return spear;
    }

    [Fact]
    public void TheDwarvesAreTheFifthFaction()
    {
        var content = ContentDb.Default;
        var dwarves = content.Faction("dwarves");
        Assert.Equal(4, dwarves.Index);
        Assert.Equal("Dwarves", dwarves.Name);
        Assert.Null(dwarves.Experience);
        Assert.False(dwarves.TamesMonsters);
        Assert.Equal(DuskTreeUse.Raze, dwarves.DuskTree);
        Assert.Equal(AiStyle.Balanced, dwarves.AiStyle);
        Assert.Equal(BuildingLook.Standard, dwarves.Look);
        Assert.Equal(new[] { ("spearman", "ironshield"), ("rider", "ironshield"), ("archer", "crossbowman"), ("mage", "runesmith") },
            dwarves.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        Assert.Equal(new[] { ("barrier", "stonework") }, dwarves.BuildingReplacements.Select(r => (r.From.Id, r.To.Id)));
        // Harder to kill than everyone's, no faster, and they all see in the dark.
        var iron = content.Unit("ironshield");
        Assert.True(iron.Speed <= content.Unit("spearman").Speed);
        Assert.True(iron.MaxHp > content.Unit("spearman").MaxHp);
        Assert.True(iron.Shield > 0);
        var bow = content.Unit("crossbowman");
        Assert.True(bow.Damage >= 2 * content.Unit("archer").Damage);
        Assert.True(bow.AttackTicks > content.Unit("archer").AttackTicks);
        Assert.True(bow.Range < content.Unit("archer").Range);
        var rune = content.Unit("runesmith");
        Assert.True(rune.EarthWall && rune.Chasm);
        Assert.False(rune.IceBridge || rune.GrowForest);
        Assert.All(new[] { iron, bow, rune }, d => Assert.True(d.DarkSight, d.Id));
    }

    [Fact]
    public void TheDwarvesStartWithTheirOwnUnitsAndNoHorses()
    {
        var content = ContentDb.Default;
        var start = content.Faction("dwarves").StartUnits;
        Assert.Equal(new[] { "worker", "ironshield", "crossbowman", "ironshield", "sapper", "runesmith" }, start.Select(u => u.Id));

        var sim = Flat(96);
        sim.SetFaction(1, "dwarves");
        sim.Map.Starts.Add(new StartArea(0, 20, Ground + 1, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Ground + 1, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "mage") + Count(sim, 1, "rider"));
        Assert.Equal(4, Count(sim, 1, "ironshield")); // in place of the spearmen and the riders
        Assert.Equal(2, Count(sim, 1, "crossbowman"));
        Assert.Equal(2, Count(sim, 1, "runesmith"));
        Assert.Equal(2, Count(sim, 1, "sapper"));
        Assert.Equal(2, Count(sim, 0, "rider"));
    }

    [Fact]
    public void TheDwarvesTrainTheirOwnAtTheBarracks()
    {
        var sim = Flat();
        sim.SetFaction(1, "dwarves");
        var econ = sim.Economy;
        foreach (var id in new[] { "spearman", "archer", "mage", "rider" })
            Assert.False(econ.Unlocked(1, sim.Content.Unit(id)), id);
        Assert.True(econ.Unlocked(1, sim.Content.Unit("ironshield")));
        Assert.False(econ.Unlocked(0, sim.Content.Unit("ironshield"))); // another faction's
        Assert.Contains("crossbowman", sim.Content.Building("barracks").Trains);

        Hall(sim, 1, 10, 10);
        var barracks = Place(sim, 1, "barracks", 30, 10);
        sim.Schedule(0, new TrainCommand(1, barracks.Id, "ironshield"));
        Run(sim, () => Count(sim, 1, "ironshield") == 1, 30 * Tps, "an ironshield to be trained");
    }

    [Fact]
    public void TheDwarvesLayStoneworkInsteadOfBarriers()
    {
        var sim = Flat();
        sim.SetFaction(1, "dwarves");
        var econ = sim.Economy;
        var stonework = sim.Content.Building("stonework");
        Assert.True(econ.Unlocked(1, stonework));
        Assert.False(econ.Unlocked(0, stonework));
        foreach (var id in new[] { "barrier", "wooden_wall", "stone_wall" })
            Assert.False(econ.Unlocked(1, sim.Content.Building(id)), id);
        Assert.Null(stonework.Upgrade);
        Assert.Equal(BlockType.Rampart, stonework.Wall);
        Assert.True(stonework.Walkway && stonework.Thick);
        Assert.Contains(stonework.Cost, c => c.Good == Good.Stone);
        Assert.Contains(stonework.Cost, c => c.Good == Good.Wood && c.Amount <= sim.Content.Building("barrier").Cost.Single(b => b.Good == Good.Wood).Amount);
        // Sturdier than a barrier, as sturdy as everyone's wooden wall, and no taller.
        Assert.True(stonework.Sturdy >= sim.Content.Building("wooden_wall").Sturdy);
        Assert.Equal(sim.Content.Building("wooden_wall").WallHeight, stonework.WallHeight);

        // The wall tool (which lays everyone's barrier) lays their stonework instead, paid for in stone.
        Hall(sim, 0, 4, 4);
        var hall = Hall(sim, 1, 4, 40);
        int dwarf = sim.SpawnUnit(1, "worker", 20, 50), settler = sim.SpawnUnit(0, "worker", 20, 14);
        int stone = econ.Stockpile(1)[(int)Good.Stone];
        sim.Schedule(0, new WallCommand(1, new[] { dwarf }, "barrier", 20, 52, 24, 52));
        sim.Schedule(0, new WallCommand(0, new[] { settler }, "barrier", 20, 16, 24, 16));
        sim.Step();
        var walls = sim.Buildings.All.Where(b => b.Owner == 1 && b.Def.IsWall).ToList();
        Assert.Equal(5, walls.Count);
        Assert.All(walls, b => Assert.Equal("stonework", b.Def.Id));
        Assert.All(sim.Buildings.All.Where(b => b.Owner == 0 && b.Def.IsWall), b => Assert.Equal("barrier", b.Def.Id));
        Run(sim, () => walls.All(b => b.Active), 2 * 60 * Tps, "the stonework to be laid");
        Assert.True(econ.Stockpile(1)[(int)Good.Stone] < stone);
        int top = walls[0].Y + walls[0].Shape.Height - 1;
        Assert.Equal(BlockType.Rampart, sim.World.Get(walls[0].X, top, walls[0].Z));
    }

    [Fact]
    public void AnEnemyThatKillsADwarfIsMarkedWithAGrudge()
    {
        var sim = Flat();
        int spear = KillAWorker(sim, grudges: true);
        int s = sim.Units.IndexOf(spear);
        Assert.True(sim.Combat.Bears(0, s));
        Assert.False(sim.Combat.Bears(1, s));
        Assert.Equal(1, sim.Combat.GrudgesSworn);
        Assert.Equal(1 << 0, sim.Units.Grudged[s]);

        // The dwarves' blows on it deal half as much again; on anyone else, the same as ever.
        int iron = sim.SpawnUnit(0, "ironshield", 33, 30);
        int other = sim.SpawnUnit(1, "spearman", 33, 33);
        var def = sim.Content.Unit("ironshield");
        int i = sim.Units.IndexOf(iron);
        int marked = sim.Combat.Damage(i, def, sim.Units.IndexOf(spear));
        int plain = sim.Combat.Damage(i, def, sim.Units.IndexOf(other));
        Assert.InRange(marked, plain * 150 / 100, (plain + 1) * 150 / 100); // half as much again, before rounding
        // Nobody else's blows on it change.
        int third = sim.SpawnUnit(2, "spearman", 40, 40);
        var spearDef = sim.Content.Unit("spearman");
        Assert.Equal(sim.Combat.Damage(sim.Units.IndexOf(third), spearDef, sim.Units.IndexOf(other)),
            sim.Combat.Damage(sim.Units.IndexOf(third), spearDef, sim.Units.IndexOf(spear)));
    }

    [Fact]
    public void WithoutTheBookOfGrudgesNoGrudgeIsKept()
    {
        var sim = Flat();
        int spear = KillAWorker(sim, grudges: false);
        int s = sim.Units.IndexOf(spear);
        Assert.False(sim.Combat.Bears(0, s));
        Assert.Equal(0, sim.Units.Grudged[s]);
        Assert.Equal(0, sim.Combat.GrudgesSworn);
        Assert.Equal(0, sim.Economy.GrudgePercent(0));
    }

    [Fact]
    public void AGrudgeIsKeptForLifeAndOutlastsTheFight()
    {
        var sim = Flat();
        int spear = KillAWorker(sim, grudges: true);
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spear }, 10, 58));
        for (int t = 0; t < 60 * Tps; t++) sim.Step();
        Assert.True(sim.Combat.Bears(0, sim.Units.IndexOf(spear)));
        // The ironshields that catch up with it hit it the harder: the blows count.
        int iron = sim.SpawnUnit(0, "ironshield", 12, 56);
        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { iron }, spear));
        Run(sim, () => sim.Combat.GrudgeBlows > 0, 30 * Tps, "a blow for the grudge");
    }

    [Fact]
    public void AMarkedEnemyShowsThroughTheFog()
    {
        var sim = Flat(fog: true);
        int spear = KillAWorker(sim, grudges: true);
        // It walks off far out of sight of the dwarves' hall and units: they still see it, its own side does too, others do not.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spear }, 60, 60));
        Run(sim, () => sim.Units.X[sim.Units.IndexOf(spear)].FloorToInt() >= 59, 60 * Tps, "the spearman to walk off");
        sim.Run(VisionSystem.UpdateTicks);
        int s = sim.Units.IndexOf(spear);
        Assert.False(sim.Vision.Sees(0, 60, 60));
        Assert.True(sim.Vision.SeesUnit(0, s));
        Assert.False(sim.Vision.SeesUnit(2, s));
        // An unmarked one there is not seen.
        int other = sim.SpawnUnit(1, "spearman", 61, 60);
        Assert.False(sim.Vision.SeesUnit(0, sim.Units.IndexOf(other)));
    }

    [Fact]
    public void DwarfWorkersAreBornMiners()
    {
        var content = ContentDb.Default;
        var tools = content.Tech("tools");
        var dwarves = content.Faction("dwarves");
        var sound = tools.Effects.Single(e => e.Type == TechEffectType.Prospecting && e.Faction == dwarves);
        var foreman = tools.Effects.Single(e => e.Type == TechEffectType.Prospecting && e.Unit?.Id == "foreman");
        var orcs = tools.Effects.Single(e => e.Type == TechEffectType.Prospecting && e.Faction?.Id == "orcs");
        Assert.True(sound.Ticks < foreman.Ticks);
        Assert.True(sound.Amount > orcs.Amount); // the radius they sound
        Assert.Contains(tools.Effects, e => e.Type == TechEffectType.Felling && e.Faction == dwarves);
    }

    [Fact]
    public void TheDwarvesFinalTechOpensTheMountainHall()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("dwarf_king");
        Assert.Equal("dwarves", tech.Faction!.Id);
        Assert.Contains(tech.Requires, t => t.Id == "dwarf_grudges");
        var hall = content.Building("mountain_hall");
        Assert.Equal(BuildingKind.GreatWork, hall.Kind);
        Assert.Equal("dwarves", hall.Faction!.Id);
        Assert.Equal(TechEffectType.Grudges, content.Tech("dwarf_grudges").Effects.Single().Type);
        Assert.Equal(50, content.Tech("dwarf_grudges").Effects.Single().Percent);
    }
}
