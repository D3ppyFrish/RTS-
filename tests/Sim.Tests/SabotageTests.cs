using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage's saboteurs (see "Clan Kage" in docs/rules.md, <c>Economy/EconomySystem.Sabotage.cs</c>): a kunoichi or
/// shinobi at an enemy building wrecks its works, so it does no work for a while and goods in it spoil.
/// </summary>
public class SabotageTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, bool learn = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = false;
        sim.SetFaction(0, "kage");
        if (learn) Learn(sim, 0);
        return sim;
    }

    private static void Learn(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("kage_saboteurs"));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>Runs until the condition holds, failing after maxTicks.</summary>
    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.True(done(), $"not done after {maxTicks} ticks: {what}");
    }

    [Fact]
    public void SaboteursAreClanKagesTechForTheirKunoichiAndShinobi()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("kage_saboteurs");
        Assert.Equal("kage", tech.Faction?.Id);
        Assert.Contains(content.Tech("kage_shadow_strike"), tech.Requires);
        Assert.Equal(new[] { "kunoichi", "shinobi" },
            tech.Effects.Where(e => e.Type == TechEffectType.Sabotage).Select(e => e.Unit!.Id));
        var rules = content.Combat.Sabotage;
        Assert.Equal((6 * Tps, 3, 45 * Tps, 25, 60 * Tps), (rules.Ticks, rules.Guard, rules.StallTicks, rules.Spoil, rules.WaitTicks));

        var sim = Flat(learn: false);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Stand);
        int caster = sim.SpawnUnit(0, "shadow_caster", 22, 20, Stand);
        Assert.False(sim.Economy.CanSabotage(Index(sim, kunoichi))); // not before the tech
        Learn(sim, 0);
        Assert.True(sim.Economy.ReadyToSabotage(Index(sim, kunoichi)));
        Assert.False(sim.Economy.CanSabotage(Index(sim, caster))); // casters do not
    }

    [Fact]
    public void ASabotagedHallStopsTrainingAndResearchingAndItsGoodsSpoil()
    {
        var sim = Flat();
        var hall = Place(sim, 1, "town_hall", 36, 36);
        hall.Stock[(int)Good.Food] = 400;
        hall.Stock[(int)Good.Wood] = 400;
        sim.Schedule(0, new TrainCommand(1, hall.Id, "worker"));
        sim.Schedule(0, new TrainCommand(1, hall.Id, "worker"));
        sim.Schedule(0, new ResearchCommand(1, "farming"));
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 24, 24, Stand);
        sim.Schedule(0, new SabotageCommand(0, new[] { kunoichi }, hall.Id));
        sim.Step();
        Assert.Equal(UnitJob.Sabotage, sim.Units.Job[Index(sim, kunoichi)]);

        // It walks there and works the sabotage's time; then the hall stalls and a quarter of its goods spoil.
        int food = 0, wood = 0;
        RunUntil(sim, () =>
        {
            if (sim.Economy.Sabotages > 0) return true;
            (food, wood) = (hall.Stock[(int)Good.Food], hall.Stock[(int)Good.Wood]);
            return false;
        }, 40 * Tps, "the hall sabotaged");
        Assert.True(sim.Economy.Stalled(hall));
        Assert.Equal(food - food * 25 / 100, hall.Stock[(int)Good.Food]);
        Assert.Equal(wood - wood * 25 / 100, hall.Stock[(int)Good.Wood]);
        Assert.Equal(food * 25 / 100 + wood * 25 / 100, sim.Economy.GoodsSpoiled);
        int k = Index(sim, kunoichi);
        Assert.Equal(UnitJob.None, sim.Units.Job[k]);
        Assert.False(sim.Economy.ReadyToSabotage(k)); // it waits for the next

        // While stalled: no training, no research.
        int train = hall.TrainTicks, research = sim.Economy.ResearchTicks(1), queued = hall.Queue.Count;
        sim.Run(20 * Tps);
        Assert.Equal(train, hall.TrainTicks);
        Assert.Equal(research, sim.Economy.ResearchTicks(1));
        Assert.Equal(queued, hall.Queue.Count);
        Assert.False(sim.Economy.Sabotageable(0, hall)); // not again while it lies wrecked

        // After the stall, the work goes on; after its wait, the saboteur is ready again.
        RunUntil(sim, () => !sim.Economy.Stalled(hall), 30 * Tps, "the stall over");
        sim.Run(2);
        Assert.True(hall.TrainTicks > train || hall.Queue.Count < queued);
        Assert.True(sim.Economy.ResearchTicks(1) > research);
        RunUntil(sim, () => sim.Economy.ReadyToSabotage(Index(sim, kunoichi)), 20 * Tps, "ready again");
    }

    [Fact]
    public void ASoldierByTheBuildingHoldsTheSaboteurOffButItsWorkersDoNot()
    {
        var sim = Flat();
        var smelter = Place(sim, 1, "smelter", 36, 36);
        smelter.Stock[(int)Good.Iron] = 40;
        // The guard stands on the far side of the smelter from where the saboteur works, within the guard's reach of
        // the smelter but out of a spear's reach of the saboteur, holding its ground.
        Assert.True(sim.Economy.AccessSpot(smelter, out int ax, out int az, out _));
        int gx = Math.Clamp(2 * (smelter.X + 1) - ax, smelter.X - 2, smelter.MaxX + 2);
        int gz = Math.Clamp(2 * (smelter.Z + 1) - az, smelter.Z - 2, smelter.MaxZ + 2);
        int guard = sim.SpawnUnit(1, "spearman", gx, gz, Stand);
        sim.Schedule(0, new StanceCommand(1, new[] { guard }, UnitStance.Hold));
        int worker = sim.SpawnUnit(1, "worker", ax + (ax > smelter.MaxX ? 1 : 0), az + (az > smelter.MaxZ ? 1 : 0), Stand);
        int shinobi = sim.SpawnUnit(0, "shinobi", 30, 30, Stand);
        sim.Schedule(0, new SabotageCommand(0, new[] { shinobi }, smelter.Id));
        sim.Run(20 * Tps);
        Assert.Equal(0, sim.Economy.Sabotages);
        Assert.Equal(40, smelter.Stock[(int)Good.Iron]);

        // Once the soldier is gone, the worker by it does not stop the work.
        sim.Units.Remove(guard);
        RunUntil(sim, () => sim.Economy.Sabotages > 0, 20 * Tps, "sabotaged");
        Assert.True(sim.Units.Exists(worker));
        Assert.True(sim.Units.Exists(shinobi));
        Assert.Equal(30, smelter.Stock[(int)Good.Iron]);
    }

    [Fact]
    public void WallsTowersOwnBuildingsAndAlliesCannotBeSabotaged()
    {
        var sim = Flat();
        var own = Place(sim, 0, "house", 20, 20);
        var house = Place(sim, 1, "house", 36, 36);
        Assert.False(sim.Economy.Sabotageable(0, own));
        Assert.True(sim.Economy.Sabotageable(0, house));
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 28, 28, Stand);
        sim.Schedule(0, new SabotageCommand(0, new[] { kunoichi }, own.Id));
        sim.Step();
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, kunoichi)]);

        var wall = sim.Content.Buildings.First(b => b.IsWall);
        var tower = sim.Content.Buildings.First(b => b.IsTower);
        Assert.False(wall == null || tower == null);
        var w = sim.Economy.PlaceFinished(1, wall, 10, 50);
        if (w != null) Assert.False(sim.Economy.Sabotageable(0, w));
    }

    [Fact]
    public void ASaboteurThatIsHitDropsTheJobAndFights()
    {
        var sim = Flat();
        var house = Place(sim, 1, "house", 36, 36);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 24, 24, Stand);
        sim.Schedule(0, new SabotageCommand(0, new[] { kunoichi }, house.Id));
        sim.Run(2);
        int archer = sim.SpawnUnit(1, "archer", 28, 24, Stand);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { archer }, kunoichi));
        RunUntil(sim, () => sim.Units.Job[Index(sim, kunoichi)] != UnitJob.Sabotage, 10 * Tps, "the job dropped");
        Assert.Equal(0, sim.Economy.Sabotages);
    }

    [Fact]
    public void SabotageGoesInTheCommandLog()
    {
        var log = new CommandLog();
        log.Add(12, new SabotageCommand(0, new[] { 3, 4 }, 7));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(new SabotageCommand(0, new[] { 3, 4 }, 7), back.Commands.Single().Command);
    }

    [Fact]
    public void SameSabotageSameHash()
    {
        ulong Play()
        {
            var sim = Flat();
            var hall = Place(sim, 1, "town_hall", 36, 36);
            hall.Stock[(int)Good.Food] = 300;
            int kunoichi = sim.SpawnUnit(0, "kunoichi", 24, 24, Stand);
            sim.Schedule(0, new SabotageCommand(0, new[] { kunoichi }, hall.Id));
            sim.Run(30 * Tps);
            Assert.Equal(1, sim.Economy.Sabotages);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    /// <summary>
    /// Player 0, a Clan Kage computer player with the tech and a wave of kunoichi, against player 1, who gives no orders;
    /// a smelter of player 1's stands by the way between the two halls.
    /// </summary>
    [Fact]
    public void AComputerPlayersWaveSendsASaboteurToARivalSmelterItPasses()
    {
        foreach (var level in new[] { AiLevel.Easy, AiLevel.Normal })
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(128, 16, 128, Ground));
            sim.SetFaction(0, "kage");
            sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
            sim.Map.Starts.Add(new StartArea(1, 104, Ground + 1, 64));
            sim.PlaceStartingBuildings();
            Learn(sim, 0);
            for (int k = 0; k < 18; k++) sim.SpawnUnit(0, "kunoichi", 14 + k, 72 + k % 2 * 2);
            var smelter = Place(sim, 1, "smelter", 54, 58);
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            bool done = false;
            for (int t = 0; t < AiPlayer.SmartFirstWaveTicks + 5 * 60 * Tps && !done; t++)
            {
                ai.Think(sim, c =>
                {
                    log.Add(c);
                    sim.Schedule(sim.Tick, c);
                });
                sim.Step();
                done = sim.Economy.Stalled(smelter);
            }
            Assert.True(done, $"{level}: the smelter sabotaged (sent {ai.SaboteursSent}, waves {ai.Waves})");
            var sent = log.OfType<SabotageCommand>().First();
            Assert.Equal(smelter.Id, sent.Building);
            Assert.Single(sent.Units);
        }
    }
}
