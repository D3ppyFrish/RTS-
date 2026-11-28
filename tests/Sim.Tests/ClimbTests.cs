using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.World;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage's wall climbing (see "Clan Kage" in docs/rules.md, <c>Economy/EconomySystem.Climb.cs</c>): kunoichi, shinobi
/// and shuriken throwers climb up and down walls and cliffs with no ladder.
/// </summary>
public class ClimbTests
{
    private const int Tps = Simulation.TicksPerSecond;
    private const int Ground = 3; // units stand at 4
    private const int Foot = Ground + 1;
    private const int CliffX = 40; // a cliff from here east: units stand on top at Foot + its height
    private const int Z = 32;

    private static Simulation Cliff(int height = 5, bool learn = true)
    {
        var w = TestWorlds.Flat(64, 24, 64, Ground);
        TestWorlds.Box(w, CliffX, Ground + 1, 0, 63, Ground + height, 63, BlockType.Stone);
        return Kage(TestWorlds.Sim(w), learn);
    }

    /// <summary>Flat ground with player 1's wooden wall right across the map at x = 40, and no way round it.</summary>
    private static Simulation Walled()
    {
        var w = TestWorlds.Flat(64, 16, 64, Ground);
        var sim = TestWorlds.Sim(w);
        for (int z = 1; z < 63; z++) Assert.NotNull(sim.Economy.PlaceFinished(1, sim.Content.Building("wooden_wall"), 40, z));
        foreach (int z in new[] { 0, 63 }) TestWorlds.Box(w, 40, Ground + 1, z, 40, 12, z, BlockType.Bedrock);
        return Kage(sim, true);
    }

    private static Simulation Kage(Simulation sim, bool learn)
    {
        sim.Fog = false;
        sim.SetFaction(0, "kage");
        if (learn) Learn(sim, 0);
        return sim;
    }

    private static void Learn(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("kage_wall_climbing"));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int X(Simulation sim, int id) => sim.Units.X[Index(sim, id)].FloorToInt();
    private static int Y(Simulation sim, int id) => sim.Units.Y[Index(sim, id)];

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
    public void WallClimbingIsClanKagesTechForTheirKunoichiShinobiAndShurikenThrowers()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("kage_wall_climbing");
        Assert.Equal("kage", tech.Faction?.Id);
        Assert.Contains(content.Tech("kage_smoke_bombs"), tech.Requires);
        Assert.Equal(new[] { "kunoichi", "shinobi", "shuriken_thrower" },
            tech.Effects.Where(e => e.Type == TechEffectType.WallClimbing).Select(e => e.Unit!.Id));
        var rules = content.Combat.Climb;
        Assert.Equal((Tps, 10, 2), (rules.TicksPerLevel, rules.Height, rules.Reach));
        Assert.Equal(6, content.Techs.Count(t => t.Faction?.Id == "kage"));

