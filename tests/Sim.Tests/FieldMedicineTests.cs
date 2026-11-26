using System;
using System.Linq;
using Rts.Sim.AI;
using Rts.Sim.Content;
using Rts.Sim.Economy;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Field medicine, a tech branch for every faction: Field dressings (units bind their own wounds a few seconds out of
/// the fight, grievous ones too) and Field hospital (a hut by the front where units heal as at home).
/// </summary>
public class FieldMedicineTests
{
    private const int Ground = 3; // flat test worlds: grass at 3, units stand at 4
    private const int Tps = Simulation.TicksPerSecond;
    private const int Settlers = 0, Orcs = 1;

    private static Simulation Flat() => new(new RegionMap(1, TestWorlds.Flat(96, 16, 96, Ground)));

    private static int Index(Simulation sim, int id) => sim.Units.IndexOf(id);
    private static int Hp(Simulation sim, int id) => sim.Units.Exists(id) ? sim.Units.Hp[Index(sim, id)] : 0;

    private static TechDef Tech(string id) => ContentDb.Default.Techs.First(t => t.Id == id);

    private static Building Place(Simulation sim, int owner, string def, int x, int z) =>
        sim.Economy.PlaceFinished(owner, sim.Content.Building(def), x, z) ?? throw new InvalidOperationException($"{def} does not fit");

    private static void Cut(Simulation sim, int id, BodyPart part, int wound, int bleed)
    {
        var u = sim.Units;
        int i = Index(sim, id), at = i * Body.Parts + (int)part;
        u.Wounds[at] = (byte)wound;
        u.BleedLeft[at] = (byte)bleed;
        u.Bleeding[i] |= (byte)(1 << (int)part);
    }

    private static int Healed(int max, Fix64 percent, int seconds) => (Fix64.FromInt(max) * percent * seconds / 100).FloorToInt();

    [Fact]
    public void TheBranchIsInTheDataForEveryFaction()
    {
        var dressings = Tech("field_dressings");
        Assert.Null(dressings.Faction);
        Assert.Equal(Discovery.None, dressings.Discovery);
        Assert.Contains(dressings.Requires, r => r.Id == "farming");
        var bind = Assert.Single(dressings.Effects);
        Assert.Equal(TechEffectType.BindWounds, bind.Type);
        Assert.Equal(3 * Tps, bind.Ticks);

        var hospitalTech = Tech("field_hospital");
        Assert.Null(hospitalTech.Faction);
        Assert.Contains(hospitalTech.Requires, r => r.Id == "field_dressings");
        var hospital = ContentDb.Default.Building("field_hospital");
        Assert.Equal(BuildingKind.Infirmary, hospital.Kind);
        Assert.Equal(0, hospital.Housing);
        Assert.False(hospital.Storage);

        // The hospital waits for its tech.
        var sim = Flat();
        Assert.False(sim.Economy.Unlocked(Settlers, hospital));
        sim.Economy.Grant(Settlers, dressings);
        Assert.False(sim.Economy.Unlocked(Settlers, hospital));
        sim.Economy.Grant(Settlers, hospitalTech);
        Assert.True(sim.Economy.Unlocked(Settlers, hospital));
        Assert.False(sim.Economy.Unlocked(Orcs, hospital));
    }

    /// <summary>
    /// With Field dressings a unit out of the fight for 3 seconds binds its own wounds, a grievous one too, far from
    /// home and with no healer; a rival's unit without it bleeds on, and its grievous wound kills it.
    /// </summary>
    [Fact]
    public void FieldDressingsBindWoundsAwayFromHome()
    {
        var sim = Flat();
        sim.Economy.Grant(Settlers, Tech("field_dressings"));
        Assert.Equal(3 * Tps, sim.Economy.BindTicks(Settlers));
        Assert.Equal(0, sim.Economy.BindTicks(Orcs));

        int ours = sim.SpawnUnit(Settlers, "spearman", 20, 30);
        int theirs = sim.SpawnUnit(Orcs, "spearman", 70, 30);
        foreach (int id in new[] { ours, theirs })
        {
            sim.Units.Hp[Index(sim, id)] = 60;
            Cut(sim, id, BodyPart.Torso, 20, 40);
            sim.Combat.OpenGrievousWound(Index(sim, id), BodyPart.Arms);
            sim.Units.LastFight[Index(sim, id)] = sim.Tick; // just out of the fight
        }

        // In the fight it bleeds like anyone: keep it fighting for 5 seconds.
        for (int t = 0; t < 5 * Tps; t++)
        {
            sim.Units.LastFight[Index(sim, ours)] = sim.Tick;
            sim.Step();
        }
        Assert.True(Hp(sim, ours) < 60);
        Assert.NotEqual(0, sim.Units.Bleeding[Index(sim, ours)]);

        // Out of it, it binds its wounds within 3 seconds (and a second's step).
        int was = Hp(sim, ours);
        sim.Run(4 * Tps);
        int i = Index(sim, ours);
        Assert.Equal(0, sim.Units.Bleeding[i]);
        Assert.Equal(0, sim.Units.Grievous[i]);
        Assert.True(Hp(sim, ours) >= was - 3 * (CombatSystem.GrievousRate + 3));
        int bound = Hp(sim, ours);
        sim.Run(5 * Tps); // and loses no more
        Assert.True(Hp(sim, ours) >= bound);

        // The rival with no dressings bleeds to death from the grievous wound.
        sim.Run(30 * Tps);
        Assert.False(sim.Units.Exists(theirs));
        Assert.True(sim.Units.Exists(ours));
    }

    /// <summary>
    /// A field hospital heals its owner's units within the home range as at home, and binds their wounds; a rival's
    /// units by it, and its owner's beyond the range, heal only in the field.
    /// </summary>
    [Fact]
    public void AFieldHospitalHealsAsAtHome()
    {
        var sim = Flat();
        var rules = sim.Content.Combat;
        int max = sim.Content.Unit("spearman").MaxHp;
        var hospital = Place(sim, Settlers, "field_hospital", 40, 40);
        int near = sim.SpawnUnit(Settlers, "spearman", hospital.MaxX + rules.HomeRange, 41);
        int far = sim.SpawnUnit(Settlers, "spearman", hospital.MaxX + rules.HomeRange + 2, 70);
        foreach (int id in new[] { near, far }) sim.Units.Hp[Index(sim, id)] = max / 2;
        Cut(sim, near, BodyPart.Legs, 10, 60);
        Cut(sim, far, BodyPart.Legs, 10, 60);

        Assert.False(sim.Combat.Healing(Index(sim, near), out _)); // not out of the fight long enough yet
        sim.Run(rules.RecoveryDelayTicks + Tps);
        Assert.Equal(0, sim.Units.Bleeding[Index(sim, near)]); // bound at the hospital
        Assert.NotEqual(0, sim.Units.Bleeding[Index(sim, far)]); // still bleeding out there
        Assert.True(sim.Combat.Healing(Index(sim, near), out bool atHome) && atHome);
        Assert.True(sim.Combat.Healing(Index(sim, far), out bool farHome) && !farHome);
        int before = Hp(sim, near);
        sim.Run(20 * Tps);
        Assert.True(Hp(sim, near) - before >= Healed(max, rules.HomeRecoveryPercent, 20) - 1);

        // A rival's unit beside it heals only as in the field.
        var other = Flat();
        Place(other, Settlers, "field_hospital", 40, 40);
        int rival = other.SpawnUnit(Orcs, "spearman", 46, 42);
        other.Units.Hp[Index(other, rival)] = max / 2;
        other.Run(rules.RecoveryDelayTicks + Tps);
        Assert.True(other.Combat.Healing(Index(other, rival), out bool rivalHome) && !rivalHome);
    }

    /// <summary>The computer players research the branch (late in their orders), and the smart ones put up a field hospital.</summary>
    [Fact]
    public void ComputerPlayersKnowTheBranch()
    {
        Assert.Contains("field_dressings", AiPlayer.TechOrder);
        Assert.Contains("field_hospital", AiPlayer.TechOrder);
        Assert.Contains("field_dressings", AiPlayer.HardTechOrder);
        Assert.Contains("field_hospital", AiPlayer.HardTechOrder);
        Assert.True(Array.IndexOf(AiPlayer.HardTechOrder, "field_dressings") < Array.IndexOf(AiPlayer.HardTechOrder, "field_hospital"));
    }
}
