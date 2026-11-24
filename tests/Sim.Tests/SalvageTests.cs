using System;
using System.Linq;
using Rts.Sim.Automation;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Salvage (Craft guilds): Settler workers take down their own finished buildings, bringing back half the cost (in
/// proportion to the hit points left) and the goods the building held to the nearest other storage.
/// </summary>
public class SalvageTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit at {x}, {z}");

    private static int Stock(Simulation sim, int player, Good g) => sim.Economy.Stockpile(player)[(int)g];

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

    [Fact]
    public void CraftGuildsGivesSalvageToTheSettlersOnly()
    {
        var guilds = ContentDb.Default.Tech("settler_guilds");
        Assert.Equal("settlers", guilds.Faction?.Id);
        Assert.Contains(guilds.Effects, e => e.Type == TechEffectType.Salvage);
        Assert.All(ContentDb.Default.Techs.Where(t => t.Id != "settler_guilds"),
            t => Assert.DoesNotContain(t.Effects, e => e.Type == TechEffectType.Salvage));
    }

    [Fact]
    public void WithoutCraftGuildsNothingIsTakenDown()
    {
        var sim = Flat();
        Place(sim, 0, "town_hall", 4, 4);
        var house = Place(sim, 0, "house", 30, 30);
        int worker = sim.SpawnUnit(0, "worker", 28, 30);
        Assert.False(sim.Economy.Salvageable(0, house));
        sim.Schedule(sim.Tick, new SalvageCommand(0, new[] { worker }, house.Id));
        for (int t = 0; t < 30 * Tps; t++) sim.Step();
        Assert.Same(house, sim.Buildings.Get(house.Id));
        Assert.False(house.Salvaging);
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
    }

    [Fact]
    public void WorkersTakeAHouseDownAndBringBackHalfItsCost()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 4, 4);
        sim.Economy.Grant(0, sim.Content.Tech("tools"));
        sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
        var house = Place(sim, 0, "house", 30, 30);
        var before = sim.Economy.Stockpile(0).ToArray();
        int worker = sim.SpawnUnit(0, "worker", 28, 30);
        sim.Schedule(sim.Tick, new SalvageCommand(0, new[] { worker }, house.Id));
        int ticks = Run(sim, () => sim.Buildings.Get(house.Id) == null, 3 * 60 * Tps, "the house to come down");
        // Half the time it took to build (plus the walk there).
        Assert.True(ticks >= house.Def.BuildTicks / 2, $"took {ticks} ticks");
        foreach (var c in house.Def.Cost)
            Assert.Equal(before[(int)c.Good] + c.Amount / 2, Stock(sim, 0, c.Good));
        // Gone with no rubble: the ground where it stood is clear.
        for (int y = Ground + 1; y < Ground + 1 + house.Shape.Height; y++)
            Assert.Equal(BlockType.Air, sim.World.Get(house.X + 1, y, house.Z + 1));
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
        Assert.Same(hall, sim.Buildings.Get(hall.Id));
    }

    [Fact]
    public void AStorehouseTakenDownSendsItsGoodsHome()
    {
        var sim = Flat();
        var hall = Place(sim, 0, "town_hall", 4, 4);
        sim.Economy.Grant(0, sim.Content.Tech("tools"));
        sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
        var store = Place(sim, 0, "storehouse", 40, 40);
        store.Stock[(int)Good.Stone] = 70;
        int hallStone = hall.Stock[(int)Good.Stone];
        var back = sim.Economy.SalvageReturn(store);
        Assert.True(back[(int)Good.Stone] >= 70);
        int worker = sim.SpawnUnit(0, "worker", 38, 40);
        sim.Schedule(sim.Tick, new SalvageCommand(0, new[] { worker }, store.Id));
        Run(sim, () => sim.Buildings.Get(store.Id) == null, 3 * 60 * Tps, "the storehouse to come down");
        Assert.Equal(hallStone + back[(int)Good.Stone], hall.Stock[(int)Good.Stone]);
    }

    [Fact]
    public void ADamagedBuildingGivesBackLess()
    {
        var sim = Flat();
        Place(sim, 0, "town_hall", 4, 4);
        var wall = Place(sim, 0, "stone_wall", 30, 30);
        var whole = sim.Economy.SalvageReturn(wall);
        int top = wall.Y + wall.Shape.Height - 1;
        sim.World.Set(30, top, 30, BlockType.Air);
        sim.World.Set(30, top - 1, 30, BlockType.Air);
        sim.Step();
        Assert.True(wall.Damaged);
        var hurt = sim.Economy.SalvageReturn(wall);
        Assert.True(hurt[(int)Good.Stone] < whole[(int)Good.Stone]);
        Assert.Equal(wall.Def.Cost.First(c => c.Good == Good.Stone).Amount / 2, whole[(int)Good.Stone]);
    }

    [Fact]
    public void StoppingKeepsTheWorkAndAutomaticWorkersFinishIt()
    {
        var sim = Flat();
        Place(sim, 0, "town_hall", 4, 4);
        sim.Economy.Grant(0, sim.Content.Tech("tools"));
        sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
        var house = Place(sim, 0, "house", 20, 10);
        int worker = sim.SpawnUnit(0, "worker", 18, 10);
        sim.Schedule(sim.Tick, new SalvageCommand(0, new[] { worker }, house.Id));
        Run(sim, () => house.Salvaged > 0, 30 * Tps, "the work to start");
        for (int t = 0; t < 2 * Tps; t++) sim.Step();
        sim.Schedule(sim.Tick, new SalvageCommand(0, new[] { worker }, house.Id, Stop: true));
        sim.Step();
        int done = house.Salvaged;
        Assert.False(house.Salvaging);
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
        for (int t = 0; t < 2 * Tps; t++) sim.Step();
        Assert.Equal(done, house.Salvaged);
        // Marked again with nobody sent: an automatic worker takes it as building work.
        sim.Schedule(sim.Tick, new AutomateCommand(0, new[] { worker }, true));
        sim.Schedule(sim.Tick, new SalvageCommand(0, Array.Empty<int>(), house.Id));
        Run(sim, () => sim.Buildings.Get(house.Id) == null, 3 * 60 * Tps, "automatic workers to take it down");
    }

    [Fact]
    public void OnlyTheOwnerMarksOrUnmarks()
    {
        var sim = Flat();
        Place(sim, 0, "town_hall", 4, 4);
        sim.Economy.Grant(0, sim.Content.Tech("tools"));
        sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
        var house = Place(sim, 0, "house", 30, 30);
        sim.Schedule(sim.Tick, new SalvageCommand(0, Array.Empty<int>(), house.Id));
        sim.Step();
        Assert.True(house.Salvaging);
        // Another player cannot mark it or take the mark off.
        sim.Schedule(sim.Tick, new SalvageCommand(1, Array.Empty<int>(), house.Id, Stop: true));
        sim.Step();
        Assert.True(house.Salvaging);
    }

    [Fact]
    public void ASalvageReplaysTheSame()
    {
        static ulong Play()
        {
            var sim = Flat();
            Place(sim, 0, "town_hall", 4, 4);
            sim.Economy.Grant(0, sim.Content.Tech("tools"));
            sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
            var house = Place(sim, 0, "house", 30, 30);
            int worker = sim.SpawnUnit(0, "worker", 28, 30);
            sim.Schedule(5, new SalvageCommand(0, new[] { worker }, house.Id));
            for (int t = 0; t < 60 * Tps; t++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
