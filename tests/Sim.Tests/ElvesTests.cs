using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Elves, the forest guardians (see "The Elves" in docs/rules.md): their own glade wardens, elf archers and druids
/// in place of everyone's spearmen, archers, mages and healers, and living walls in place of the barrier: a hedge of
/// living wood that grows back what is knocked out of it (<see cref="BuildingDef.RegrowTicks"/>) and, with Thornwood,
/// pricks whoever hacks at it (<see cref="BuildingDef.Thorns"/>, <see cref="TechEffectType.Thorns"/>).
/// </summary>
public class ElvesTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int WallX = 30, WallZ = 30;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

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

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[sim.Units.IndexOf(id)];

    /// <summary>An Elf living wall of player 0's at (WallX, WallZ), with its top <paramref name="knocked"/> blocks knocked out.</summary>
    private static Building DamagedLivingWall(Simulation sim, int knocked)
    {
        var wall = Place(sim, 0, "living_wall", WallX, WallZ);
        int top = wall.Y + wall.Shape.Height - 1;
        for (int k = 0; k < knocked; k++) sim.World.Set(WallX, top - k, WallZ, BlockType.Air);
        sim.Step(); // the blocks gone count against it
        Assert.True(wall.Damaged);
        return wall;
    }

    [Fact]
    public void TheElvesAreTheFourthFaction()
    {
        var content = ContentDb.Default;
        var elves = content.Faction("elves");
        Assert.Equal(3, elves.Index);
        Assert.Equal("Elves", elves.Name);
        Assert.Null(elves.Experience);
        Assert.False(elves.TamesMonsters);
        Assert.Equal(DuskTreeUse.Heal, elves.DuskTree);
        Assert.Equal(AiStyle.Balanced, elves.AiStyle);
        Assert.Equal(BuildingLook.Standard, elves.Look);
        Assert.Equal(new[] { ("spearman", "glade_warden"), ("archer", "elf_archer"), ("mage", "druid"), ("healer", "druid") },
            elves.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        Assert.Equal(new[] { ("barrier", "living_wall") }, elves.BuildingReplacements.Select(r => (r.From.Id, r.To.Id)));
        // Their archers outshoot everyone's, and see as far as they shoot.
        var archer = content.Unit("elf_archer");
        Assert.True(archer.Range > content.Unit("archer").Range);
        Assert.True(archer.Sight > archer.Range);
        // Their druid heals, and grows the forest, but casts none of a mage's other spells.
        var druid = content.Unit("druid");
        Assert.True(druid.Heal > 0);
        Assert.True(druid.GrowForest);
        Assert.False(druid.EarthWall || druid.IceBridge || druid.Chasm);
    }

    [Fact]
    public void TheElvesStartWithTheirOwnUnits()
    {
        var content = ContentDb.Default;
        var elves = content.Faction("elves").StartUnits;
        Assert.Equal(content.StartUnits.Count, elves.Count);
        Assert.Equal(new[] { "worker", "glade_warden", "elf_archer", "rider", "sapper", "druid" }, elves.Select(u => u.Id));

        var sim = Flat(96);
        sim.SetFaction(1, "elves");
        sim.Map.Starts.Add(new StartArea(0, 20, Ground + 1, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Ground + 1, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(2, Count(sim, 0, "spearman"));
        Assert.Equal(0, Count(sim, 0, "glade_warden"));
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "mage"));
        Assert.Equal(2, Count(sim, 1, "glade_warden"));
        Assert.Equal(2, Count(sim, 1, "elf_archer"));
        Assert.Equal(2, Count(sim, 1, "druid"));
        Assert.Equal(2, Count(sim, 1, "rider")); // riders and sappers are everyone's
        Assert.Equal(2, Count(sim, 1, "sapper"));
    }

    [Fact]
    public void TheElvesTrainTheirOwnAtTheBarracks()
    {
        var sim = Flat();
        sim.SetFaction(1, "elves");
        var econ = sim.Economy;
        foreach (var id in new[] { "spearman", "archer", "mage", "healer" })
            Assert.False(econ.Unlocked(1, sim.Content.Unit(id)), id);
        Assert.True(econ.Unlocked(1, sim.Content.Unit("glade_warden")));
        Assert.False(econ.Unlocked(0, sim.Content.Unit("glade_warden"))); // another faction's
        Assert.Contains("druid", sim.Content.Building("barracks").Trains);

        var hall = Hall(sim, 1, 10, 10);
        var barracks = Place(sim, 1, "barracks", 30, 10);
        sim.Schedule(0, new TrainCommand(1, barracks.Id, "glade_warden"));
        Run(sim, () => Count(sim, 1, "glade_warden") == 1, 30 * Tps, "a glade warden to be trained");
    }

    [Fact]
    public void TheElvesPlantLivingWallsInsteadOfBarriers()
    {
        var sim = Flat();
        sim.SetFaction(1, "elves");
        var econ = sim.Economy;
        var living = sim.Content.Building("living_wall");
        Assert.True(econ.Unlocked(1, living));
        Assert.False(econ.Unlocked(0, living));
        // The barrier and the walls it is upgraded to are not theirs, nor is there an upgrade of a living wall.
        foreach (var id in new[] { "barrier", "wooden_wall", "stone_wall" })
            Assert.False(econ.Unlocked(1, sim.Content.Building(id)), id);
        Assert.Null(living.Upgrade);
        Assert.Equal(BlockType.Livewood, living.Wall);
        Assert.True(living.Walkway && living.Thick);
        Assert.True(econ.Unlocked(1, sim.Content.Building("barrier_gate"))); // their gates are everyone's

        // The wall tool (which lays everyone's barrier) lays their living wall instead; the Settlers' stays a barrier.
        Hall(sim, 0, 4, 4);
        Hall(sim, 1, 4, 40);
        int elf = sim.SpawnUnit(1, "worker", 20, 50), settler = sim.SpawnUnit(0, "worker", 20, 14);
        sim.Schedule(0, new WallCommand(1, new[] { elf }, "barrier", 20, 52, 24, 52));
        sim.Schedule(0, new WallCommand(0, new[] { settler }, "barrier", 20, 16, 24, 16));
        sim.Step();
        var elfWalls = sim.Buildings.All.Where(b => b.Owner == 1 && b.Def.IsWall).ToList();
        var settlerWalls = sim.Buildings.All.Where(b => b.Owner == 0 && b.Def.IsWall).ToList();
        Assert.Equal(5, elfWalls.Count);
        Assert.All(elfWalls, b => Assert.Equal("living_wall", b.Def.Id));
        Assert.Equal(5, settlerWalls.Count);
        Assert.All(settlerWalls, b => Assert.Equal("barrier", b.Def.Id));
        Run(sim, () => elfWalls.All(b => b.Active), 2 * 60 * Tps, "the living wall to be planted");
        int top = elfWalls[0].Y + elfWalls[0].Shape.Height - 1;
        Assert.Equal(BlockType.Livewood, sim.World.Get(elfWalls[0].X, top, elfWalls[0].Z));
    }

    [Fact]
    public void ALivingWallGrowsBackByItselfForNothing()
    {
        var sim = Flat();
        sim.SetFaction(0, "elves");
        Hall(sim, 0, 4, 4);
        // A wall comes down once it has lost half its hit points, so one block of the four is knocked out of it.
        var wall = DamagedLivingWall(sim, knocked: 1);
        int top = wall.Y + wall.Shape.Height - 1;
        var stock = (int[])sim.Economy.Stockpile(0).Clone();
        // It grows back in one step of 5 seconds, then it is whole.
        int took = Run(sim, () => sim.World.Get(WallX, top, WallZ) == BlockType.Livewood, 6 * Tps, "the block to grow back");
        Assert.True(took > Tps / 2);
        Run(sim, () => !wall.Damaged, 6 * Tps, "the wall to be whole");
        Assert.Equal(wall.MaxHealth, wall.Health);
        Assert.Equal(stock, sim.Economy.Stockpile(0)); // nobody worked it, nothing was paid
        Assert.True(sim.Economy.Regrown >= 1);
    }

    [Fact]
    public void EveryonesWallsDoNotGrowBack()
    {
        var sim = Flat();
        Hall(sim, 0, 4, 4);
        var wall = Place(sim, 0, "wooden_wall", WallX, WallZ);
        int top = wall.Y + wall.Shape.Height - 1;
        sim.World.Set(WallX, top, WallZ, BlockType.Air);
        for (int t = 0; t < 30 * Tps; t++) sim.Step();
        Assert.True(wall.Damaged);
        Assert.Equal(BlockType.Air, sim.World.Get(WallX, top, WallZ));
    }

    [Fact]
    public void ALivingWallUnderAttackDoesNotGrowBackUntilTheBlowsStop()
    {
        var sim = Flat();
        sim.SetFaction(0, "elves");
        Hall(sim, 0, 4, 4);
        Hall(sim, 1, 4, 54);
        var wall = DamagedLivingWall(sim, knocked: 1);
        int lost = wall.MaxHealth - wall.Health;
        // A spearman hacks at it for 20 seconds: no regrowth while the blows land, so it only loses ground.
        int spear = sim.SpawnUnit(1, "spearman", WallX + 2, WallZ);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { spear }, wall.Id));
        long regrown = sim.Economy.Regrown;
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.Equal(regrown, sim.Economy.Regrown);
        Assert.True(wall.MaxHealth - wall.Health > lost);
        // It is called off: once the blows have stopped a few seconds, the wall grows back.
        sim.Schedule(sim.Tick, new MoveCommand(1, new[] { spear }, 50, 54));
        Run(sim, () => !wall.Damaged, 60 * Tps, "the wall to grow back once left alone");
        Assert.True(sim.Economy.Regrown > regrown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThornwoodPricksWhoeverHacksAtALivingWall(bool thornwood)
    {
        var sim = Flat();
        sim.SetFaction(0, "elves");
        Hall(sim, 0, 4, 4);
        Hall(sim, 1, 4, 54);
        if (thornwood) sim.Economy.Grant(0, sim.Content.Tech("elf_thornwood"));
        Assert.Equal(thornwood, sim.Economy.HasThorns(0));
        var wall = Place(sim, 0, "living_wall", WallX, WallZ);
        int spear = sim.SpawnUnit(1, "spearman", WallX + 2, WallZ);
        int archer = sim.SpawnUnit(1, "archer", WallX + 6, WallZ);
        int hp = Hp(sim, spear), archerHp = Hp(sim, archer);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { spear, archer }, wall.Id));
        for (int t = 0; t < 10 * Tps; t++) sim.Step();
        Assert.True(wall.Health < wall.MaxHealth); // both struck it
        Assert.Equal(archerHp, Hp(sim, archer));   // shooting from afar, the archer never feels the thorns
        if (thornwood)
        {
            Assert.True(Hp(sim, spear) < hp);
            Assert.True(sim.Combat.ThornPricks > 0);
        }
        else
        {
            Assert.Equal(hp, Hp(sim, spear));
            Assert.Equal(0, sim.Combat.ThornPricks);
        }
    }

    [Fact]
    public void EveryonesWallsHaveNoThorns()
    {
        var sim = Flat();
        sim.SetFaction(0, "elves");
        Hall(sim, 0, 4, 4);
        Hall(sim, 1, 4, 54);
        sim.Economy.Grant(0, sim.Content.Tech("elf_thornwood"));
        var wall = Place(sim, 0, "wooden_wall", WallX, WallZ); // say, one they took
        int spear = sim.SpawnUnit(1, "spearman", WallX + 2, WallZ);
        int hp = Hp(sim, spear);
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, new[] { spear }, wall.Id));
        for (int t = 0; t < 10 * Tps; t++) sim.Step();
        Assert.True(wall.Health < wall.MaxHealth);
        Assert.Equal(hp, Hp(sim, spear));
    }

    [Fact]
    public void ADruidHealsTheMostHurtAllyNearby()
    {
        var sim = Flat();
        sim.SetFaction(0, "elves");
        Hall(sim, 0, 4, 4);
        int warden = sim.SpawnUnit(0, "glade_warden", 40, 40);
        int druid = sim.SpawnUnit(0, "druid", 43, 40);
        int i = sim.Units.IndexOf(warden);
        sim.Units.Hp[i] = 20;
        for (int t = 0; t < 10 * Tps; t++) sim.Step();
        Assert.True(Hp(sim, warden) >= 20 + 4 * sim.Content.Unit("druid").Heal);
    }

    [Fact]
    public void TheElvesFinalTechOpensTheEldertree()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("elf_eldertree");
        Assert.Equal("elves", tech.Faction!.Id);
        Assert.Contains(tech.Requires, t => t.Id == "elf_thornwood");
        var tree = content.Building("eldertree");
        Assert.Equal(BuildingKind.GreatWork, tree.Kind);
        Assert.Equal("elves", tree.Faction!.Id);
        Assert.True(tree.Mound);
        Assert.Equal(BlockType.Livewood, tree.Wall);
        Assert.True(tree.RegrowTicks > 0);
    }
}
