using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Field repairs (Craft guilds): Settler workers mend their damaged catapults and battering rams where they stand, a step at
/// a time, paying a share of the machine's cost in materials. Not ships, not soldiers, not without the tech.
/// </summary>
public class FieldRepairTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(bool guilds = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));
        var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 4, 4)!;
        for (int g = 0; g < hall.Stock.Length; g++) hall.Stock[g] = 500;
        if (guilds)
        {
            sim.Economy.Grant(0, sim.Content.Tech("tools"));
            sim.Economy.Grant(0, sim.Content.Tech("settler_guilds"));
        }
        return sim;
    }

    private static int Hurt(Simulation sim, string unit, int x, int z, int hp)
    {
        int id = sim.SpawnUnit(0, unit, x, z);
        sim.Units.Hp[sim.Units.IndexOf(id)] = hp;
        return id;
    }

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[sim.Units.IndexOf(id)];

    [Fact]
    public void CraftGuildsGivesFieldRepairsToTheSettlersOnly()
    {
        var guilds = ContentDb.Default.Tech("settler_guilds");
        Assert.Contains(guilds.Effects, e => e.Type == TechEffectType.FieldRepairs);
        Assert.All(ContentDb.Default.Techs.Where(t => t.Id != "settler_guilds"),
            t => Assert.DoesNotContain(t.Effects, e => e.Type == TechEffectType.FieldRepairs));
    }

    [Fact]
    public void WorkersMendACatapultToFullAndPayInMaterialsNotFood()
    {
        var sim = Flat();
        var def = sim.Content.Unit("catapult");
        int max = sim.Economy.MaxHp(0, def);
        int cat = Hurt(sim, "catapult", 30, 30, max / 2);
        int worker = sim.SpawnUnit(0, "worker", 25, 30);
        var before = sim.Economy.Stockpile(0).ToArray();
        sim.Schedule(sim.Tick, new MendCommand(0, new[] { worker }, cat));
        for (int t = 0; t < 120 * Tps && Hp(sim, cat) < max; t++) sim.Step();
        Assert.Equal(max, Hp(sim, cat));
        var after = sim.Economy.Stockpile(0);
        Assert.Equal(before[(int)Good.Food], after[(int)Good.Food]);
        int paid = def.Cost.Where(c => c.Good != Good.Food).Sum(c => before[(int)c.Good] - after[(int)c.Good]);
        Assert.True(paid > 0);
        // About half the materials for half its hit points: a quarter of its cost, rounded up a little.
        int materials = def.Cost.Where(c => c.Good != Good.Food).Sum(c => c.Amount);
        Assert.InRange(paid, materials / 4, materials / 4 + 2 * 10);
        for (int t = 0; t < 2 * Tps; t++) sim.Step();
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
    }

    [Fact]
    public void WithoutCraftGuildsNothingIsMended()
    {
        var sim = Flat(guilds: false);
        int ram = Hurt(sim, "battering_ram", 30, 30, 100);
        int alone = Hurt(sim, "battering_ram", 30, 40, 100); // heals on its own at the same pace, with nobody mending it
        int worker = sim.SpawnUnit(0, "worker", 28, 30);
        sim.Schedule(sim.Tick, new MendCommand(0, new[] { worker }, ram));
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.Equal(Hp(sim, alone), Hp(sim, ram));
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(worker)]);
    }

    [Fact]
    public void OnlyMachinesAreMended()
    {
        var sim = Flat();
        int knight = Hurt(sim, "spearman", 30, 30, 10);
        Assert.False(sim.Economy.Mendable(0, sim.Units.IndexOf(knight)));
        int enemyRam = sim.SpawnUnit(1, "battering_ram", 40, 40);
        sim.Units.Hp[sim.Units.IndexOf(enemyRam)] = 50;
        Assert.False(sim.Economy.Mendable(0, sim.Units.IndexOf(enemyRam)));
    }

    [Fact]
    public void WorkersFollowAMachineThatMoves()
    {
        var sim = Flat();
        int max = sim.Economy.MaxHp(0, sim.Content.Unit("battering_ram"));
        int ram = Hurt(sim, "battering_ram", 30, 30, max / 2);
        int worker = sim.SpawnUnit(0, "worker", 28, 30);
        sim.Schedule(sim.Tick, new MendCommand(0, new[] { worker }, ram));
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { ram }, 30, 45, Ground + 1));
        for (int t = 0; t < 90 * Tps && Hp(sim, ram) < max; t++) sim.Step();
        Assert.Equal(max, Hp(sim, ram));
    }

    [Fact]
    public void FieldRepairsReplayTheSame()
    {
        static ulong Play()
        {
            var sim = Flat();
            int cat = Hurt(sim, "catapult", 30, 30, 40);
            int worker = sim.SpawnUnit(0, "worker", 25, 30);
            sim.Schedule(3, new MendCommand(0, new[] { worker }, cat));
            for (int t = 0; t < 30 * Tps; t++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
