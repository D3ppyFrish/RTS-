using System;
using Rts.Sim.Units;

namespace Rts.Sim.Content;

/// <summary>
/// The design doc's role table, loaded from <c>data/roles.json</c>: which roles each role is strong
/// against, and so which attacks deal extra damage. A role is strong against another if its own row says
/// so, or if the other's row lists it as a weakness. The same file says how fast units out of the fight
/// heal on their own (see <see cref="CombatSystem"/>).
/// </summary>
public sealed class CombatRules
{
    /// <summary>Number of <see cref="UnitRole"/> values; the table has a row and a column for each.</summary>
    public static readonly int RoleCount = Enum.GetValues<UnitRole>().Length;

    private readonly bool[] _strong = new bool[RoleCount * RoleCount];
    private readonly bool[] _structures = new bool[RoleCount];
    private readonly string[] _jobs = new string[RoleCount];

    internal CombatRules()
    {
        Array.Fill(_jobs, "");
    }

    /// <summary>Damage multiplier for an attack on a role it is strong against.</summary>
    public Fix64 Strong { get; internal set; } = Fix64.FromInt(2);

    /// <summary>Damage multiplier for attacks on buildings by roles not strong against structures.</summary>
    public Fix64 Structures { get; internal set; } = Fix64.FromRatio(1, 4);

    /// <summary>
    /// What a close-up blow on a thick wall, gate or tower (see <see cref="Economy.BuildingDef.Thick"/>) is
    /// multiplied by on top of <see cref="Structures"/>, for every role not strong against structures: hacking at
    /// solid stone or timber gets little done.
    /// </summary>
    public Fix64 Thick { get; internal set; } = Fix64.One;

    /// <summary>Ticks a unit must go without hitting anything or being hit before it heals on its own.</summary>
    public int RecoveryDelayTicks { get; internal set; } = 10 * Simulation.TicksPerSecond;

    /// <summary>Percentage of its most hit points a unit out of the fight gets back each second.</summary>
    public Fix64 RecoveryPercent { get; internal set; } = Fix64.FromRatio(1, 4);

    /// <summary>The same for a unit at home: near a town hall or house of its own (see <see cref="HomeRange"/>).</summary>
    public Fix64 HomeRecoveryPercent { get; internal set; } = Fix64.FromInt(2);

    /// <summary>A unit at most this many columns from a town hall or house of its own is at home.</summary>
    public int HomeRange { get; internal set; } = 12;

    /// <summary>
    /// A slash or pierce wound bleeds away this percentage of the hit's damage again, over time, unless it is
    /// stemmed first (see <see cref="CombatSystem"/>).
    /// </summary>
    public int BleedShare { get; internal set; } = 40;

    /// <summary>Hit points each bleeding wound loses a second (a deep wound more, see <see cref="CombatSystem.BleedLoss"/>).</summary>
    public int BleedRate { get; internal set; } = 1;

    /// <summary>Whether bleeding can kill; if not, it stops at 1 hit point.</summary>
    public bool BleedKills { get; internal set; } = true;

    /// <summary>In-game days (see <see cref="Conditions.DayTicks"/>) a full stain of blood takes to fade on a clear day.</summary>
    public Fix64 BloodDays { get; internal set; } = Fix64.One;

    /// <summary>How a shield wall stands up to shots and charges (see <see cref="ShieldWallRules"/>).</summary>
    public ShieldWallRules Wall { get; internal set; } = new();

    /// <summary>How molting heals a hive unit and leaves it soft (see <see cref="MoltRules"/>).</summary>
    public MoltRules Molt { get; internal set; } = new();

    /// <summary>How Clan Kage's smoke bombs hang about (see <see cref="SmokeRules"/>).</summary>
    public SmokeRules Smoke { get; internal set; } = new();

    /// <summary>How far and with how many Clan Kage's shadow casters step through the shadows (see <see cref="ShadowStepRules"/>).</summary>
    public ShadowStepRules ShadowStep { get; internal set; } = new();

