using System;
using System.Linq;
using System.Collections.Generic;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.World;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>Issue #146: Better tools gives aimed felling and prospecting in place of faster gathering.</summary>
public class ToolsTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64) => TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));

    private static void Tools(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("tools"));

    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[sim.Units.IndexOf(id)] : 0;

    private static void RunUntil(Simulation sim, Func<bool> done, int maxTicks, string what)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return;
            sim.Step();
        }
        Assert.True(done(), $"timed out waiting for {what}");
    }

    private static bool Standing(Simulation sim, int x, int z) =>
        Enumerable.Range(Ground + 1, 12).Any(y => BlockInfo.IsTrunk(sim.World.Get(x, y, z)));

    [Fact]
    public void BetterToolsGivesFellingAndProspectingNotFasterGathering()
    {
        var db = ContentLoader.LoadEmbedded();
        var effects = db.Tech("tools").Effects;
        Assert.DoesNotContain(effects, e => e.Type == TechEffectType.GatherSpeed);
        Assert.Contains(effects, e => e.Type == TechEffectType.Felling && e.Unit?.Id == "militia" && e.Ticks == 4 * Tps && e.Amount == 60);
        Assert.Contains(effects, e => e.Type == TechEffectType.Prospecting && e.Unit?.Id == "foreman" && e.Ticks == 8 * Tps && e.Amount == 12);

        var sim = Flat();
        sim.SetFaction(1, "orcs");
        Tools(sim, 0);
        Tools(sim, 1);
        Assert.True(sim.Economy.CanFell(0, db.Unit("militia")));
        Assert.False(sim.Economy.CanFell(0, db.Unit("worker"))); // a Settler worker leaves it to the militia
        Assert.True(sim.Economy.CanProspect(0, db.Unit("foreman")));
        Assert.False(sim.Economy.CanProspect(0, db.Unit("worker")));
        Assert.True(sim.Economy.CanFell(1, db.Unit("worker"))); // an Orc worker does both, twice as slowly
        Assert.True(sim.Economy.CanProspect(1, db.Unit("worker")));
        int militia = sim.SpawnUnit(0, "militia", 10, 10), orc = sim.SpawnUnit(1, "worker", 12, 10);
        Assert.Equal(2 * sim.Economy.FellTicksOf(sim.Units.IndexOf(militia)), sim.Economy.FellTicksOf(sim.Units.IndexOf(orc)));
        // Gathering is as quick with the tech as without.
        Assert.Equal(sim.Economy.WorkTicks(2, BlockType.Stone), sim.Economy.WorkTicks(0, BlockType.Stone));
    }

    [Fact]
    public void ATreeFelledTowardsAnEnemyHitsItAndLeavesItsWoodThere()
    {
        var sim = Flat();
        TestWorlds.Tree(sim.World, 20, 20, 5);
        int militia = sim.SpawnUnit(0, "militia", 18, 20);
        int enemy = sim.SpawnUnit(1, "spearman", 24, 20);
        int aside = sim.SpawnUnit(1, "spearman", 24, 27);
        sim.Schedule(0, new StanceCommand(1, new[] { enemy, aside }, UnitStance.Hold));
        sim.Schedule(0, new StanceCommand(0, new[] { militia }, UnitStance.Hold)); // no fighting: only the tree hits

        // Without the tech the order does nothing.
        sim.Schedule(0, new FellCommand(0, new[] { militia }, 20, 20, 1, 0));
        sim.Run(8 * Tps);
        Assert.True(Standing(sim, 20, 20));

        Tools(sim, 0);
        int hp = Hp(sim, enemy), asideHp = Hp(sim, aside);
        sim.Schedule(sim.Tick, new FellCommand(0, new[] { militia }, 21, 21, 1, 0)); // a click on the crown finds it
        RunUntil(sim, () => !Standing(sim, 20, 20), 10 * Tps, "the tree to come down");
        RunUntil(sim, () => sim.Physics.FallingTrees.Count == 0, 2 * Tps, "the tree to land");
        sim.Run(2);
        Assert.True(hp - Hp(sim, enemy) >= 50, $"the enemy took {hp - Hp(sim, enemy)}");
        Assert.Equal(asideHp, Hp(sim, aside));
        Assert.Equal(sim.Content.Unit("militia").MaxHp, Hp(sim, militia)); // the feller stood behind it
        // It fell east, its wood lying there to gather.
        Assert.Equal(BlockType.TreeRubble, sim.World.Get(21, Ground + 1, 20));
        Assert.Equal(BlockType.TreeRubble, sim.World.Get(23, Ground + 1, 20));
        Assert.Equal(BlockType.Air, sim.World.Get(19, Ground + 1, 20));
    }

    [Fact]
    public void ABigTreeFallsItsWholeLengthAndSparesItsFeller()
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(96, 40, 96, Ground));
        sim.World.TreeScale = 2;
        Assert.True(Rts.Sim.World.Trees.Grow(sim.World, 30, Ground, 30, 5, 0, TreeKind.Broadleaf, 1));
        int militia = sim.SpawnUnit(0, "militia", 26, 31);
        int enemy = sim.SpawnUnit(1, "spearman", 40, 30);
        sim.Schedule(0, new StanceCommand(1, new[] { enemy }, UnitStance.Hold));
        sim.Schedule(0, new StanceCommand(0, new[] { militia }, UnitStance.Hold));
        Tools(sim, 0);
        int hp = Hp(sim, enemy);
        sim.Schedule(0, new FellCommand(0, new[] { militia }, 30, 30, 1, 0));
        RunUntil(sim, () => !Standing(sim, 30, 30) && !Standing(sim, 31, 30), 20 * Tps, "the tree to come down");
        RunUntil(sim, () => sim.Physics.FallingTrees.Count == 0, 2 * Tps, "the tree to land");
        sim.Run(2);
        Assert.Equal(sim.Content.Unit("militia").MaxHp, Hp(sim, militia));
        Assert.True(hp - Hp(sim, enemy) >= 50, $"the enemy took {hp - Hp(sim, enemy)}");
    }

    [Fact]
    public void ATreeFelledOntoABuildingDamagesIt()
    {
        var sim = Flat();
        TestWorlds.Tree(sim.World, 20, 20, 5);
        var house = sim.Economy.PlaceFinished(1, sim.Content.Building("house"), 22, 19) ?? throw new InvalidOperationException("no room");
        int blocks = Intact(house);
        int militia = sim.SpawnUnit(0, "militia", 18, 20);
        Tools(sim, 0);
        sim.Schedule(0, new FellCommand(0, new[] { militia }, 20, 20, 1, 0));
        RunUntil(sim, () => !Standing(sim, 20, 20), 10 * Tps, "the tree to come down");
        RunUntil(sim, () => sim.Physics.FallingTrees.Count == 0, 2 * Tps, "the tree to land");
        Assert.True(house.Damage > 0 || Intact(house) < blocks, "the house took no damage");

        int Intact(Building b)
        {
            int n = 0;
            foreach (var t in b.Shape.Blocks)
                if (sim.World.Get(b.X + t.X, b.Y + t.Y, b.Z + t.Z) == t.Block) n++;
            return n;
        }
    }

    [Fact]
    public void ATrunkFelledAcrossAOneBlockGapLetsUnitsCross()
    {
        var sim = Flat();
        // A trench a block wide and three deep, right across the map.
        TestWorlds.Box(sim.World, 26, 1, 0, 26, Ground, 63, BlockType.Air);
        TestWorlds.Tree(sim.World, 24, 20, 4);

        int before = sim.SpawnUnit(0, "spearman", 20, 30);
        sim.Schedule(0, new MoveCommand(0, new[] { before }, 32, 30));
        sim.Run(15 * Tps);
        int i = sim.Units.IndexOf(before);
        Assert.False(sim.Units.X[i].FloorToInt() > 26 && sim.Units.Y[i] == Ground + 1, "it got across with no bridge");

        int militia = sim.SpawnUnit(0, "militia", 22, 20);
        Tools(sim, 0);
        sim.Schedule(sim.Tick, new FellCommand(0, new[] { militia }, 24, 20, 1, 0));
        RunUntil(sim, () => !Standing(sim, 24, 20), 10 * Tps, "the tree to come down");
        RunUntil(sim, () => sim.Physics.FallingTrees.Count == 0, 2 * Tps, "the tree to land");
        sim.Run(2);
        // The log lies across the trench at the ground's level, resting on both sides.
        Assert.Equal(BlockType.TreeRubble, sim.World.Get(26, Ground + 1, 20));
        Assert.Equal(BlockType.Air, sim.World.Get(26, Ground, 20));
        Assert.Equal(BlockType.TreeRubble, sim.World.Get(27, Ground + 1, 20));

        int after = sim.SpawnUnit(0, "spearman", 20, 18);
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { after }, 32, 18));
        RunUntil(sim, () =>
        {
            int j = sim.Units.IndexOf(after);
            return sim.Units.X[j].FloorToInt() >= 30 && sim.Units.Y[j] == Ground + 1;
        }, 40 * Tps, "a unit to cross on the log");
    }

    [Fact]
    public void ProspectingShowsBuriedOreWithinTheRadiusAndNothingOutside()
    {
        var sim = Flat();
        sim.World.Set(20, 1, 20, BlockType.IronOre);   // buried under the grass
        sim.World.Set(28, 2, 26, BlockType.GoldOre);   // 10 away
        sim.World.Set(33, 1, 20, BlockType.Coal);      // 13 away: outside
        int foreman = sim.SpawnUnit(0, "foreman", 16, 20);

        sim.Schedule(0, new ProspectCommand(0, new[] { foreman }, 20, 20));
        sim.Run(12 * Tps);
        Assert.False(sim.Economy.Prospected(0, 20, 1, 20)); // no tech: nothing

        Tools(sim, 0);
        sim.Schedule(sim.Tick, new ProspectCommand(0, new[] { foreman }, 20, 20));
        sim.Run(5 * Tps);
        Assert.False(sim.Economy.Prospected(0, 20, 1, 20)); // still sounding
        RunUntil(sim, () => sim.Economy.Soundings(0) == 1, 10 * Tps, "the sounding");
        Assert.True(sim.Economy.Prospected(0, 20, 1, 20));
        Assert.True(sim.Economy.Prospected(0, 28, 2, 26));
        Assert.False(sim.Economy.Prospected(0, 33, 1, 20));
        Assert.False(sim.Economy.Prospected(1, 20, 1, 20)); // only its own player knows
        var ore = sim.Economy.ProspectedOre(0);
        Assert.Equal(2, ore.Count);
        Assert.Contains((20, 1, 20, BlockType.IronOre), ore);
        Assert.Equal(UnitJob.None, sim.Units.Job[sim.Units.IndexOf(foreman)]);

        // Dug out, it no longer shows.
        sim.World.Set(20, 1, 20, BlockType.Stone);
        Assert.Single(sim.Economy.ProspectedOre(0));
    }

    [Fact]
    public void OrcWorkersProspectTwiceAsSlowly()
    {
        var sim = Flat();
        sim.SetFaction(0, "orcs");
        sim.World.Set(20, 1, 20, BlockType.IronOre);
        int worker = sim.SpawnUnit(0, "worker", 19, 20);
        Tools(sim, 0);
        sim.Schedule(0, new ProspectCommand(0, new[] { worker }, 20, 20));
        sim.Run(12 * Tps);
        Assert.Equal(0, sim.Economy.Soundings(0));
        sim.Run(6 * Tps);
        Assert.Equal(1, sim.Economy.Soundings(0));
        Assert.True(sim.Economy.Prospected(0, 20, 1, 20));
    }

    [Fact]
    public void FellAndProspectOrdersReplayFromTheLog()
    {
        var log = new CommandLog();
        log.Add(5, new FellCommand(0, new[] { 3, 4 }, 40, 52, 0, -1));
        log.Add(6, new ProspectCommand(1, new[] { 8 }, 12, 13));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(log.Commands.Select(c => c.Command), back.Commands.Select(c => c.Command));
    }

    [Fact]
    public void ComputerPlayersSoundTheGroundOnceByEachStorehouse()
    {
        foreach (var level in new[] { AiLevel.Easy, AiLevel.Normal, AiLevel.Hard })
        {
            var sim = TestWorlds.Sim(TestWorlds.Flat(96, 16, 96, Ground));
            sim.SetFaction(0, "orcs");
            sim.Map.Starts.Add(new StartArea(0, 30, Ground + 1, 48));
            sim.PlaceStartingBuildings();
            var hall = sim.Economy.TownHall(0)!;
            sim.World.Set(hall.X, 1, hall.MaxZ + 4, BlockType.IronOre);
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            for (int t = 0; t < 30 * Tps && !log.Any(c => c is ProspectCommand); t++)
            {
                ai.Think(sim, c => { log.Add(c); sim.Schedule(sim.Tick, c); });
                sim.Step();
            }
            Assert.Empty(log.OfType<ProspectCommand>()); // nothing to sound with yet
            Tools(sim, 0);
            for (int t = 0; t < 60 * Tps && sim.Economy.Soundings(0) == 0; t++)
            {
                ai.Think(sim, c => { log.Add(c); sim.Schedule(sim.Tick, c); });
                sim.Step();
            }
            Assert.Equal(1, sim.Economy.Soundings(0));
            Assert.True(sim.Economy.Prospected(0, hall.X, 1, hall.MaxZ + 4), $"{level}: the ore by the hall");
            for (int t = 0; t < 30 * Tps; t++)
            {
                ai.Think(sim, c => { log.Add(c); sim.Schedule(sim.Tick, c); });
                sim.Step();
            }
            Assert.Single(log.OfType<ProspectCommand>()); // once
        }
    }
}
