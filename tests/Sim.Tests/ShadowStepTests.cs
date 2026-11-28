using System;
using System.Collections.Generic;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Replay;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Clan Kage's shadow step (see "Clan Kage" in docs/rules.md, <c>Units/CombatSystem.ShadowStep.cs</c>): a shadow caster
/// takes the units round it through the shadows to a spot its side has seen, out of the daylight.
/// </summary>
public class ShadowStepTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Stand = Ground + 1;
    private const int Tps = Simulation.TicksPerSecond;

    private static Simulation Flat(int size = 128, bool fog = false, bool learn = true, int? dayStart = null)
    {
        var sim = TestWorlds.Sim(TestWorlds.Flat(size, 16, size, Ground));
        sim.Fog = fog;
        if (dayStart is { } start)
        {
            sim.DayNight = true;
            sim.DayStart = start;
        }
        sim.SetFaction(0, "kage");
        if (learn) Learn(sim, 0);
        return sim;
    }

    private static void Learn(Simulation sim, int player) => sim.Economy.Grant(player, sim.Content.Tech("kage_shadow_step"));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    private static (int X, int Z) At(Simulation sim, int id)
    {
        int i = Index(sim, id);
        return (sim.Units.X[i].FloorToInt(), sim.Units.Z[i].FloorToInt());
    }

    private static bool Near(Simulation sim, int id, int x, int z, int r)
    {
        var (ux, uz) = At(sim, id);
        return Math.Abs(ux - x) <= r && Math.Abs(uz - z) <= r;
    }

    [Fact]
    public void ShadowStepIsClanKagesTechForTheirShadowCasters()
    {
        var content = ContentDb.Default;
        var tech = content.Tech("kage_shadow_step");
        Assert.Equal("kage", tech.Faction?.Id);
        Assert.Equal(new[] { "shadow_caster" }, tech.Effects.Where(e => e.Type == TechEffectType.ShadowStep).Select(e => e.Unit!.Id));
        var rules = content.Combat.ShadowStep;
        Assert.Equal((80, 5, 8, 3, 30, 90 * Tps), (rules.Range, rules.Gather, rules.Carry, rules.Spread, rules.Mana, rules.WaitTicks));

        var sim = Flat(learn: false);
        int caster = sim.SpawnUnit(0, "shadow_caster", 20, 20, Stand);
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 22, 20, Stand);
        Assert.False(sim.Combat.CanStep(Index(sim, caster))); // not before the tech
        Learn(sim, 0);
        Assert.True(sim.Combat.ReadyToStep(Index(sim, caster)));
        Assert.False(sim.Combat.CanStep(Index(sim, kunoichi))); // only casters walk the shadows
    }

    [Fact]
    public void TheCasterTakesTheUnitsRoundItThroughTheShadows()
    {
        var sim = Flat();
        int caster = sim.SpawnUnit(0, "shadow_caster", 20, 20, Stand);
        int near = sim.SpawnUnit(0, "kunoichi", 23, 21, Stand); // within 5 columns
        int far = sim.SpawnUnit(0, "kunoichi", 30, 20, Stand); // too far off to go
        int other = sim.SpawnUnit(0, "kunoichi", 21, 20, Stand); // close, but not in the command
        sim.Schedule(0, new MoveCommand(0, new[] { near }, 10, 40, Stand)); // walking off, but still close when it goes
        int mana = sim.Economy.Player(0).Mana;
        sim.Schedule(1, new ShadowStepCommand(0, new[] { near, far, caster }, 80, 60));
        sim.Run(2);

        Assert.Equal(1, sim.Combat.StepsMade);
        Assert.Equal(2, sim.Combat.UnitsStepped);
        Assert.True(Near(sim, caster, 80, 60, 3));
        Assert.True(Near(sim, near, 80, 60, 3));
        Assert.Equal(UnitOrder.Idle, sim.Units.Order[Index(sim, near)]); // it comes out with no orders
        Assert.NotEqual(At(sim, caster), At(sim, near)); // each on a spot of its own
        Assert.True(Near(sim, far, 30, 20, 1));
        Assert.True(Near(sim, other, 21, 20, 1));
        Assert.Equal(mana - sim.Content.Combat.ShadowStep.Mana, sim.Economy.Player(0).Mana);
        Assert.Single(sim.Combat.ShadowSteps);
        Assert.Equal((20, 80), (sim.Combat.ShadowSteps[0].FromX, sim.Combat.ShadowSteps[0].ToX));

        // The caster waits before the next step.
        Assert.False(sim.Combat.ReadyToStep(Index(sim, caster)));
        sim.Schedule(sim.Tick, new ShadowStepCommand(0, new[] { caster }, 40, 40));
        sim.Run(1);
        Assert.Equal(1, sim.Combat.StepsMade);
        sim.Run(sim.Content.Combat.ShadowStep.WaitTicks);
        Assert.True(sim.Combat.ReadyToStep(Index(sim, caster)));
        Assert.Empty(sim.Combat.ShadowSteps); // the marks have faded
    }

    [Fact]
    public void AStepCarriesAtMostItsShareOfUnitsNearestFirst()
    {
        var sim = Flat();
        int caster = sim.SpawnUnit(0, "shadow_caster", 30, 30, Stand);
        var ids = new List<int> { caster };
        for (int k = 0; k < 10; k++) ids.Add(sim.SpawnUnit(0, "kunoichi", 26 + k, 32, Stand));
        sim.Schedule(0, new ShadowStepCommand(0, ids.ToArray(), 85, 75));
        sim.Run(1);
        int carry = sim.Content.Combat.ShadowStep.Carry;
        Assert.Equal(carry, sim.Combat.UnitsStepped);
        Assert.Equal(carry, ids.Count(id => Near(sim, id, 85, 75, 3)));
        // The ones furthest from the caster stayed behind.
        Assert.True(Near(sim, ids[1], 26, 32, 0) || Near(sim, ids[10], 35, 32, 0));
    }

    [Fact]
    public void NoStepWithoutTheTechTheManaOrACaster()
    {
        var sim = Flat(learn: false);
        int caster = sim.SpawnUnit(0, "shadow_caster", 20, 20, Stand);
        sim.Schedule(0, new ShadowStepCommand(0, new[] { caster }, 60, 60));
        sim.Run(1);
        Assert.Equal(0, sim.Combat.StepsMade);

        Learn(sim, 0);
        sim.Economy.Player(0).Mana = sim.Content.Combat.ShadowStep.Mana - 1;
        Assert.Equal(StepResult.NoMana, sim.Combat.CanShadowStep(Index(sim, caster), 60, 60));
        sim.Schedule(sim.Tick, new ShadowStepCommand(0, new[] { caster }, 60, 60));
        sim.Run(1);
        Assert.Equal(0, sim.Combat.StepsMade);

        sim.Economy.Player(0).Mana = 100;
        int kunoichi = sim.SpawnUnit(0, "kunoichi", 22, 20, Stand);
        sim.Schedule(sim.Tick, new ShadowStepCommand(0, new[] { kunoichi }, 60, 60)); // no caster among them
        sim.Run(1);
        Assert.Equal(0, sim.Combat.StepsMade);
        Assert.Equal(StepResult.Ok, sim.Combat.CanShadowStep(Index(sim, caster), 60, 60));
    }

    [Fact]
    public void TheSpotMustBeInRangeAndHaveGroundToStandOn()
    {
        var sim = Flat(size: 160);
        int caster = sim.SpawnUnit(0, "shadow_caster", 10, 10, Stand);
        int c = Index(sim, caster);
        Assert.Equal(StepResult.TooFar, sim.Combat.CanShadowStep(c, 10 + 81, 10));
        Assert.Equal(StepResult.TooFar, sim.Combat.CanShadowStep(c, -1, 10));
        Assert.Equal(StepResult.Ok, sim.Combat.CanShadowStep(c, 10 + 80, 10));

        sim.World.Set(40, Stand, 40, BlockType.Water);
        sim.World.Set(40, Ground, 40, BlockType.Water);
        Assert.Equal(StepResult.NoGround, sim.Combat.CanShadowStep(c, 40, 40));

        var hall = sim.Economy.PlaceFinished(0, sim.Content.Building("house"), 50, 50)!;
        Assert.Equal(StepResult.NoGround, sim.Combat.CanShadowStep(c, hall.X, hall.Z));
    }

    [Fact]
    public void BothEndsMustLieInShadow()
    {
        // At noon on open ground there is no shadow to step into.
        var noon = Flat(dayStart: 12 * 60);
        int caster = noon.SpawnUnit(0, "shadow_caster", 20, 20, Stand);
        Assert.Equal(StepResult.InTheLight, noon.Combat.CanShadowStep(Index(noon, caster), 60, 60));
        // Under a roof the caster stands in shadow, but the spot out on the open ground is still lit.
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
            noon.World.Set(20 + dx, Stand + 3, 20 + dz, BlockType.Stone);
        Assert.Equal(StepResult.LitThere, noon.Combat.CanShadowStep(Index(noon, caster), 60, 60));
        noon.Schedule(0, new ShadowStepCommand(0, new[] { caster }, 60, 60));
        noon.Run(1);
        Assert.Equal(0, noon.Combat.StepsMade);

        // By night anywhere will do.
        var night = Flat(dayStart: 23 * 60);
        int nightCaster = night.SpawnUnit(0, "shadow_caster", 20, 20, Stand);
        Assert.Equal(StepResult.Ok, night.Combat.CanShadowStep(Index(night, nightCaster), 60, 60));
        night.Schedule(0, new ShadowStepCommand(0, new[] { nightCaster }, 60, 60));
        night.Run(1);
        Assert.True(Near(night, nightCaster, 60, 60, 3));
    }

    [Fact]
    public void OnlySpotsItsSideHasSeen()
    {
        var sim = Flat(fog: true);
        int caster = sim.SpawnUnit(0, "shadow_caster", 40, 40, Stand);
        int scout = sim.SpawnUnit(0, "shinobi", 90, 90, Stand);
        sim.Run(Tps);
        int c = Index(sim, caster);
        Assert.Equal(StepResult.Unseen, sim.Combat.CanShadowStep(c, 40, 100));
        Assert.Equal(StepResult.Ok, sim.Combat.CanShadowStep(c, 88, 88)); // the shinobi sees it
        _ = scout;
    }

    [Fact]
    public void ShadowStepIsLoggedAndReplays()
    {
        var log = new CommandLog();
        log.Add(12, new ShadowStepCommand(1, new[] { 7, 9 }, 40, 55));
        var back = CommandLog.Parse(log.ToString());
        Assert.Equal(new ShadowStepCommand(1, new[] { 7, 9 }, 40, 55), back.Commands.Single().Command);
    }

    [Theory]
    [InlineData(AiLevel.Classic)]
    [InlineData(AiLevel.Normal)]
    public void AComputerWaveStepsThroughTheShadowsTowardsAFarTarget(AiLevel level)
    {
        const int Kage = 1;
        var sim = AiTests.WithStarts(160, (24, 80), (136, 80));
        sim.SetFaction(Kage, "kage");
        Learn(sim, Kage);
        for (int k = 0; k < 8; k++) sim.SpawnUnit(Kage, "kunoichi", 128 + k, 72);
        int caster = sim.SpawnUnit(Kage, "shadow_caster", 130, 74);
        var ai = new AiPlayer(Kage, 7, level);
        var log = new List<Command>();
        bool Stepped() => log.OfType<ShadowStepCommand>().Any() && sim.Combat.StepsMade > 0;
        Assert.True(Play(sim, ai, () => ai.Waves > 0, AiPlayer.SmartFirstWaveTicks + 4 * 60 * Tps, log), $"{level}: a wave to set out");
        Assert.True(Play(sim, ai, Stepped, 30 * Tps, log), $"{level}: the wave to step through the shadows");
        var step = log.OfType<ShadowStepCommand>().First();
        Assert.Contains(caster, step.Units);
        var hall = sim.Economy.TownHall(0)!;
        long d2 = (long)(step.X - hall.X) * (step.X - hall.X) + (long)(step.Z - hall.Z) * (step.Z - hall.Z);
        Assert.True(d2 < 60L * 60, $"{level}: stepped to {step.X},{step.Z}, towards the rival's hall at {hall.X},{hall.Z}");
        Assert.True(d2 >= (long)(AiPlayer.StepShort - 2) * (AiPlayer.StepShort - 2), $"{level}: not right on top of it");
    }

    private static bool Play(Simulation sim, AiPlayer ai, Func<bool> done, int maxTicks, List<Command> log)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            if (done()) return true;
            ai.Think(sim, c =>
            {
                log.Add(c);
                sim.Schedule(sim.Tick, c);
            });
            sim.Step();
        }
        return done();
    }
}
