using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Yards: a dock mends its owner's ships and a siege workshop their catapults and rams while they wait beside it out of
/// the fight, with no worker and no tech, paid for from storage in materials like field repairs.
/// </summary>
public class YardRepairTests
{
    private const int Ground = 3;
    private const int Tps = Simulation.TicksPerSecond;
    private const int RiverX0 = 30, RiverX1 = 35;

    /// <summary>Flat ground with a deep river along z, from x = 30 to 35, and a stocked town hall.</summary>
    private static (Simulation Sim, Building Hall) World(bool river = false)
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        if (river) TestWorlds.Box(w, RiverX0, 1, 0, RiverX1, Ground, 63, BlockType.Water);
        var sim = TestWorlds.Sim(w);
        var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("town_hall"), 4, 4)!;
        for (int g = 0; g < hall.Stock.Length; g++) hall.Stock[g] = 500;
        return (sim, hall);
    }

    private static int Hurt(Simulation sim, int owner, string unit, int x, int z, int hp)
    {
        int id = sim.SpawnUnit(owner, unit, x, z);
        Assert.True(id >= 0, $"{unit} spawned at {x}, {z}");
        sim.Units.Hp[sim.Units.IndexOf(id)] = hp;
        return id;
    }

    private static int Hp(Simulation sim, int id) => sim.Units.Hp[sim.Units.IndexOf(id)];

    private static void Run(Simulation sim, int seconds)
    {
        for (int t = 0; t < seconds * Tps; t++) sim.Step();
    }

    [Fact]
    public void DocksAndSiegeWorkshopsMend()
    {
        var db = ContentDb.Default;
        Assert.True(db.Building("dock").Mends > 0);
        Assert.True(db.Building("siege_workshop").Mends > 0);
        foreach (var id in new[] { "ship", "bombard_ship" }) Assert.Equal("dock", db.Unit(id).TrainedAt?.Id);
        foreach (var id in new[] { "catapult", "battering_ram" }) Assert.Equal("siege_workshop", db.Unit(id).TrainedAt?.Id);
    }

    [Fact]
    public void ACatapultBesideItsWorkshopIsMendedWithNoWorkerAndNoTech()
    {
        var (sim, _) = World();
        var shop = sim.Economy.PlaceFinished(0, sim.Content.Building("siege_workshop"), 30, 30)!;
        var def = sim.Content.Unit("catapult");
        int max = sim.Economy.MaxHp(0, def);
        int near = Hurt(sim, 0, "catapult", shop.MaxX + 2, 32, max / 4);
        int away = Hurt(sim, 0, "catapult", 50, 50, max / 4); // heals on its own, no faster
        var before = sim.Economy.Stockpile(0).ToArray();
        Run(sim, 12); // out of the fight for the recovery delay first
        Assert.NotNull(sim.Economy.YardFor(sim.Units.IndexOf(near)));
        Assert.Null(sim.Economy.YardFor(sim.Units.IndexOf(away)));
        Run(sim, 40);
        Assert.Equal(max, Hp(sim, near));
        Assert.True(Hp(sim, away) < max, $"the catapult away from the workshop healed to {Hp(sim, away)}");
        var after = sim.Economy.Stockpile(0);
        Assert.Equal(before[(int)Good.Food], after[(int)Good.Food]);
        int paid = def.Cost.Where(c => c.Good != Good.Food).Sum(c => before[(int)c.Good] - after[(int)c.Good]);
        Assert.True(paid > 0, "paid for in materials");
        Assert.Equal(0, sim.Units.Wounds.Skip(sim.Units.IndexOf(near) * Body.Parts).Take(Body.Parts).Sum(w => w));
    }

    [Fact]
    public void AShipBesideItsDockIsMended()
    {
        var (sim, _) = World(river: true);
        var dock = sim.Economy.PlaceFinished(0, sim.Content.Building("dock"), 25, 20)!;
        int max = sim.Economy.MaxHp(0, sim.Content.Unit("ship"));
        int near = Hurt(sim, 0, "ship", RiverX0 + 1, 21, max / 4);
        int away = Hurt(sim, 0, "ship", RiverX0 + 2, 50, max / 4);
        Run(sim, 40);
        Assert.Equal(max, Hp(sim, near));
        Assert.True(Hp(sim, away) < max, $"the ship away from the dock healed to {Hp(sim, away)}");
        Assert.Null(sim.Economy.YardFor(sim.Units.IndexOf(near)));
    }

    [Fact]
    public void NothingIsMendedInTheFightAtAnotherPlayersYardOrWithNothingToPay()
    {
        var (sim, hall) = World();
        sim.Economy.PlaceFinished(0, sim.Content.Building("siege_workshop"), 20, 30);
        sim.Economy.PlaceFinished(1, sim.Content.Building("siege_workshop"), 40, 30);
        var def = sim.Content.Unit("battering_ram");
        int max = sim.Economy.MaxHp(0, def);
        int fighting = Hurt(sim, 0, "battering_ram", 27, 32, max / 4);
        int theirs = Hurt(sim, 0, "battering_ram", 38, 32, max / 4);
        for (int t = 0; t < 15 * Tps; t++)
        {
            sim.Units.LastFight[sim.Units.IndexOf(fighting)] = sim.Tick;
            sim.Step();
        }
        Assert.Equal(max / 4, Hp(sim, fighting));
        Assert.Null(sim.Economy.YardFor(sim.Units.IndexOf(theirs)));
        // With no materials left, it waits beside the workshop, healing only on its own.
        for (int g = 0; g < hall.Stock.Length; g++) if (g != (int)Good.Food) hall.Stock[g] = 0;
        Run(sim, 12);
        Assert.InRange(Hp(sim, fighting), max / 4, Hp(sim, theirs));
        Assert.True(Hp(sim, fighting) < max);
    }

    [Fact]
    public void YardRepairsReplayTheSame()
    {
        static ulong Play()
        {
            var (sim, _) = World();
            sim.Economy.PlaceFinished(0, sim.Content.Building("siege_workshop"), 30, 30);
            int max = sim.Economy.MaxHp(0, sim.Content.Unit("catapult"));
            Hurt(sim, 0, "catapult", 37, 32, max / 3);
            Run(sim, 30);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }
}