        var sim = Cliff(learn: false);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 20, 20, Foot);
        int caster = sim.SpawnUnit(0, "shadow_caster", 22, 20, Foot);
        Assert.False(sim.Economy.CanClimb(Index(sim, kunoichi))); // not before the tech
        Learn(sim, 0);
        Assert.True(sim.Economy.ReadyToClimb(Index(sim, kunoichi)));
        Assert.False(sim.Economy.CanClimb(Index(sim, caster))); // casters do not
    }

    [Fact]
    public void AKunoichiClimbsACliffNoOneElseCanAndTakesASecondALevel()
    {
        var sim = Cliff();
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 30, Z, Foot);
        int sapper = sim.SpawnUnit(0, "sapper", 30, Z + 4, Foot);
        sim.Schedule(0, new ClimbCommand(0, new[] { kunoichi }, 41, Z));
        sim.Schedule(0, new ClimbCommand(0, new[] { sapper }, 41, Z + 4)); // not a sapper's to do
        sim.Step();
        Assert.Equal(UnitJob.Climb, sim.Units.Job[Index(sim, kunoichi)]);
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, sapper)]);
        // The face nearest it: from the foot at x = 39 onto the top at x = 40, 5 levels up.
        Assert.True(sim.Economy.ClimbOf(Index(sim, kunoichi), out var route));
        Assert.Equal(new EconomySystem.ClimbRoute(CliffX - 1, Z, Foot, CliffX, Z, Foot + 5), route);

        RunUntil(sim, () => sim.Units.Phase[Index(sim, kunoichi)] == JobPhase.Working, 20 * Tps, "the kunoichi at the foot");
        sim.Run(5 * Tps - 2);
        Assert.Equal(Foot, Y(sim, kunoichi)); // still on the face
        sim.Run(3);
        Assert.Equal((CliffX, Foot + 5), (X(sim, kunoichi), Y(sim, kunoichi)));
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, kunoichi)]);
        Assert.Equal(1, sim.Economy.Climbs);

        // And on from there over the top, as anyone walks.
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { kunoichi }, 55, Z));
        RunUntil(sim, () => X(sim, kunoichi) >= 55, 20 * Tps, "the kunoichi across the top");
    }

    [Fact]
    public void ClimbersComeDownACliffTheSameWay()
    {
        var sim = Cliff(height: 8);
        int thrower = sim.SpawnUnit(0, "shuriken_thrower", 45, Z, Foot + 8);
        sim.Schedule(0, new ClimbCommand(0, new[] { thrower }, 38, Z));
        RunUntil(sim, () => Y(sim, thrower) == Foot, 30 * Tps, "the thrower down the cliff");
        Assert.True(X(sim, thrower) < CliffX);
        Assert.Equal(1, sim.Economy.Climbs);
    }

    [Fact]
    public void NoClimbIsTooHighOrNoClimbAtAll()
    {
        var sim = Cliff(height: 11); // higher than anyone climbs
        int shinobi = sim.SpawnUnit(0, "shinobi", 30, Z, Foot);
        sim.Schedule(0, new ClimbCommand(0, new[] { shinobi }, 41, Z));
        sim.Step();
        Assert.Equal(UnitJob.None, sim.Units.Job[Index(sim, shinobi)]);
        Assert.False(sim.Economy.PlanClimb(41, Z, 30, Foot, Z, out _));

        // Clicking the flat ground it stands on is no climb either: it walks there.
        var flat = Cliff();
        int k = flat.SpawnUnit(0, "kunoichi", 20, Z, Foot);
        Assert.False(flat.Economy.PlanClimb(25, Z, 20, Foot, Z, out _));
        // And never on a diagonal, nor a single level (a step).
        Assert.False(flat.Economy.Climbable(CliffX - 1, Z - 1, Foot, CliffX, Z, Foot + 5));
        Assert.True(flat.Economy.Climbable(CliffX - 1, Z, Foot, CliffX, Z, Foot + 5));
        Assert.Equal(UnitJob.None, flat.Units.Job[Index(flat, k)]);
    }

    [Fact]
    public void KunoichiScaleAnEnemyWallStandOnItAndGoOnDownTheFarSide()
    {
        var sim = Walled();
        var kunoichi = new[] { sim.SpawnUnit(0, "kunoichi", 30, Z - 2, Foot), sim.SpawnUnit(0, "kunoichi", 30, Z + 2, Foot) };
        // Without climbing, nobody gets through the wall.
        sim.Schedule(0, new MoveCommand(0, kunoichi, 50, Z));
        sim.Run(15 * Tps);
        Assert.All(kunoichi, id => Assert.True(X(sim, id) < 40));

        int wallTop = Walk.GroundLevel(sim.World, 40, Z);
        sim.Schedule(sim.Tick, new ClimbCommand(0, kunoichi, 40, Z));
        RunUntil(sim, () => kunoichi.All(id => sim.Economy.WallUnder(Index(sim, id)) != null), 30 * Tps, "both up on the wall");
        Assert.All(kunoichi, id => Assert.Equal(wallTop, Y(sim, id)));
        Assert.Equal(2, sim.Economy.Climbs);

        // Sent on beyond it, they climb down the far side of it and walk on.
        sim.Schedule(sim.Tick, new MoveCommand(0, kunoichi, 50, Z));
        RunUntil(sim, () => kunoichi.All(id => X(sim, id) >= 48), 30 * Tps, "both beyond the wall");
    }

    [Fact]
    public void AClimberThatIsHitLetsGoAndFights()
    {
        var sim = Cliff(height: 10);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 36, Z, Foot);
        sim.Schedule(0, new ClimbCommand(0, new[] { kunoichi }, 41, Z));
        RunUntil(sim, () => sim.Units.Phase[Index(sim, kunoichi)] == JobPhase.Working, 20 * Tps, "the kunoichi on the face");
        int archer = sim.SpawnUnit(1, "archer", 34, Z, Foot);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { archer }, kunoichi));
        RunUntil(sim, () => sim.Units.Job[Index(sim, kunoichi)] != UnitJob.Climb, 10 * Tps, "the climb dropped");
        Assert.Equal(Foot, Y(sim, kunoichi));
        Assert.Equal(0, sim.Economy.Climbs);
    }

    [Fact]
    public void ClimbingGoesInTheCommandLog()
    {
        var log = new CommandLog();
        log.Add(12, new ClimbCommand(0, new[] { 3, 4 }, 40, 32));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(new ClimbCommand(0, new[] { 3, 4 }, 40, 32), back.Commands.Single().Command);
    }

    [Fact]
    public void SameClimbSameHash()
    {
        ulong Play()
        {
            var sim = Walled();
            var units = new[] { sim.SpawnUnit(0, "kunoichi", 30, Z, Foot), sim.SpawnUnit(0, "shinobi", 30, Z + 3, Foot) };
            sim.Schedule(0, new ClimbCommand(0, units, 40, Z + 1));
            sim.Run(20 * Tps);
            Assert.Equal(2, sim.Economy.Climbs);
            return sim.StateHash();
        }
        Assert.Equal(Play(), Play());
    }

    /// <summary>
    /// Player 0, a Clan Kage computer player with the tech and a wave of kunoichi (and no sappers to raise a ladder), in
    /// the west, against player 1, who gives no orders, behind a stone wall right across the map at x = 80.
    /// </summary>
    [Theory]
    [InlineData(AiLevel.Easy)]
    [InlineData(AiLevel.Normal)]
    [InlineData(AiLevel.Hard)]
    public void AComputerPlayersWaveClimbsARivalWallAndGoesOnOverIt(AiLevel level)
    {
        const int size = 128, wallX = 80;
        var w = TestWorlds.Flat(size, 32, size, Ground);
        var sim = TestWorlds.Sim(w);
        sim.SetFaction(0, "kage");
        sim.Map.Starts.Add(new StartArea(0, 24, Ground + 1, 64));
        sim.Map.Starts.Add(new StartArea(1, 104, Ground + 1, 64));
        sim.PlaceStartingBuildings();
        for (int z = 1; z < size - 1; z++) Assert.NotNull(sim.Economy.PlaceFinished(1, sim.Content.Building("stone_wall"), wallX, z));
        foreach (int z in new[] { 0, size - 1 }) TestWorlds.Box(w, wallX, Ground + 1, z, wallX, 14, z, BlockType.Bedrock);
        Learn(sim, 0);
        var kunoichi = Enumerable.Range(0, 10).Select(k => sim.SpawnUnit(0, "kunoichi", 40 + k, 60)).ToList();
        var ai = new AiPlayer(0, 1, level);
        var hall = sim.Economy.TownHall(0)!;
        bool over = false;
        var log = new System.Collections.Generic.List<Command>();
        for (int t = 0; t < AiPlayer.SmartFirstWaveTicks + 6 * 60 * Tps && !over; t++)
        {
            hall.Stock[(int)Good.Wood] = Math.Max(hall.Stock[(int)Good.Wood], 200);
            hall.Stock[(int)Good.Food] = Math.Max(hall.Stock[(int)Good.Food], 200);
            ai.Think(sim, c => { log.Add(c); sim.Schedule(sim.Tick, c); });
            sim.Step();
            over = kunoichi.Count(id => sim.Units.IndexOf(id) is var i && i >= 0 && sim.Units.X[i].FloorToInt() > wallX) >= 2;
        }
        Assert.True(over, $"{level}: kunoichi over the wall (climbs sent {ai.ClimbsSent}, climbs made {sim.Economy.Climbs}, waves {ai.Waves})");
        var sent = log.OfType<ClimbCommand>().First();
        Assert.Equal(wallX, sent.X);
        Assert.InRange(sent.Units.Length, 1, AiPlayer.WallClimbers);
        Assert.True(sim.Economy.Climbs >= 2);
        Assert.Empty(log.OfType<LadderCommand>()); // no sappers: they climb
    }
}
