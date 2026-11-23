using System;
using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Battering rams (the design doc's siege role, "break walls", and the siege concept art's covered ram at the gate): a
/// siege workshop builds them; a ram goes only at buildings, strikes at the foot of a wall so the column falls, breaks
/// gates twice as fast, and shrugs off arrows under its roof.
/// </summary>
public class RamTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[Index(sim, id)];

    private static void Run(Simulation sim, int seconds)
    {
        for (int t = 0; t < seconds * Tps; t++) sim.Step();
    }

    /// <summary>Seconds until the building is gone, or -1 if it still stands after the limit.</summary>
    private static int Until(Simulation sim, Building b, int limit)
    {
        for (int t = 0; t < limit * Tps; t++)
        {
            if (sim.Buildings.Get(b.Id) == null) return t / Tps;
            sim.Step();
        }
        return -1;
    }

    [Fact]
    public void TheRamIsASiegeMachineFromTheSiegeWorkshopThatGoesOnlyAtBuildings()
    {
        var content = Rts.Sim.Content.ContentDb.Default;
        var ram = content.Unit("battering_ram");
        Assert.Equal(UnitRole.Siege, ram.Role);
        Assert.Null(ram.Faction); // every faction builds them
        Assert.Equal("siege_workshop", ram.TrainedAt?.Id);
        Assert.Contains("battering_ram", content.Building("siege_workshop").Trains);
        Assert.DoesNotContain("battering_ram", content.Building("barracks").Trains);
        Assert.Equal(new[] { "smelting" }, ram.Requires.Select(t => t.Id));
        Assert.Contains(ram.Cost, c => c.Good == Good.Planks);
        Assert.True(ram.Ram && ram.Machine && !ram.IsRanged && !ram.Blast && !ram.Lobs);
        Assert.False(ram.Bleeds);
        Assert.False(ram.InStart);
        Assert.Equal(200, ram.Gates);
        Assert.Equal(75, ram.Cover);
        Assert.Empty(ram.Forms);
        // Tougher than a catapult, and slow.
        Assert.True(ram.MaxHp > content.Unit("catapult").MaxHp);
        Assert.True(ram.Speed < content.Unit("spearman").Speed);
        // Nothing else is a ram or deals more to gates, so nothing else changes; only a ship's hull is cover as well.
        Assert.All(content.Units.Where(u => u.Id != "battering_ram"), u => Assert.True(!u.Ram && u.Gates == 100 && u.Cover == (u.IsBoat ? u.Cover : 0), u.Id));
    }

    [Fact]
    public void ASiegeWorkshopTrainsARamForPlanksAndIron()
    {
        var sim = Flat();
        if (sim.Economy.TownHall(0) == null) Place(sim, 0, "town_hall", 6, 6);
        var store = Place(sim, 0, "storehouse", 20, 6);
        foreach (var (g, n) in new[] { (Good.Food, 200), (Good.Wood, 200), (Good.Planks, 200), (Good.Iron, 50) })
            store.Stock[(int)g] = n;
        sim.Economy.Grant(0, sim.Content.Tech("smelting"));
        var shop = Place(sim, 0, "siege_workshop", 30, 30);
        int planks = sim.Economy.Stock(0, Good.Planks), wood = sim.Economy.Stock(0, Good.Wood);
        int before = sim.Units.Count;
        sim.Schedule(0, new TrainCommand(0, shop.Id, "battering_ram"));
        Run(sim, sim.Content.Unit("battering_ram").TrainTicks / Tps + 2);
        Assert.Equal(before + 1, sim.Units.Count);
        Assert.Equal("battering_ram", sim.Content.Units[sim.Units.Def[sim.Units.Count - 1]].Id);
        Assert.Equal(planks - 40, sim.Economy.Stock(0, Good.Planks));
        Assert.Equal(wood - 40, sim.Economy.Stock(0, Good.Wood));
    }

    [Fact]
    public void ARamNeverAttacksUnitsEvenWhenOrderedTo()
    {
        var sim = Flat();
        int ram = sim.SpawnUnit(0, "battering_ram", 20, 20);
        int foe = sim.SpawnUnit(1, "worker", 21, 20);
        sim.Units.Stance[Index(sim, foe)] = UnitStance.Hold;
        int full = Hp(sim, foe);
        Run(sim, 10);
        Assert.Equal(full, Hp(sim, foe));
        Assert.Equal(0, sim.Units.Target[Index(sim, ram)]);

        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { ram }, foe));
        Run(sim, 10);
        Assert.Equal(full, Hp(sim, foe));
        Assert.Equal(0, sim.Units.Target[Index(sim, ram)]);
        // An attack-move walks it past the enemy without a blow.
        sim.Schedule(sim.Tick, new AttackMoveCommand(0, new[] { ram }, 40, 20));
        Run(sim, 20);
        Assert.Equal(full, Hp(sim, foe));
    }

    [Fact]
    public void ARamStrikesAtTheFootOfTheWallSoTheColumnAboveFalls()
    {
        // A wooden wall along z at x 24, hit from the west. Watch the column nearest the attacker after its first block
        // is knocked out: a ram takes its foot and the column comes down; a spearman only chips its top.
        string Column(string unit)
        {
            var sim = Flat();
            var wall = Place(sim, 1, "wooden_wall", 24, 20);
            int a = sim.SpawnUnit(0, unit, 22, 20);
            sim.Schedule(0, new AttackBuildingCommand(0, new[] { a }, wall.Id));
            var before = Snapshot(sim, 24, 20, wall.Y, wall.Y + 6);
            for (int t = 0; t < 120 * Tps && Snapshot(sim, 24, 20, wall.Y, wall.Y + 6) == before; t++) sim.Step();
            return before + " -> " + Snapshot(sim, 24, 20, wall.Y, wall.Y + 6);
        }
        // P is the wall's planks, A air: the ram's first blow brings the whole column down, the spearman's takes its top.
        Assert.Equal("PPPPAAA -> AAAAAAA", Column("battering_ram"));
        Assert.Equal("PPPPAAA -> PPPAAAA", Column("spearman"));
    }

    private static string Snapshot(Simulation sim, int x, int z, int y0, int y1)
    {
        var sb = new System.Text.StringBuilder();
        for (int y = y0; y <= y1; y++) sb.Append(sim.World.Get(x, y, z).ToString()[0]);
        return sb.ToString();
    }

    private static int Standing(Simulation sim, int x, int z, int y0, int y1)
    {
        int n = 0;
        for (int y = y0; y <= y1; y++) if (sim.World.Get(x, y, z) != BlockType.Air) n++;
        return n;
    }

    [Fact]
    public void ARamDealsFullDamageToThickWallsAndTwiceThatToGates()
    {
        var sim = Flat();
        var combat = sim.Combat;
        var content = sim.Content;
        var ram = content.Unit("battering_ram");
        var spear = content.Unit("spearman");
        var wall = content.Building("stone_wall");
        var gate = content.Building("stone_gate");
        Assert.Equal(ram.Damage, combat.BuildingDamage(ram, wall, ram.Damage));
        Assert.Equal(2 * ram.Damage, combat.BuildingDamage(ram, gate, ram.Damage));
        // Everyone else deals the same to a gate as to a wall: a spearman's blows barely mark either.
        Assert.Equal(combat.BuildingDamage(spear, wall, spear.Damage), combat.BuildingDamage(spear, gate, spear.Damage));
        Assert.True(combat.BuildingDamage(spear, wall, spear.Damage) * 10 < ram.Damage);
    }

    [Fact]
    public void ARamBreaksAStoneGateFarSoonerThanSpearmenAndSoonerThanTheWallBesideIt()
    {
        int Time(string building, string unit, int count)
        {
            var sim = Flat();
            var b = Place(sim, 1, building, 30, 20);
            var ids = Enumerable.Range(0, count).Select(k => sim.SpawnUnit(0, unit, 26, 18 + k)).ToArray();
            sim.Schedule(0, new AttackBuildingCommand(0, ids, b.Id));
            return Until(sim, b, 600);
        }
        int gate = Time("stone_gate", "battering_ram", 1);
        int wall = Time("stone_wall", "battering_ram", 1);
        int spears = Time("stone_gate", "spearman", 4);
        Assert.True(gate > 0 && wall > 0, $"gate {gate}s, wall {wall}s");
        Assert.True(gate < wall, $"a gate ({gate}s) falls sooner than a wall ({wall}s)");
        Assert.True(spears < 0 || gate * 3 < spears, $"one ram ({gate}s) breaks a gate far sooner than four spearmen ({spears}s)");
    }

    [Fact]
    public void ArrowsHardlyHurtARamButRidersMakeShortWorkOfIt()
    {
        int Left(string machine, string attacker, int count, int seconds)
        {
            var sim = Flat();
            int m = sim.SpawnUnit(0, machine, 20, 20);
            sim.Units.Stance[Index(sim, m)] = UnitStance.Hold;
            var ids = Enumerable.Range(0, count).Select(k => sim.SpawnUnit(1, attacker, 28, 18 + k)).ToArray();
            sim.Schedule(0, new AttackCommand(1, ids, m));
            Run(sim, seconds);
            return sim.Units.Exists(m) ? Hp(sim, m) * 100 / sim.Content.Unit(machine).MaxHp : 0;
        }
        // Three archers shooting for 20 seconds: a catapult is wrecked or nearly, the ram has most of its strength left.
        int ram = Left("battering_ram", "archer", 3, 20);
        int cat = Left("catapult", "archer", 3, 20);
        Assert.True(ram >= 75, $"the ram has {ram}% left");
        Assert.True(cat + 25 < ram, $"the catapult has {cat}%, the ram {ram}%");
        // Three riders in the same time wreck it.
        Assert.Equal(0, Left("battering_ram", "rider", 3, 30));
    }

    [Fact]
    public void ARamNeverBreaksAndRunsAndDoesNotBleed()
    {
        var sim = Flat();
        int ram = sim.SpawnUnit(0, "battering_ram", 20, 20);
        sim.Units.Morale[Index(sim, ram)] = 0;
        Run(sim, 1);
        Assert.Equal(0, sim.Units.Rout[Index(sim, ram)]);
        int rider = sim.SpawnUnit(1, "rider", 21, 20);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { rider }, ram));
        Run(sim, 3);
        Assert.True(Hp(sim, ram) < sim.Content.Unit("battering_ram").MaxHp);
        Assert.Equal(0, sim.Blood.Total);
    }

    [Fact]
    public void ComputerPlayersDoNotTrainRamsYet()
    {
        var sim = Flat();
        var ai = new Rts.Sim.AI.AiPlayer(1, 1, Rts.Sim.AI.AiLevel.Hard);
        Assert.Equal(0, ai.TrainWeight(sim, sim.Content.Unit("battering_ram")));
    }
}
