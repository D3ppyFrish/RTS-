using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Reptilians, the swamp brood (see "The Reptilians" in docs/rules.md): their own scale guards, blowpipers and marsh
/// shamans in place of everyone's spearmen and riders, archers and mages; all three regenerate, are amphibious (wading at
/// full pace, healing faster in the water and lying hidden in it) and cold-blooded (sluggish at night and in the hills);
/// the shaman's Flood; and Lurkers' strike (<see cref="TechEffectType.Ambush"/>).
/// </summary>
public class ReptiliansTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, bool fog = false)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = fog;
        return sim;
    }

    /// <summary>A pool of water a block deep (where units stand at <see cref="Ground"/>) over the columns from (x0, z0) to (x1, z1).</summary>
    private static void Pool(Simulation sim, int x0, int z0, int x1, int z1)
    {
        for (int z = z0; z <= z1; z++)
        for (int x = x0; x <= x1; x++)
            sim.World.Set(x, Ground, z, BlockType.Water);
    }

    private static int Count(Simulation sim, int owner, string unit)
    {
        int def = sim.Content.Unit(unit).Index, n = 0;
        for (int i = 0; i < sim.Units.Count; i++)
            if (sim.Units.Owner[i] == owner && sim.Units.Def[i] == def) n++;
        return n;
    }

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    [Fact]
    public void TheReptiliansAreTheSixthFaction()
    {
        var content = ContentDb.Default;
        var brood = content.Faction("reptilians");
        Assert.Equal(5, brood.Index);
        Assert.Equal("Reptilians", brood.Name);
        Assert.Equal(DuskTreeUse.Raze, brood.DuskTree);
        Assert.Equal(AiStyle.Waves, brood.AiStyle);
        Assert.Equal(new[] { ("spearman", "scale_guard"), ("rider", "scale_guard"), ("archer", "blowpiper"), ("mage", "marsh_shaman") },
            brood.UnitReplacements.Select(r => (r.From.Id, r.To.Id)));
        var own = new[] { content.Unit("scale_guard"), content.Unit("blowpiper"), content.Unit("marsh_shaman") };
        Assert.All(own, d => Assert.True(d.Amphibious && d.ColdBlooded && d.Regen > 0, d.Id));
        Assert.True(content.Unit("blowpiper").Poison != null);
        var shaman = content.Unit("marsh_shaman");
        Assert.True(shaman.Flood);
        Assert.False(shaman.EarthWall || shaman.Chasm || shaman.IceBridge || shaman.GrowForest);
        Assert.False(content.Unit("mage").Flood);
        Assert.Equal("reptilians", content.Tech("reptile_lurkers").Faction?.Id);
        Assert.Equal("reptilians", content.Building("spawning_mound").Faction?.Id);
    }

    [Fact]
    public void TheReptiliansStartWithTheirOwnUnitsAndNoHorses()
    {
        var sim = Flat(96);
        sim.SetFaction(1, "reptilians");
        sim.Map.Starts.Add(new StartArea(0, 20, Stand, 30));
        sim.Map.Starts.Add(new StartArea(1, 70, Stand, 30));
        sim.SpawnStartingUnits(12);
        Assert.Equal(0, Count(sim, 1, "spearman") + Count(sim, 1, "archer") + Count(sim, 1, "mage") + Count(sim, 1, "rider"));
        Assert.Equal(4, Count(sim, 1, "scale_guard"));
        Assert.Equal(2, Count(sim, 1, "blowpiper"));
        Assert.Equal(2, Count(sim, 1, "marsh_shaman"));
        Assert.False(sim.Economy.Unlocked(1, sim.Content.Unit("spearman")));
        Assert.True(sim.Economy.Unlocked(1, sim.Content.Unit("scale_guard")));
        Assert.False(sim.Economy.Unlocked(0, sim.Content.Unit("scale_guard")));
    }

    /// <summary>Hit points unit (id) gets back over <paramref name="seconds"/> while still in a fight (it was just hit).</summary>
    private static int HealedInAFight(Simulation sim, int id, int seconds)
    {
        int i = Index(sim, id);
        sim.Units.Hp[i] = 20;
        int healed = 0;
        for (int s = 0; s < seconds; s++)
        {
            sim.Units.LastFight[Index(sim, id)] = sim.Tick; // still in the fight
            int was = sim.Units.Hp[Index(sim, id)];
            sim.Run(Tps);
            healed += sim.Units.Hp[Index(sim, id)] - was;
        }
        return healed;
    }

    [Fact]
    public void ScaleGuardsRegenerateInAFightAndTwiceAsFastInTheWater()
    {
        var sim = Flat();
        Pool(sim, 40, 10, 44, 14);
        int dry = sim.SpawnUnit(0, "scale_guard", 20, 20, Stand);
        int wet = sim.SpawnUnit(0, "scale_guard", 42, 12, Ground);
        int spear = sim.SpawnUnit(0, "spearman", 20, 40, Stand);
        Assert.True(sim.Combat.InWater(Index(sim, wet)));
        int onLand = HealedInAFight(sim, dry, 10);
        Assert.InRange(onLand, 8, 9); // 1% of 85 a second
        Assert.InRange(HealedInAFight(sim, wet, 10), 2 * onLand - 1, 2 * onLand + 1);
        Assert.Equal(0, HealedInAFight(sim, spear, 10)); // everyone else heals only out of the fight
    }

    [Fact]
    public void TheColdBloodedStopHealingAtNightAndInTheHills()
    {
        var sim = Flat();
        int guard = sim.SpawnUnit(0, "scale_guard", 20, 20, Stand);
        Assert.False(sim.Combat.Chilled(Index(sim, guard)));
        Assert.True(HealedInAFight(sim, guard, 5) > 0);
        // Up in the hills it is cold at any hour.
        sim.Map.SetBiome(20, 20, Biome.Hills);
        Assert.True(sim.Combat.Chilled(Index(sim, guard)));
        Assert.Equal(0, HealedInAFight(sim, guard, 5));
        sim.Map.SetBiome(20, 20, Biome.Grassland);

        var night = Flat();
        night.DayNight = true;
        night.DayStart = 1 * 60; // 1 in the night
        int cold = night.SpawnUnit(0, "scale_guard", 20, 20, Stand);
        int ogre = night.SpawnUnit(0, "ogre_brute", 30, 20, Stand); // regenerates, but is not cold-blooded
        Assert.True(night.Combat.Chilled(Index(night, cold)));
        Assert.False(night.Combat.Chilled(Index(night, ogre)));
        Assert.Equal(0, HealedInAFight(night, cold, 5));
        Assert.True(HealedInAFight(night, ogre, 5) > 0);
    }

    /// <summary>Blocks unit (id) covers walking east for <paramref name="seconds"/>.</summary>
    private static Fix64 Walked(Simulation sim, int id, int seconds)
    {
        var from = sim.Units.X[Index(sim, id)];
        int z = sim.Units.Z[Index(sim, id)].FloorToInt();
        sim.Schedule(sim.Tick, new MoveCommand(sim.Units.Owner[Index(sim, id)], new[] { id }, 60, z));
        sim.Run(seconds * Tps);
        return sim.Units.X[Index(sim, id)] - from;
    }

    [Fact]
    public void TheColdBloodedWalkSlowerInTheCold()
    {
        var warm = Flat();
        var hills = Flat();
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
            hills.Map.SetBiome(x, z, Biome.Hills);
        Fix64 a = Walked(warm, warm.SpawnUnit(0, "scale_guard", 5, 20, Stand), 4);
        Fix64 b = Walked(hills, hills.SpawnUnit(0, "scale_guard", 5, 20, Stand), 4);
        Assert.InRange((b * 100 / a).RoundToInt(), CombatSystem.ColdPercent - 3, CombatSystem.ColdPercent + 3);
        // The hills do not slow everyone.
        Fix64 c = Walked(warm, warm.SpawnUnit(0, "spearman", 5, 40, Stand), 4);
        Fix64 d = Walked(hills, hills.SpawnUnit(0, "spearman", 5, 40, Stand), 4);
        Assert.Equal(c, d);
    }

    [Fact]
    public void TheColdBloodedStrikeSofterInTheCold()
    {
        var sim = Flat();
        int guard = sim.SpawnUnit(0, "scale_guard", 20, 20, Stand);
        int spear = sim.SpawnUnit(1, "spearman", 21, 20, Stand);
        int i = Index(sim, guard), t = Index(sim, spear);
        var def = sim.Content.Unit("scale_guard");
        int warm = sim.Combat.Damage(i, def, t);
        sim.Map.SetBiome(20, 20, Biome.Hills);
        int cold = sim.Combat.Damage(i, def, t);
        Assert.True(cold < warm);
        Assert.InRange(cold, warm * CombatSystem.ColdPercent / 100 - 1, warm * CombatSystem.ColdPercent / 100 + 1);
    }

    [Fact]
    public void TheAmphibiousWadeAsFastAsTheyWalkAndEveryoneElseWadesSlowly()
    {
        var land = Flat();
        var wet = Flat();
        Pool(wet, 1, 1, 62, 62); // all water, so the way does not leave it (wading costs the path search double)
        Fix64 guardOnLand = Walked(land, land.SpawnUnit(0, "scale_guard", 5, 20, Stand), 4);
        Fix64 guardInWater = Walked(wet, wet.SpawnUnit(0, "scale_guard", 5, 20, Ground), 4);
        Assert.Equal(guardOnLand, guardInWater);
        Fix64 spearOnLand = Walked(land, land.SpawnUnit(0, "spearman", 5, 40, Stand), 4);
        Fix64 spearInWater = Walked(wet, wet.SpawnUnit(0, "spearman", 5, 40, Ground), 4);
        Assert.InRange((spearInWater * 100 / spearOnLand).RoundToInt(), UnitMovement.WadePercent - 3, UnitMovement.WadePercent + 3);
    }

    [Fact]
    public void TheAmphibiousAreNotSlowedByMud()
    {
        var dry = Flat();
        var mud = Flat();
        mud.DayNight = true;
        mud.Conditions.SetWetness(100);
        Fix64 a = Walked(dry, dry.SpawnUnit(0, "scale_guard", 5, 20, Stand), 4);
        Fix64 b = Walked(mud, mud.SpawnUnit(0, "scale_guard", 5, 20, Stand), 4);
        Assert.Equal(a, b);
        Fix64 c = Walked(dry, dry.SpawnUnit(0, "spearman", 5, 40, Stand), 4);
        Fix64 d = Walked(mud, mud.SpawnUnit(0, "spearman", 5, 40, Stand), 4);
        Assert.True(d < c);
    }

    [Fact]
    public void AReptileLyingInTheWaterIsSeenOnlyFromCloseBy()
    {
        var sim = Flat(fog: true);
        Pool(sim, 28, 28, 32, 32);
        int guard = sim.SpawnUnit(0, "scale_guard", 30, 30, Ground);
        int spear = sim.SpawnUnit(1, "spearman", 36, 30, Stand); // well within its sight
        sim.Run(Tps);
        int g = Index(sim, guard);
        Assert.True(sim.Combat.Lurking(g));
        Assert.False(sim.Vision.SeesUnit(1, g));
        Assert.Equal(0, sim.Units.Target[Index(sim, spear)]); // nobody goes after it from further off
        Assert.Equal(0, sim.Units.Target[g]); // and it lies in wait rather than going after the spearman
        // Walked up close, the spearman spots it.
        sim.Units.X[Index(sim, spear)] = Fix64.FromInt(32) + Fix64.Half;
        sim.Run(Tps);
        int gi = Index(sim, guard);
        Assert.True(sim.Vision.SeesUnit(1, gi), $"lurking {sim.Combat.Lurking(gi)} close {sim.Vision.Close(1, 30, 30)} sees {sim.Vision.Sees(1, 30, 30)} spear at {sim.Units.X[Index(sim, spear)]} {sim.Units.Z[Index(sim, spear)]} target {sim.Units.Target[Index(sim, spear)]}");
        Assert.NotEqual(0, sim.Units.Target[gi]); // and it springs at him
        // On dry land it hides from nobody.
        var land = Flat(fog: true);
        int open = land.SpawnUnit(0, "scale_guard", 30, 30, Stand);
        land.SpawnUnit(1, "spearman", 36, 30, Stand);
        land.Run(Tps);
        Assert.False(land.Combat.Lurking(Index(land, open)));
        Assert.True(land.Vision.SeesUnit(1, Index(land, open)));
    }

    /// <summary>The damage of a scale guard's first blow coming up out of a pool at a spearman, with Lurkers' strike or not.</summary>
    private static (int First, long Strikes) FirstBlowOutOfTheWater(bool lurkers)
    {
        var sim = Flat();
        Pool(sim, 28, 28, 32, 32);
        if (lurkers) sim.Economy.Grant(0, sim.Content.Tech("reptile_lurkers"));
        int guard = sim.SpawnUnit(0, "scale_guard", 32, 30, Ground);
        sim.Run(2);
        Assert.Equal(lurkers, sim.Combat.LyingInWait(Index(sim, guard)));
        int spear = sim.SpawnUnit(1, "spearman", 34, 30, Stand);
        sim.Units.Hp[Index(sim, spear)] = 1000;
        sim.Units.Morale[Index(sim, spear)] = 1000;
        sim.Schedule(sim.Tick, new AttackCommand(0, new[] { guard }, spear));
        sim.Schedule(sim.Tick, new StopCommand(1, new[] { spear }));
        for (int t = 0; t < 10 * Tps && sim.Units.Hp[Index(sim, spear)] == 1000; t++) sim.Step();
        return (1000 - sim.Units.Hp[Index(sim, spear)], sim.Combat.LurkerStrikes);
    }

    [Fact]
    public void WithLurkersStrikeTheFirstBlowOutOfTheWaterStrikesTwiceAsHard()
    {
        var (plain, none) = FirstBlowOutOfTheWater(lurkers: false);
        var (ambush, one) = FirstBlowOutOfTheWater(lurkers: true);
        Assert.True(plain > 0);
        Assert.Equal(0, none);
        Assert.Equal(1, one);
        Assert.InRange(ambush, 2 * plain - 2, 2 * plain + 2);
    }

    private static int Shaman(Simulation sim, int x, int z) => sim.SpawnUnit(0, "marsh_shaman", x, z, Stand);

    [Fact]
    public void AMarshShamanFloodsALineOfGroundIntoAShallowPoolThatDrains()
    {
        var sim = Flat();
        int shaman = Shaman(sim, 20, 30);
        int spear = sim.SpawnUnit(0, "spearman", 26, 30, Stand); // one of the shaman's own
        int mana = sim.Economy.Player(0).Mana;
        sim.Schedule(sim.Tick, new FloodCommand(0, new[] { shaman }, 26, 27, 26, 33));
        sim.Step();
        for (int z = 27; z <= 33; z++) Assert.Equal(BlockType.Water, sim.World.Get(26, Ground, z));
        Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, 26));
        Assert.Single(sim.Spells.Floods);
        Assert.Equal(7, sim.Spells.Floods[0].Blocks.Count);
        Assert.Equal(mana - SpellSystem.ManaCost(Spell.Flood), sim.Economy.Player(0).Mana);
        Assert.False(sim.Spells.CanCast(Index(sim, shaman), Spell.Flood)); // it waits, as after any spell
        // The spearman standing there sinks in and wades.
        sim.Run(Tps);
        Assert.Equal(Ground, sim.Units.Y[Index(sim, spear)]);
        Assert.True(sim.Combat.InWater(Index(sim, spear)));
        // It drains after a while, the ground as it was, and the spearman stands on it again.
        sim.Run(SpellSystem.FloodLastsTicks - 3 * Tps);
        Assert.Single(sim.Spells.Floods);
        sim.Run(3 * Tps);
        Assert.Empty(sim.Spells.Floods);
        for (int z = 27; z <= 33; z++) Assert.Equal(BlockType.Grass, sim.World.Get(26, Ground, z));
        Assert.Equal(Stand, sim.Units.Y[Index(sim, spear)]);
    }

    [Fact]
    public void FloodTakesOnlySoftGroundThatHoldsTheWater()
    {
        var sim = Flat();
        int shaman = Shaman(sim, 20, 30);
        sim.World.Set(26, Ground, 28, BlockType.Stone); // rock does not soak
        sim.World.Set(26, Ground, 30, BlockType.Road);
        sim.World.Set(27, Ground, 32, BlockType.Air); // a hole beside it: the water would run off
        var plan = new System.Collections.Generic.List<(int X, int Z, int Y, bool Room)>();
        sim.Spells.PlanFlood(26, 27, 26, 33, plan);
        Assert.Equal(7, plan.Count);
        Assert.Equal(new[] { 27, 29, 31, 33 }, plan.Where(c => c.Room).Select(c => c.Z));
        // A line with no room floods nothing and costs nothing.
        for (int z = 27; z <= 33; z++) sim.World.Set(26, Ground, z, BlockType.Stone);
        int mana = sim.Economy.Player(0).Mana;
        sim.Schedule(sim.Tick, new FloodCommand(0, new[] { shaman }, 26, 27, 26, 33));
        sim.Step();
        Assert.Empty(sim.Spells.Floods);
        Assert.Equal(mana, sim.Economy.Player(0).Mana);
        Assert.True(sim.Spells.CanCast(Index(sim, shaman), Spell.Flood));
    }

    [Fact]
    public void ShamansTogetherFloodAWiderPool()
    {
        var sim = Flat();
        int a = Shaman(sim, 20, 30), b = Shaman(sim, 20, 31), c = Shaman(sim, 20, 32);
        sim.Economy.Player(0).Mana = 500;
        sim.Schedule(sim.Tick, new FloodCommand(0, new[] { a, b, c }, 26, 25, 26, 40));
        sim.Step();
        Assert.Single(sim.Spells.Floods);
        Assert.Equal(SpellSystem.WallLength(3) * SpellSystem.WallThickness(3), sim.Spells.Floods[0].Blocks.Count);
    }

    [Fact]
    public void AFloodIsInTheCommandLogAndTheHash()
    {
        var cmd = new FloodCommand(0, new[] { 5, 7 }, 26, 27, 26, 33);
        var log = new CommandLog();
        log.Add(12, cmd);
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(cmd, back.Commands.Single().Command);

        Simulation Run(bool flood)
        {
            var sim = Flat();
            int shaman = Shaman(sim, 20, 30);
            if (flood) sim.Schedule(0, new FloodCommand(0, new[] { shaman }, 26, 27, 26, 33));
            sim.Run(Tps);
            return sim;
        }
        Assert.Equal(Run(true).StateHash(), Run(true).StateHash());
        Assert.NotEqual(Run(true).StateHash(), Run(false).StateHash());
    }
}
