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
