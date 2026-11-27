using Rts.Sim.Content;

namespace Rts.Sim.Units;

/// <summary>
/// The Dwarves' Book of Grudges (a <see cref="TechEffectType.Grudges"/> tech; see "The Dwarves" in docs/rules.md): an
/// enemy unit that kills one of a grudge-keeping player's units is marked with that player's grudge for the rest of its
/// life (<see cref="UnitStore.Grudged"/>). The player's units deal it <see cref="Economy.EconomySystem.GrudgePercent"/>
/// more, and the player sees it through the fog of war (<see cref="VisionSystem.SeesUnit"/>).
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Grudges sworn (a unit marked by a player that did not bear it one yet) since the match began.</summary>
    public long GrudgesSworn { get; private set; }

    /// <summary>Blows that dealt more for a grudge since the match began.</summary>
    public long GrudgeBlows { get; private set; }

    /// <summary>
    /// Unit <paramref name="dead"/> was killed by unit <paramref name="killer"/>: if the dead one's owner keeps grudges
    /// and the killer is another player's (a wild monster too), the killer bears that owner's grudge from now on.
    /// </summary>
    internal void BearGrudge(int dead, int killer)
    {
        var u = U;
        int owner = u.Owner[dead];
        if ((uint)owner >= Simulation.WildPlayer || u.Owner[killer] == owner || _sim.Economy.GrudgePercent(owner) == 0) return;
        byte bit = (byte)(1 << owner);
        if ((u.Grudged[killer] & bit) != 0) return;
        u.Grudged[killer] |= bit;
        GrudgesSworn++;
    }

    /// <summary>True if <paramref name="player"/> bears unit i a grudge.</summary>
    public static bool Bears(UnitStore u, int player, int i) =>
        (uint)player < Simulation.WildPlayer && (uint)i < (uint)u.Count && (u.Grudged[i] & (1 << player)) != 0;

    /// <summary>True if <paramref name="player"/> bears unit i a grudge (see <see cref="BearGrudge"/>).</summary>
    public bool Bears(int player, int i) => Bears(U, player, i);

    /// <summary>
    /// How much more (percent) unit i's blow on unit t deals for a grudge: its owner's <see cref="TechEffectType.Grudges"/>
    /// percent if that owner bears t a grudge, else 0.
    /// </summary>
    private int GrudgeAgainst(int i, int t)
    {
        var u = U;
        int owner = u.Owner[i];
        if (u.Grudged[t] == 0 || !Bears(u, owner, t)) return 0;
        int percent = _sim.Economy.GrudgePercent(owner);
        if (percent > 0) GrudgeBlows++;
        return percent;
    }
}