    /// <summary>How Clan Kage's saboteurs wreck enemy buildings (see <see cref="SabotageRules"/>).</summary>
    public SabotageRules Sabotage { get; internal set; } = new();

    /// <summary>How Clan Kage's climbers scale walls and cliffs (see <see cref="ClimbRules"/>).</summary>
    public ClimbRules Climb { get; internal set; } = new();

    public bool StrongAgainst(UnitRole attacker, UnitRole target) => _strong[(int)attacker * RoleCount + (int)target];

    public bool StrongAgainstStructures(UnitRole role) => _structures[(int)role];

    /// <summary>The role's job, from the table.</summary>
    public string Job(UnitRole role) => _jobs[(int)role];

    /// <summary>What an attack by one role on another is multiplied by.</summary>
    public Fix64 Multiplier(UnitRole attacker, UnitRole target) => StrongAgainst(attacker, target) ? Strong : Fix64.One;

    /// <summary>What an attack by this role on a building is multiplied by.</summary>
    public Fix64 StructureMultiplier(UnitRole attacker) => StrongAgainstStructures(attacker) ? Fix64.One : Structures;

    internal void SetStrong(UnitRole attacker, UnitRole target) => _strong[(int)attacker * RoleCount + (int)target] = true;

    internal void SetStrongAgainstStructures(UnitRole role) => _structures[(int)role] = true;

    internal void SetJob(UnitRole role, string job) => _jobs[(int)role] = job;
}

/// <summary>
/// The numbers of the shield wall (<c>shieldWall</c> in <c>data/roles.json</c>; see <see cref="CombatSystem"/>):
/// first guesses, for playtesting.
/// </summary>
public sealed class ShieldWallRules
{
    /// <summary>Percent of its damage a shot from the wall's front (or from a side another wall unit covers) deals.</summary>
    public int Front { get; internal set; } = 25;
    /// <summary>Percent of its damage a rider's blow on the wall's front deals.</summary>
    public int Charge { get; internal set; } = 25;
    /// <summary>Percent of that blow's full damage the rider takes itself, thrown back onto the spears.</summary>
    public int Repel { get; internal set; } = 100;
    /// <summary>Ticks a rider thrown back by the wall stands stopped dead, before it can walk or strike again.</summary>
    public int StaggerTicks { get; internal set; } = 2 * Simulation.TicksPerSecond;
    /// <summary>Ticks a unit takes to lower its shield wall before it can walk.</summary>
    public int DropTicks { get; internal set; } = Simulation.TicksPerSecond;
    /// <summary>A wall unit this close (blocks) beside another covers that side of it.</summary>
    public Fix64 Cover { get; internal set; } = Fix64.FromRatio(3, 2);
}

/// <summary>
/// The numbers of molting (<c>molt</c> in <c>data/roles.json</c>; see <see cref="CombatSystem"/>): first guesses, for
/// playtesting.
/// </summary>
/// <summary>
/// Smoke bombs (a tech's <see cref="TechEffectType.SmokeBombs"/> effect, Clan Kage's): a cloud thrown at the thrower's
/// feet hides everyone in it and keeps them out of the fight (see <c>Units/CombatSystem.Smoke.cs</c>).
/// </summary>
public sealed class SmokeRules
{
    /// <summary>How far (columns) the cloud spreads round where it was thrown.</summary>
    public int Radius { get; internal set; } = 3;
    /// <summary>How many levels up or down from where it was thrown the cloud reaches.</summary>
    public int Height { get; internal set; } = 2;
    /// <summary>Ticks the cloud hangs about.</summary>
    public int Ticks { get; internal set; } = 8 * Simulation.TicksPerSecond;
    /// <summary>Ticks a thrower waits, from the throw, before it can throw the next.</summary>
    public int WaitTicks { get; internal set; } = 45 * Simulation.TicksPerSecond;
}

