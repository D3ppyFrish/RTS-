using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// The Fey's Glamour (see "The Fey" in docs/rules.md, <c>Units/CombatSystem.Glamour.cs</c>): an enchanter weaves
/// illusory sprites round itself, decoys that strike for nothing and fade at the first blow or after a while.
/// </summary>
public class GlamourTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 64, bool learn = true)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.SetFaction(0, "fey");
        if (learn) Learn(sim, 0);
        return sim;
    }

    private static void Learn(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("fey_glamour"));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static List<int> Illusions(Simulation sim, int player)
    {
        var u = sim.Units;
        var list = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Owner[i] == player && u.Illusion[i] > 0) list.Add(i);
        return list;
    }

    [Fact]
    public void GlamourIsTheFeysTechForTheirEnchanters()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("fey_glamour");
        Assert.Equal("fey", tech.Faction?.Id);
        Assert.Contains("basic_magic", tech.Requires.Select(t => t.Id));
        Assert.Equal(new[] { "enchanter" }, tech.Effects.Where(e => e.Type == TechEffectType.Glamour).Select(e => e.Unit!.Id));
        var rules = content.Combat.Glamour;
        Assert.Equal((3, "sprite", 2, 20 * Tps, 15, 40 * Tps), (rules.Count, rules.Unit, rules.Spread, rules.Ticks, rules.Mana, rules.WaitTicks));

        var sim = Flat(learn: false);
        int enchanter = sim.SpawnUnit(0, "enchanter", 20, 20, Stand);
        int sprite = sim.SpawnUnit(0, "sprite", 22, 20, Stand);
        Assert.False(sim.Combat.CanGlamour(Index(sim, enchanter))); // not before the tech
        Learn(sim, 0);
        Assert.True(sim.Combat.ReadyToGlamour(Index(sim, enchanter)));
        Assert.False(sim.Combat.CanGlamour(Index(sim, sprite))); // only enchanters weave
    }

    [Fact]
    public void AnEnchanterWeavesSpritesRoundItForManaThenWaits()
    {
        var sim = Flat();
        int enchanter = sim.SpawnUnit(0, "enchanter", 20, 20, Stand);
        var p = sim.Economy.Player(0);
        int mana = p.Mana;
        int population = sim.Economy.Population(0);
        sim.Schedule(0, new GlamourCommand(0, new[] { enchanter }));
        sim.Run(1);
        var ills = Illusions(sim, 0);
        Assert.Equal(3, ills.Count);
        Assert.Equal(3, sim.Combat.IllusionsWoven);
        Assert.Equal(mana - 15, p.Mana);
        var u = sim.Units;
        var sprite = sim.Content.Unit("sprite");
        int e = Index(sim, enchanter);
        foreach (int i in ills)
        {
            Assert.Equal(sprite.Index, u.Def[i]);
            Assert.Equal(sprite.MaxHp, u.Hp[i]); // its hit points full, like the real thing
            Assert.True(sim.Combat.IsIllusion(i));
            Assert.False(sim.Combat.CanGlamour(i)); // an illusion weaves nothing
            int dx = u.X[i].FloorToInt() - u.X[e].FloorToInt(), dz = u.Z[i].FloorToInt() - u.Z[e].FloorToInt();
            Assert.True(dx * dx + dz * dz is > 0 and <= 4, "within 2 columns of the enchanter, not on it");
        }
        Assert.False(sim.Combat.IsIllusion(e));
        Assert.False(sim.Combat.ReadyToGlamour(e));
        sim.Run(Tps); // population is counted on the economy's own beat
        Assert.Equal(population, sim.Economy.Population(0)); // illusions take no housing

        // A second weaving before the wait is up does nothing.
        sim.Schedule(sim.Tick, new GlamourCommand(0, new[] { enchanter }));
        sim.Run(1);
        Assert.Equal(3, sim.Combat.IllusionsWoven);

        // They fade by themselves after their time, and the enchanter is ready again after its wait.
        sim.Run(sim.Content.Combat.Glamour.Ticks);
        Assert.Empty(Illusions(sim, 0));
        Assert.Equal(0, sim.Combat.IllusionsBroken);
        sim.Run(sim.Content.Combat.Glamour.WaitTicks);
        Assert.True(sim.Combat.ReadyToGlamour(Index(sim, enchanter)));
    }

    [Fact]
    public void WithoutTheManaNothingIsWoven()
    {
        var sim = Flat();
        int enchanter = sim.SpawnUnit(0, "enchanter", 20, 20, Stand);
        int second = sim.SpawnUnit(0, "enchanter", 30, 20, Stand);
        sim.Economy.Player(0).Mana = 20; // enough for one weaving, not two
        sim.Schedule(0, new GlamourCommand(0, new[] { enchanter, second }));
        sim.Run(1);
        Assert.Equal(3, Illusions(sim, 0).Count);
        Assert.Equal(5, sim.Economy.Player(0).Mana);
        Assert.False(sim.Combat.ReadyToGlamour(Index(sim, second))); // too little mana left
        Assert.Equal(0, sim.Units.GlamourWait[Index(sim, second)]); // it did not weave
    }

    [Fact]
    public void AnIllusionStrikesForNothingAndFadesAtTheFirstBlowLeavingNoLoss()
    {
        var sim = Flat();
        sim.SetFaction(1, "settlers");
        int enchanter = sim.SpawnUnit(0, "enchanter", 20, 20, Stand);
        sim.Schedule(0, new GlamourCommand(0, new[] { enchanter }));
        sim.Run(1);
        var u = sim.Units;
        var ids = Illusions(sim, 0).Select(i => u.Id[i]).ToArray();
        // An enemy spearman a little way off, and the illusions sent at it; the enchanter steps away out of the fight.
        int spearman = sim.SpawnUnit(1, "spearman", 26, 20, Stand);
        sim.Schedule(sim.Tick, new AttackCommand(0, ids, spearman));
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { enchanter }, 4, 20));
        int full = u.Hp[Index(sim, spearman)];
        int bloodBefore = sim.Blood.Count;
        bool struck = false;
        for (int t = 0; t < 20 * Tps && Illusions(sim, 0).Count > 0; t++)
        {
            sim.Run(1);
            Assert.Equal(full, u.Hp[Index(sim, spearman)]); // never a scratch
            struck |= sim.Combat.Shots.Any(s => ids.Contains(s.Attacker));
        }
        Assert.True(struck, "the illusions went through the motions of striking");
        Assert.Empty(Illusions(sim, 0));
        Assert.Equal(3, sim.Combat.IllusionsBroken); // each ended by a blow, not by time
        Assert.Equal(0, sim.Combat.Losses(0)); // no loss to its side
        Assert.Equal(0, sim.Combat.Kills(1)); // no kill to the spearman
        Assert.Equal(0, u.Xp[Index(sim, spearman)]); // and no experience
        Assert.Equal(bloodBefore, sim.Blood.Count); // and no blood
        Assert.True(u.Exists(enchanter));
    }

    [Fact]
    public void ASplashEndsEveryIllusionItCatches()
    {
        var sim = Flat();
        sim.SetFaction(1, "fey");
        int enchanter = sim.SpawnUnit(0, "enchanter", 20, 20, Stand);
        sim.Schedule(0, new GlamourCommand(0, new[] { enchanter }));
        sim.Run(1);
        var u = sim.Units;
        sim.Schedule(sim.Tick, new MoveCommand(0, new[] { enchanter }, 4, 20)); // out of the way
        sim.Run(3 * Tps);
        // An enemy enchanter (a splash of 1.5 blocks) shoots into the bunch of them.
        var ills = Illusions(sim, 0).Select(i => u.Id[i]).ToArray();
        Assert.Equal(3, ills.Length);
        int rival = sim.SpawnUnit(1, "enchanter", 28, 20, Stand);
        sim.Schedule(sim.Tick, new AttackCommand(1, new[] { rival }, ills[0]));
        sim.Schedule(sim.Tick, new StopCommand(0, ills));
        sim.Run(5 * Tps);
        Assert.Empty(Illusions(sim, 0)); // the first burst caught them all, or the next ones did
        Assert.Equal(0, sim.Combat.Losses(0));
    }

    [Fact]
    public void AnIllusionCapturesNothing()
    {
        var sim = Flat();
        sim.SetFaction(1, "settlers");
        int enchanter = sim.SpawnUnit(0, "enchanter", 20, 20, Stand);
        var store = sim.Economy.PlaceFinished(1, sim.Content.Building("storehouse"), 30, 20)!;
        sim.Schedule(0, new GlamourCommand(0, new[] { enchanter }));
        sim.Run(1);
        var u = sim.Units;
        var ids = Illusions(sim, 0).Select(i => u.Id[i]).ToArray();
        sim.Schedule(sim.Tick, new CaptureCommand(0, ids, store.Id));
        sim.Run(1);
        foreach (int id in ids) Assert.Equal(UnitJob.None, u.Job[Index(sim, id)]);
        sim.Run(10 * Tps);
        Assert.Equal(1, store.Owner);
    }

    [Fact]
    public void GlamourCommandsGoThroughTheLog()
    {
        var log = new CommandLog();
        log.Add(12, new GlamourCommand(1, new[] { 7, 9 }));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(new GlamourCommand(1, new[] { 7, 9 }), back.Commands.Single().Command);
    }

    [Fact]
    public void ComputerPlayersWeaveIllusionsWhenARivalIsCloseAndSendThemAtIt()
    {
        foreach (var level in new[] { AiLevel.Classic, AiLevel.Normal, AiLevel.Hard })
        {
            var sim = AiTests.WithStarts(96, (30, 48));
            sim.SetFaction(0, "fey");
            Learn(sim, 0);
            var hall = sim.Economy.TownHall(0)!;
            int enchanter = sim.SpawnUnit(0, "enchanter", hall.X + 30, hall.Z);
            int enemy = sim.SpawnUnit(1, "spearman", hall.X + 38, hall.Z);
            var ai = new AiPlayer(0, 1, level);
            var log = new List<Command>();
            AiTests.PlayLogged(sim, new[] { ai }, log, () => log.OfType<GlamourCommand>().Any(), 5 * Tps);
            var glamour = log.OfType<GlamourCommand>().FirstOrDefault();
            Assert.True(glamour != null, $"{level}: a weaving");
            Assert.Contains(enchanter, glamour!.Units);
            var u = sim.Units;
            // Each illusion goes at the rival: on its own, seeing it, or sent on by the computer.
            bool Engaged() => Illusions(sim, 0) is { Count: > 0 } ills && ills.All(i => u.Target[i] != 0
                || log.OfType<AttackMoveCommand>().Any(m => m.Units.Contains(u.Id[i])));
            Assert.True(AiTests.PlayLogged(sim, new[] { ai }, log, Engaged, 5 * Tps), $"{level}: the illusions go at the rival");
            foreach (int id in ai.Wave) Assert.False(u.Exists(id) && u.Illusion[u.IndexOf(id)] > 0, $"{level}: no illusion in the army");
            _ = enemy;
        }
    }
}
