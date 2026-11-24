using System;
using System.Linq;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Masonry's murder holes (issue #147): a stone gate of a player with Masonry drops stones on enemies in the
/// gateway and right in front of it while a unit of its owner holds the wall beside it; an unheld gate drops nothing.
/// </summary>
public class MurderHoleTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int GateX = 32, LineZ = 30;

    private static Simulation Flat() => TestWorlds.Sim(TestWorlds.Flat(64, 16, 64, Ground));

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit at {x}, {z}");

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);

    /// <summary>A stone wall along x at <see cref="LineZ"/> with a stone gate in the middle, player 0's, with Masonry if asked.</summary>
    private static Building Gate(Simulation sim, bool masonry = true)
    {
        if (masonry) sim.Economy.Grant(0, sim.Content.Tech("masonry"));
        var gate = Place(sim, 0, "stone_gate", GateX, LineZ);
        for (int x = GateX - 8; x <= GateX + 8; x++)
            if (x != GateX) Place(sim, 0, "stone_wall", x, LineZ);
        return gate;
    }

    /// <summary>A worker of player 0 standing on top of the wall at column x: the wall is held.</summary>
    private static int Holder(Simulation sim, Building gate, int x)
    {
        int id = sim.SpawnUnit(0, "worker", x, LineZ - 3);
        int i = Index(sim, id);
        sim.Units.X[i] = Fix64.FromInt(x) + Fix64.Half;
        sim.Units.Z[i] = Fix64.FromInt(LineZ) + Fix64.Half;
        sim.Units.Y[i] = gate.Y + gate.Shape.Height;
        return id;
    }

    /// <summary>Enemy spearmen (player 1) pushing at the gate from the south.</summary>
    private static int[] Attackers(Simulation sim, Building gate, string unit = "spearman", int count = 3)
    {
        var ids = Enumerable.Range(0, count).Select(k => sim.SpawnUnit(1, unit, GateX - 1 + k, LineZ - 4)).ToArray();
        sim.Schedule(sim.Tick, new AttackBuildingCommand(1, ids, gate.Id));
        return ids;
    }

    private static int HpLeft(Simulation sim, int[] ids) =>
        ids.Sum(id => Index(sim, id) is var i and >= 0 ? sim.Units.Hp[i] : 0);

    private static int FullHp(Simulation sim, int[] ids) => ids.Sum(id => sim.MaxHpOf(Index(sim, id)));

    [Fact]
    public void MasonryGivesMurderHolesInPlaceOfRoomierHouses()
    {
        var content = ContentDb.Default;
        var masonry = content.Tech("masonry");
        Assert.DoesNotContain(masonry.Effects, e => e.Type == TechEffectType.Housing);
        Assert.Contains(masonry.Effects, e => e.Type == TechEffectType.MurderHoles);
        var holes = content.Building("stone_gate").MurderHoles;
        Assert.NotNull(holes);
        Assert.Equal(25, holes!.Damage);
        Assert.Equal(3, holes.EverySeconds);
        // Only the stone gate has them: not the lower tiers, not walls.
        Assert.All(content.Buildings.Where(b => b.Id != "stone_gate"), b => Assert.Null(b.MurderHoles));
    }

    [Fact]
    public void AHeldGateDropsStonesOnEnemiesPushingAtIt()
    {
        var sim = Flat();
        var gate = Gate(sim);
        Holder(sim, gate, GateX - 2);
        var foes = Attackers(sim, gate);
        int full = FullHp(sim, foes);
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.True(sim.Combat.Held(gate, gate.Def.MurderHoles!));
        // About a stone every three seconds once they are at the gate.
        Assert.InRange(sim.Combat.StonesDropped, 3, 7);
        Assert.True(HpLeft(sim, foes) < full, "the attackers are hurt");
        // The stones hit the head.
        Assert.Contains(foes, id => Index(sim, id) is var i && (i < 0 || sim.Units.Wounds[i * Body.Parts + (int)BodyPart.Head] > 0));
    }

    [Fact]
    public void AnUnheldGateDropsNothing()
    {
        var sim = Flat();
        var gate = Gate(sim);
        var foes = Attackers(sim, gate);
        int full = FullHp(sim, foes);
        for (int t = 0; t < 20 * Tps; t++) sim.Step();
        Assert.False(sim.Combat.Held(gate, gate.Def.MurderHoles!));
        Assert.Equal(0, sim.Combat.StonesDropped);
        Assert.Equal(full, HpLeft(sim, foes));
    }

    [Fact]
    public void AHolderTooFarAlongTheWallDoesNotHoldTheGate()
    {
        var sim = Flat();
        var gate = Gate(sim);
        Holder(sim, gate, GateX + 7);
        Attackers(sim, gate);
        for (int t = 0; t < 15 * Tps; t++) sim.Step();
        Assert.Equal(0, sim.Combat.StonesDropped);
    }

    [Fact]
    public void WithoutMasonryAStoneGateHasNoMurderHoles()
    {
        var sim = Flat();
        var gate = Gate(sim, masonry: false); // put up in a test world, though nobody could build it without
        Holder(sim, gate, GateX - 2);
        Attackers(sim, gate);
        for (int t = 0; t < 15 * Tps; t++) sim.Step();
        Assert.Equal(0, sim.Combat.StonesDropped);
    }

    [Fact]
    public void EnemiesAwayFromTheGateAreSafe()
    {
        var sim = Flat();
        var gate = Gate(sim);
        Holder(sim, gate, GateX - 2);
        // Standing at the foot of the wall well off to the side of the gate, and well out in front of it.
        int side = sim.SpawnUnit(1, "spearman", GateX + 5, LineZ - 1);
        int far = sim.SpawnUnit(1, "spearman", GateX, LineZ - 8);
        sim.Schedule(sim.Tick, new StanceCommand(1, new[] { side, far }, UnitStance.Hold));
        for (int t = 0; t < 10 * Tps; t++) sim.Step();
        Assert.Equal(0, sim.Combat.StonesDropped);
        Assert.Contains((GateX, LineZ - 1), sim.Combat.HoleColumns(gate, gate.Def.MurderHoles!));
        Assert.Contains((GateX + 1, LineZ - 3), sim.Combat.HoleColumns(gate, gate.Def.MurderHoles!));
        Assert.DoesNotContain((GateX + 5, LineZ - 1), sim.Combat.HoleColumns(gate, gate.Def.MurderHoles!));
    }

    [Fact]
    public void ARamAtAHeldGateIsHitToo()
    {
        var sim = Flat();
        var gate = Gate(sim);
        Holder(sim, gate, GateX + 2);
        var ram = Attackers(sim, gate, "battering_ram", 1);
        int full = FullHp(sim, ram);
        for (int t = 0; t < 30 * Tps; t++) sim.Step();
        int ri = Index(sim, ram[0]);
        Assert.True(sim.Combat.StonesDropped > 0, $"ram at {sim.Units.X[ri]}, {sim.Units.Y[ri]}, {sim.Units.Z[ri]}, gate y {gate.Y}");
        Assert.True(HpLeft(sim, ram) < full, "the ram is hurt");
    }

    [Fact]
    public void MurderHolesAreTheSameEveryRun()
    {
        ulong Run()
        {
            var sim = Flat();
            var gate = Gate(sim);
            Holder(sim, gate, GateX - 2);
            Attackers(sim, gate);
            for (int t = 0; t < 15 * Tps; t++) sim.Step();
            return sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