/// <summary>
/// Shadow step (a tech's <see cref="TechEffectType.ShadowStep"/> effect, Clan Kage's shadow casters): a caster takes the
/// units of its side round it through the shadows to a spot its side has seen (see <c>Units/CombatSystem.ShadowStep.cs</c>).
/// </summary>
public sealed class ShadowStepRules
{
    /// <summary>Most columns from the caster to where it steps.</summary>
    public int Range { get; internal set; } = 80;
    /// <summary>Columns round the caster within which its side's units step with it.</summary>
    public int Gather { get; internal set; } = 5;
    /// <summary>Most units that step at once, the caster too.</summary>
    public int Carry { get; internal set; } = 8;
    /// <summary>Columns round the spot within which they come out of the shadows.</summary>
    public int Spread { get; internal set; } = 3;
    /// <summary>Mana a step costs.</summary>
    public int Mana { get; internal set; } = 30;
    /// <summary>Ticks a caster waits, from the step, before it can step again.</summary>
    public int WaitTicks { get; internal set; } = 90 * Simulation.TicksPerSecond;
}

/// <summary>
/// Sabotage (a tech's <see cref="TechEffectType.Sabotage"/> effect, Clan Kage's Saboteurs): a saboteur at an enemy
/// building wrecks its works, and it does no work for a while (see <c>Economy/EconomySystem.Sabotage.cs</c>).
/// </summary>
public sealed class SabotageRules
{
    /// <summary>Ticks of work at the building a sabotage takes.</summary>
    public int Ticks { get; internal set; } = 6 * Simulation.TicksPerSecond;
    /// <summary>Columns round the building within which a soldier of its owner's holds the saboteurs off.</summary>
    public int Guard { get; internal set; } = 3;
    /// <summary>Ticks a sabotaged building does no work.</summary>
    public int StallTicks { get; internal set; } = 45 * Simulation.TicksPerSecond;
    /// <summary>Percent of each good inside it that is spoiled.</summary>
    public int Spoil { get; internal set; } = 25;
    /// <summary>Ticks a saboteur waits, from the sabotage, before the next.</summary>
    public int WaitTicks { get; internal set; } = 60 * Simulation.TicksPerSecond;
}

/// <summary>
/// Wall climbing (a tech's <see cref="TechEffectType.WallClimbing"/> effect, Clan Kage's Wall climbing): a climber at the
/// foot of a wall, gate, tower or cliff climbs its face onto the top, or down it, with no ladder (see
/// <c>Economy/EconomySystem.Climb.cs</c>).
/// </summary>
public sealed class ClimbRules
{
    /// <summary>Ticks a climber takes for each level it climbs.</summary>
    public int TicksPerLevel { get; internal set; } = Simulation.TicksPerSecond;
    /// <summary>Most levels a climb goes up or down (at least 2: a single level is a step).</summary>
    public int Height { get; internal set; } = 10;
    /// <summary>Columns round the spot clicked within which a climber looks for a face to climb.</summary>
    public int Reach { get; internal set; } = 2;
}

public sealed class MoltRules
{
    /// <summary>A unit can molt only with at most this percent of its most hit points left.</summary>
    public int Below { get; internal set; } = 25;
    /// <summary>Ticks it takes to shed the shell, standing still and healing back to full.</summary>
    public int Ticks { get; internal set; } = 15 * Simulation.TicksPerSecond;
    /// <summary>Ticks it stays soft after: no armour, slower, hit harder.</summary>
    public int SoftTicks { get; internal set; } = 30 * Simulation.TicksPerSecond;
    /// <summary>Percent of its pace it walks at while soft.</summary>
    public int Pace { get; internal set; } = 60;
    /// <summary>Percent of a hit's damage (after armour, which it has none of) a molting or soft unit takes.</summary>
    public int Tender { get; internal set; } = 150;
}
