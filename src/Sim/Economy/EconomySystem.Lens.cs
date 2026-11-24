using System;
using System.Collections.Generic;

namespace Rts.Sim.Economy;

/// <summary>
/// A crystal lens's gaze (see <see cref="EconomySystem.Gazes"/>): <see cref="Player"/> sees the circle of
/// <see cref="Radius"/> columns round (<see cref="X"/>, <see cref="Z"/>) until tick <see cref="Until"/>.
/// </summary>
public readonly record struct Gaze(int Player, int Building, int X, int Z, int Radius, long Until);

/// <summary>Why a player's crystal lens cannot gaze now.</summary>
public enum GazeResult : byte
{
    Ok,
    /// <summary>Not a finished crystal lens of the player's.</summary>
    NotALens,
    /// <summary>It gazed lately and is not ready again (see <see cref="EconomySystem.LensWait"/>).</summary>
    NotReady,
    /// <summary>The player has too little mana.</summary>
    NoMana,
}

/// <summary>
/// Crystal lenses (Crystal lens, a discovery tech opened by digging crystal; see <see cref="BuildingKind.Lens"/>) and the
/// other finds that open discovery techs as the units go about: setting foot in the deep caves.
/// <para>
/// A finished lens is aimed anywhere on the map (<see cref="GazeCommand"/>): for its <see cref="LensDef.Mana"/> mana the
/// circle of <see cref="LensDef.Radius"/> columns round that spot is in its player's sight, through the fog of war and
/// whatever the dark, for <see cref="LensDef.Ticks"/>, and the lens can gaze again <see cref="LensDef.EveryTicks"/> after
/// it began. A lens that is lost (destroyed or taken) ends its gaze. Units underground there are not seen by it (they
/// are heard, not seen: see <see cref="VisionSystem"/>).
/// </para>
/// </summary>
public sealed partial class EconomySystem
{
    // The gazes going on, oldest first.
    private readonly List<Gaze> _gazes = new();
    // Per lens (building id), the tick it can gaze again; sorted so the hash goes in one order.
    private readonly SortedDictionary<int, long> _lensReady = new();

    /// <summary>The crystal lenses' gazes going on now, oldest first.</summary>
    public IReadOnlyList<Gaze> Gazes => _gazes;

    /// <summary>Ticks until the lens can gaze again (0 if it can now).</summary>
    public long LensWait(Building b) =>
        _lensReady.TryGetValue(b.Id, out long at) ? Math.Max(0, at - _sim.Tick) : 0;

    /// <summary>Whether the player's building can gaze now.</summary>
    public GazeResult CanGaze(int player, Building? b)
    {
        if (b == null || b.Owner != player || !b.Active || b.Def.Lens is not { } def) return GazeResult.NotALens;
        if (LensWait(b) > 0) return GazeResult.NotReady;
        if ((Find(player)?.Mana ?? 0) < def.Mana) return GazeResult.NoMana;
        return GazeResult.Ok;
    }

    internal void Apply(GazeCommand c)
    {
        var b = Buildings.Get(c.Building);
        if (CanGaze(c.PlayerId, b) != GazeResult.Ok || !World.InBounds(c.X, 0, c.Z)) return;
        var def = b!.Def.Lens!;
        SpendMana(c.PlayerId, def.Mana);
        _gazes.Add(new Gaze(c.PlayerId, b.Id, c.X, c.Z, def.Radius, _sim.Tick + def.Ticks));
        _lensReady[b.Id] = _sim.Tick + def.EveryTicks;
    }

    /// <summary>Gazes end when their time is up, or when their lens is no longer its player's.</summary>
    private void StepLenses()
    {
        if (_gazes.Count == 0) return;
        long tick = _sim.Tick;
        _gazes.RemoveAll(g => tick >= g.Until || Buildings.Get(g.Building) is not { } b || b.Owner != g.Player || !b.Active);
    }

    /// <summary>
    /// Every unit of a player who has not yet set foot in the deep caves that stands in them now (on ground in the deep
    /// rock, see <see cref="DeepGround"/>) makes that discovery for its player. A map with no deep rock has none.
    /// </summary>
    private void StepDeepFinds()
    {
        if (Rts.Sim.World.RegionGenerator.DeepLayers(World.SizeY) == 0) return;
        for (int i = 0; i < U.Count; i++)
        {
            int p = U.Owner[i];
            if (p >= Simulation.WildPlayer || U.Town[i] != 0 || U.Hp[i] <= 0 || !DeepGround(U.Y[i])) continue;
            Discover(p, Rts.Sim.Content.Discovery.Deeps);
        }
    }

    private void MixLenses(ref ulong h)
    {
        void Mix(ref ulong hh, long v) { hh = (hh ^ (ulong)v) * 1099511628211UL; }
        Mix(ref h, _gazes.Count);
        foreach (var g in _gazes)
        {
            Mix(ref h, g.Player); Mix(ref h, g.Building); Mix(ref h, g.X); Mix(ref h, g.Z); Mix(ref h, g.Radius); Mix(ref h, g.Until);
        }
        foreach (var (id, at) in _lensReady)
        {
            Mix(ref h, id); Mix(ref h, at);
        }
    }
}
