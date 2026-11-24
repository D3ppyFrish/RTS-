using System.Linq;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Test mode (issue #89): its commands, only in a test mode match.</summary>
public class SandboxTests
{
    private static Simulation TestSim(bool testMode = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64));
        sim.TestMode = testMode;
        return sim;
    }

    private static void Do(Simulation sim, Command c)
    {
        sim.Schedule(sim.Tick, c);
        sim.Step();
    }

    private static int Count(Simulation sim, int owner, string unit) =>
        Enumerable.Range(0, sim.Units.Count).Count(i => sim.Units.Owner[i] == owner && sim.Content.Units[sim.Units.Def[i]].Id == unit);

    [Fact]
    public void SandboxCommandsDoNothingOutsideTestMode()
    {
        var sim = TestSim(testMode: false);
        var before = sim.StateHash();
        var plain = TestSim(testMode: false);
        Do(sim, new SandboxUnitCommand(0, 1, "spearman", 20, 20, 5));
        Do(sim, new SandboxBlocksCommand(0, BlockType.Water, 10, 4, 10, 12, 6, 12));
        Do(sim, new SandboxTreeCommand(0, TreeKind.Pine, 30, 30));
        Do(sim, new SandboxCheatCommand(0, 0, SandboxCheat.Invulnerable, true));
        plain.Run(4);
        Assert.Equal(0, sim.Units.Count);
        Assert.Equal(BlockType.Air, sim.World.Get(10, 4, 10));
        Assert.False(sim.Sandbox.Invulnerable(0));
        Assert.Equal(plain.StateHash(), sim.StateHash());
        Assert.NotEqual(0UL, before);
    }

    [Fact]
    public void TestModeIsSetBeforeTheFirstTickAndCountsInTheHash()
    {
        var a = TestSim(testMode: false);
        var b = TestSim(testMode: true);
        Assert.NotEqual(a.StateHash(), b.StateHash());
        b.Step();
        Assert.Throws<System.InvalidOperationException>(() => b.TestMode = false);
    }

    [Fact]
    public void UnitsOfAnyOwnerArePlacedAroundTheClick()
    {
        var sim = TestSim();
        Do(sim, new SandboxUnitCommand(0, 1, "spearman", 30, 30, 6));
        Do(sim, new SandboxUnitCommand(0, Simulation.WildPlayer, "wolf", 10, 10));
        Do(sim, new SandboxUnitCommand(0, 0, "no_such_unit", 10, 10));
        Assert.Equal(6, Count(sim, 1, "spearman"));
        Assert.Equal(1, Count(sim, Simulation.WildPlayer, "wolf"));
        Assert.Equal(7, sim.Units.Count);
        for (int i = 0; i < sim.Units.Count; i++)
            if (sim.Units.Owner[i] == 1)
            {
                Assert.InRange(sim.Units.X[i].FloorToInt(), 26, 34);
                Assert.InRange(sim.Units.Z[i].FloorToInt(), 26, 34);
            }
    }

    [Fact]
    public void BuildingsGoUpFinishedAndATownHallJoinsItsOwnerToTheMatch()
    {
        var sim = TestSim();
        Do(sim, new SandboxBuildingCommand(0, 1, "barracks", 20, 20));
        Do(sim, new SandboxBuildingCommand(0, 1, "town_hall", 40, 40));
        Assert.Contains(sim.Buildings.All, b => b.Owner == 1 && b.Def.Id == "barracks" && b.Active);
        Assert.Contains(1, sim.Match.Players);
        Assert.NotNull(sim.Economy.TownHall(1));
    }

    [Theory]
    [InlineData(TreeKind.Broadleaf, BlockType.Wood)]
    [InlineData(TreeKind.Pine, BlockType.PineWood)]
    [InlineData(TreeKind.Birch, BlockType.BirchWood)]
    [InlineData(TreeKind.Willow, BlockType.Wood)]
    [InlineData(TreeKind.Mushroom, BlockType.MushroomStem)]
    public void EveryKindOfTreeGrowsWhereClicked(TreeKind kind, BlockType trunk)
    {
        var sim = TestSim();
        Do(sim, new SandboxTreeCommand(0, kind, 30, 30));
        Assert.Equal(trunk, sim.World.Get(30, 4, 30));
        Assert.Equal(trunk, sim.World.Get(30, 6, 30));
        int leaves = 0;
        for (int y = 4; y < sim.World.SizeY; y++)
        for (int z = 27; z <= 33; z++)
        for (int x = 27; x <= 33; x++)
            if (BlockInfo.IsLeaves(sim.World.Get(x, y, z))) leaves++;
        Assert.True(leaves > 5);
    }

    [Fact]
    public void BlocksFillABoxButSpareBedrockAndBuildings()
    {
        var sim = TestSim();
        Do(sim, new SandboxBuildingCommand(0, 0, "house", 20, 20));
        var house = sim.Buildings.All.Single();
        Do(sim, new SandboxBlocksCommand(0, BlockType.Stone, 18, 0, 18, 24, 5, 24));
        Assert.Equal(BlockType.Bedrock, sim.World.Get(18, 0, 18));
        Assert.Equal(BlockType.Stone, sim.World.Get(18, 5, 18));
        foreach (var t in house.Def.Blocks)
            if (t.Y >= 0 && t.Y + house.Y <= 5) Assert.Equal(t.Block, sim.World.Get(house.X + t.X, house.Y + t.Y, house.Z + t.Z));
        // Air clears; a box over the limit does nothing.
        Do(sim, new SandboxBlocksCommand(0, BlockType.Air, 18, 4, 18, 18, 5, 18));
        Assert.Equal(BlockType.Air, sim.World.Get(18, 5, 18));
        Do(sim, new SandboxBlocksCommand(0, BlockType.Lava, 0, 4, 0, 63, 20, 63));
        Assert.Equal(BlockType.Air, sim.World.Get(40, 4, 40));
    }

    [Fact]
    public void WaterPutDownLowFillsTheTrenchBesideItButHighWaterStays()
    {
        var sim = TestSim(); // sea level 5 in a world 24 high; the ground's top is at 3
        Do(sim, new SandboxBlocksCommand(0, BlockType.Air, 26, 2, 30, 30, 3, 30)); // a trench
        Do(sim, new SandboxBlocksCommand(0, BlockType.Water, 26, 2, 30, 26, 3, 30)); // water at its end
        sim.Run(60);
        Assert.Equal(BlockType.Water, sim.World.Get(30, 2, 30));
        Assert.Equal(BlockType.Air, sim.World.Get(31, 4, 30)); // no higher than the ground around
        Do(sim, new SandboxBlocksCommand(0, BlockType.Water, 10, 10, 10, 10, 10, 10)); // high up: it stays
        sim.Run(20);
        Assert.Equal(BlockType.Water, sim.World.Get(10, 10, 10));
        Assert.Equal(BlockType.Air, sim.World.Get(10, 9, 10));
    }

    [Fact]
    public void LairsComeWithTheirPackAndBloodStainsTheGround()
    {
        var sim = TestSim();
        Do(sim, new SandboxLairCommand(0, "wolf", 32, 32));
        Do(sim, new SandboxLairCommand(0, "spearman", 10, 10)); // keeps no lair
        var lair = Assert.Single(sim.Monsters.Lairs);
        Assert.NotEmpty(lair.Pack);
        Do(sim, new SandboxBloodCommand(0, 10, 50, 2, 100));
        Assert.True(sim.Blood.At(10, 50) > 0);
        Assert.True(sim.Blood.At(12, 50) > 0);
        Assert.Equal(0, sim.Blood.At(14, 50));
    }

    [Fact]
    public void GoodsAndTechsAreHandedOut()
    {
        var sim = TestSim();
        Do(sim, new SandboxBuildingCommand(0, 0, "town_hall", 30, 30));
        Do(sim, new SandboxGoodsCommand(0, 0, 500));
        Assert.All(sim.Economy.Stockpile(0), g => Assert.Equal(500, g));
        Do(sim, new SandboxTechCommand(0, 0, "farming"));
        Assert.True(sim.Economy.HasTech(0, sim.Content.Tech("farming")));
        Do(sim, new SandboxTechCommand(0, 0, "all"));
        Assert.All(sim.Content.Techs, t => Assert.True(sim.Economy.HasTech(0, t)));
    }

    [Fact]
    public void InstantBuildFinishesSitesWithNoWorkers()
    {
        var sim = TestSim();
        Do(sim, new SandboxBuildingCommand(0, 0, "town_hall", 30, 30));
        Do(sim, new SandboxGoodsCommand(0, 0, 1000));
        Do(sim, new SandboxCheatCommand(0, 0, SandboxCheat.InstantBuild, true));
        Do(sim, new BuildCommand(0, System.Array.Empty<int>(), "house", 10, 10));
        sim.Run(5);
        var house = sim.Buildings.All.Single(b => b.Def.Id == "house");
        Assert.True(house.Active);
    }

    [Fact]
    public void InstantTrainingAndResearch()
    {
        var sim = TestSim();
        Do(sim, new SandboxBuildingCommand(0, 0, "town_hall", 30, 30));
        Do(sim, new SandboxBuildingCommand(0, 0, "house", 10, 10));
        Do(sim, new SandboxGoodsCommand(0, 0, 1000));
        Do(sim, new SandboxCheatCommand(0, 0, SandboxCheat.InstantTrain, true));
        Do(sim, new SandboxCheatCommand(0, 0, SandboxCheat.InstantResearch, true));
        var hall = sim.Economy.TownHall(0)!;
        Do(sim, new TrainCommand(0, hall.Id, "worker"));
        Do(sim, new ResearchCommand(0, "farming"));
        sim.Run(3);
        Assert.Equal(1, Count(sim, 0, "worker"));
        Assert.True(sim.Economy.HasTech(0, sim.Content.Tech("farming")));
    }

    [Fact]
    public void InvulnerableUnitsTakeNoDamageButATestModeKillStillKills()
    {
        var sim = TestSim();
        Do(sim, new SandboxUnitCommand(0, 0, "spearman", 30, 30));
        Do(sim, new SandboxUnitCommand(0, 1, "spearman", 31, 30, 3));
        Do(sim, new SandboxCheatCommand(0, 0, SandboxCheat.Invulnerable, true));
        int mine = sim.Units.Id[Enumerable.Range(0, sim.Units.Count).First(i => sim.Units.Owner[i] == 0)];
        int hp = sim.Units.Hp[sim.Units.IndexOf(mine)];
        sim.Run(400);
        Assert.Equal(hp, sim.Units.Hp[sim.Units.IndexOf(mine)]);
        Do(sim, new SandboxUnitsCommand(0, new[] { mine }, SandboxAction.Kill));
        Assert.False(sim.Units.Exists(mine));
    }

    [Fact]
    public void UnitActionsHealBleedSeasonAndRemove()
    {
        var sim = TestSim();
        Do(sim, new SandboxUnitCommand(0, 1, "spearman", 30, 30, 2));
        int a = sim.Units.Id[0], b = sim.Units.Id[1];
        int ia = sim.Units.IndexOf(a);
        int full = sim.Units.Hp[ia];
        sim.Units.Hp[ia] = 1;
        Do(sim, new SandboxUnitsCommand(0, new[] { a }, SandboxAction.Heal));
        Assert.Equal(full, sim.Units.Hp[sim.Units.IndexOf(a)]);
        Do(sim, new SandboxUnitsCommand(0, new[] { a }, SandboxAction.Bleed));
        Assert.NotEqual(0, sim.Units.Bleeding[sim.Units.IndexOf(a)]);
        Do(sim, new SandboxUnitsCommand(0, new[] { a }, SandboxAction.Veteran));
        Assert.Equal(Units.EvolutionSystem.MaxXp, sim.Units.Xp[sim.Units.IndexOf(a)]);
        Do(sim, new SandboxUnitsCommand(0, new[] { a }, SandboxAction.Wound));
        Assert.Equal(full / 5, sim.Units.Hp[sim.Units.IndexOf(a)]);
        Do(sim, new SandboxUnitsCommand(0, new[] { b }, SandboxAction.Remove));
        Assert.False(sim.Units.Exists(b));
        Assert.True(sim.Units.Exists(a));
    }

    [Fact]
    public void ClearRemovesUnitsAndBuildingsNearby()
    {
        var sim = TestSim();
        Do(sim, new SandboxUnitCommand(0, 0, "spearman", 30, 30, 3));
        Do(sim, new SandboxUnitCommand(0, 0, "spearman", 50, 50));
        Do(sim, new SandboxBuildingCommand(0, 1, "house", 32, 32));
        Do(sim, new SandboxClearCommand(0, 30, 30, 4));
        Assert.Equal(1, sim.Units.Count);
        Assert.Empty(sim.Buildings.All);
        Assert.Equal(BlockType.Air, sim.World.Get(33, 4, 33));
    }

    [Fact]
    public void WeatherAndTimeOfDayAreSetWithDayAndNightOn()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(64, 24, 64));
        sim.TestMode = true;
        sim.DayNight = true;
        sim.Run(100);
        Do(sim, new SandboxWeatherCommand(0, Weather.Storm, 2));
        Assert.Equal(Weather.Storm, sim.Conditions.Weather);
        Do(sim, new SandboxTimeCommand(0, 22 * 60));
        Assert.InRange(sim.Conditions.Minute, 22 * 60, 22 * 60 + 1);
        Assert.Equal(DayPart.Night, sim.Conditions.Part);
        sim.Run(Conditions.HourTicks);
        Assert.InRange(sim.Conditions.Minute, 23 * 60, 23 * 60 + 2);
    }

    [Fact]
    public void ATestModeMatchSavesAndReplaysTheSame()
    {
        var log = CommandLog.UsualStart(3, 64, 32, 64, testMode: true);
        var sim = log.CreateSimulation();
        sim.Recording = log;
        Assert.True(sim.TestMode);
        void Issue(Command c) => sim.Schedule(sim.Tick, c);
        Issue(new SandboxUnitCommand(0, Simulation.WildPlayer, "wolf", 32, 32, 4));
        Issue(new SandboxTreeCommand(0, TreeKind.Willow, 20, 40));
        Issue(new SandboxBlocksCommand(0, BlockType.IronOre, 40, 10, 40, 42, 12, 42));
        Issue(new SandboxCheatCommand(0, 0, SandboxCheat.Invulnerable, true));
        Issue(new SandboxGoodsCommand(0, 1, 300));
        sim.Run(60);
        Issue(new SandboxUnitsCommand(0, new[] { sim.Units.Id[0] }, SandboxAction.Kill));
        Issue(new SandboxClearCommand(0, 10, 10, 3));
        sim.Run(60);

        var text = log.ToString();
        Assert.Contains("test-mode 1", text);
        var copy = CommandLog.Parse(text);
        Assert.Equal(log.Commands, copy.Commands);
        Assert.Equal(text, copy.ToString());
        var replayed = MatchReplay.Run(copy, 60, out var again);
        Assert.Equal(sim.StateHash(), again.StateHash());
        Assert.Equal(sim.Tick, again.Tick);
        Assert.NotEmpty(replayed);
    }

    [Fact]
    public void TestModeDamageKnocksBlocksOutOfABuilding()
    {
        var sim = TestSim();
        var wall = sim.Economy.PlaceFinished(0, sim.Content.Building("stone_wall"), 30, 30)!;
        Do(sim, new SandboxDamageCommand(0, 30, 30, 150));
        Assert.Equal(wall.MaxHealth - 150, wall.Health);
        Assert.True(wall.Damaged);
        Assert.False(sim.Economy.UnderFire(wall)); // not a siege: repairs to it go at the usual pace

        var off = TestSim(testMode: false);
        var other = off.Economy.PlaceFinished(0, off.Content.Building("stone_wall"), 30, 30)!;
        Do(off, new SandboxDamageCommand(0, 30, 30, 150));
        Assert.False(other.Damaged);
    }
}
