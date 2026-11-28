using System;
using System.Collections.Generic;
using Rts.Sim.Content;

namespace Rts.Sim.Units;

/// <summary>
/// The Reptilians, the swamp brood (see "The Reptilians" in docs/rules.md). Their units are <b>amphibious</b>
/// (<see cref="UnitDef.Amphibious"/>): one lying in the water with nothing to fight is hidden, as a bog horror is, and
/// seen by its enemies only from close by (<see cref="Lurking"/>); with Lurkers' strike (a
/// <see cref="TechEffectType.Ambush"/> tech) its first blow coming up out of the water at someone deals more
/// (<see cref="SpringLurker"/>). They are <b>cold-blooded</b> (<see cref="UnitDef.ColdBlooded"/>): in the cold, at night
/// or up in the hills (<see cref="Conditions.Cold"/>), they walk slower, strike softer and do not regenerate
/// (<see cref="Chilled"/>). Their regeneration doubles with their feet in water (<see cref="SwampRegen"/>).
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Percent of its damage a chilled unit deals (and of its pace it walks at, see <see cref="UnitMovement"/>).</summary>
    public const int ColdPercent = 75;

    // Units (ids, sorted) of players with an ambush tech that lie in wait in the water: their next blow is an ambush.
    private readonly List<int> _lurkers = new();

    /// <summary>Ambushes sprung out of the water by players' amphibious units since the match began.</summary>
    public long LurkerStrikes { get; private set; }

    /// <summary>True if unit i stands with its feet in water.</summary>
    public bool InWater(int i)
    {
        var u = U;
        return (uint)i < (uint)u.Count && _sim.World.Get(u.X[i].FloorToInt(), u.Y[i], u.Z[i].FloorToInt()) == BlockType.Water;
    }

    /// <summary>
    /// True if unit i is a player's amphibious unit lying in the water (its feet in water), or a unit of theirs with
    /// camouflage standing still (see <see cref="Hiding"/>): either way with nothing to fight, not running, not aboard a ship. Its enemies see it only from close by (<see cref="VisionSystem.SpotSubmerged"/>, as a
    /// bog horror lying in its pool), and their soldiers do not go after it from further off; the game draws it sunk.
    /// </summary>
    public bool Lurking(int i)
    {
        var u = U;
        if ((uint)i >= (uint)u.Count || u.Owner[i] >= Simulation.WildPlayer || u.Target[i] != 0 || u.Rout[i] != 0 || u.Aboard[i] != 0) return false;
        var def = Def(i);
        return (def.Amphibious && InWater(i)) || (def.Camouflage && StandsStill(i)); // or Clan Kage hiding (CombatSystem.Shadows.cs)
    }

    /// <summary>True if unit i is cold-blooded and in the cold now (see <see cref="Conditions.Cold"/>).</summary>
    public bool Chilled(int i)
    {
        var u = U;
        return (uint)i < (uint)u.Count && Def(i).ColdBlooded && _sim.Conditions.Cold(u.X[i].FloorToInt(), u.Z[i].FloorToInt());
    }

    /// <summary>Unit i's regeneration (percent a second), <paramref name="regen"/> in the usual way: none in the cold, double in the water.</summary>
    private int SwampRegen(int i, UnitDef def, int regen)
    {
        if (def.ColdBlooded && Chilled(i)) return 0;
        if (def.Amphibious && InWater(i)) return regen * 2;
        return regen;
    }

    /// <summary>
    /// Each tick: the lurking units of players with an ambush tech lie in wait (their next blow is an ambush), and
    /// those that died or came out of the water with nobody to fight lie in wait no more.
    /// </summary>
    private void StepLurkers()
    {
        var u = U;
        if (_lurkers.Count > 0) _lurkers.RemoveAll(id => u.IndexOf(id) is var i && (i < 0 || (u.Target[i] == 0 && !Lurking(i))));
        bool any = false;
        for (int p = 0; p < Simulation.WildPlayer && !any; p++) any = _sim.Economy.AmbushPercent(p) > 0;
        if (!any) return;
        for (int i = 0; i < u.Count; i++)
        {
            if (!Lurking(i) || _sim.Economy.AmbushPercent(u.Owner[i]) == 0) continue;
            int k = _lurkers.BinarySearch(u.Id[i]);
            if (k < 0) _lurkers.Insert(~k, u.Id[i]);
        }
    }

    /// <summary>True if unit i lies in wait in the water (or came up out of it at someone) and has not struck yet.</summary>
    public bool LyingInWait(int i) => _lurkers.Count > 0 && (uint)i < (uint)U.Count && _lurkers.BinarySearch(U.Id[i]) >= 0;

    /// <summary>
    /// Unit i strikes a blow: if it lay in wait in the water and has not struck since, the extra percent of damage this
    /// blow does (its owner's <see cref="TechEffectType.Ambush"/> percent), and it has sprung its ambush; otherwise 0.
    /// </summary>
    private int SpringLurker(int i)
    {
        if (_lurkers.Count == 0) return 0;
        int k = _lurkers.BinarySearch(U.Id[i]);
        if (k < 0) return 0;
        _lurkers.RemoveAt(k);
        LurkerStrikes++;
        return _sim.Economy.AmbushPercent(U.Owner[i]);
    }
}
