using System;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Listening posts at the smart levels (Easy and up). Sappers tunnel under walls unseen, so once it has a finished
// barracks it keeps one listening post on the side of its town hall the enemy is on (where it last saw an enemy
// town hall or building, PostReach columns from the hall; by the hall if it knows of none). While it is threatened
// by diggers (it has seen an enemy sapper, or heard an enemy unit underground, in the last SpotMemoryTicks) it keeps
// up to MostPosts: a new one goes where the diggers were last seen (no further than DiggerReach from its town hall),
// unless a post of its already hears that spot. One at a time, as it can pay for it; Hard lays them out like the rest
// of its town (clear of its wall line and pits).
public sealed partial class AiPlayer
{
    /// <summary>Listening posts it keeps while diggers threaten it (one otherwise).</summary>
    public const int MostPosts = 3;
    /// <summary>How far (columns) from its town hall's middle, towards the enemy, its first post goes.</summary>
    public const int PostReach = 14;
    /// <summary>A post towards diggers goes no further than this (columns) from its town hall's middle: its town's edge.</summary>
    public const int DiggerReach = HardTownRadius;
    /// <summary>Columns round a post (or a spot it wants heard) in which it looks for room for the post.</summary>
    public const int PostRoom = 8;

    // Where and when it last saw an enemy sapper or heard an enemy unit underground (see Look, Listen).
    private (int X, int Z)? _diggers;
    private long _diggersAt = long.MinValue;

    /// <summary>True while it has seen an enemy sapper or heard an enemy unit underground lately.</summary>
    public bool Diggers(Simulation sim) => _diggers != null && sim.Tick - _diggersAt <= SpotMemoryTicks;

    /// <summary>
    /// Called from <see cref="Look"/> for each rival unit it sees: remembers sappers and units heard underground
    /// as diggers.
    /// </summary>
    private void Listen(Simulation sim, int i, UnitDef def)
    {
        if (def.Role != UnitRole.Sapper && !sim.Vision.Underground(i)) return;
        var u = sim.Units;
        if (!Diggers(sim)) Note(sim, sim.Vision.Underground(i) ? "hears enemies digging underground" : "sees enemy sappers");
        _diggers = (u.X[i].FloorToInt(), u.Z[i].FloorToInt());
        _diggersAt = sim.Tick;
    }

    /// <summary>The smart levels: listening posts (see the top of this file).</summary>
    private void ListeningPosts(Simulation sim, Building hall, Action<Command> issue)
    {
        if (!Smart || !hall.Active) return;
        var def = FirstBuilding(sim, BuildingKind.ListeningPost);
        if (def == null) return;
        bool barracks = false;
        int posts = 0;
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player) continue;
            if (b.Def.Kind == BuildingKind.ListeningPost)
            {
                if (!b.Active) return; // one is on its way
                posts++;
            }
            else if (IsBarracks(sim, b.Def) && b.Active) barracks = true;
        }
        bool threat = Diggers(sim);
        if ((!barracks && !threat) || posts >= (threat ? MostPosts : 1)) return;

        var home = Middle(hall);
        var toward = threat ? _diggers : KnownHall(home.X, home.Z) is { } h ? (h.X, h.Z) : _lastFoe;
        var spot = toward is var (tx, tz) ? Toward(home.X, home.Z, tx, tz, threat ? DiggerReach : PostReach, sim.World) : home;
        if (posts > 0 && Heard(sim, spot.X, spot.Z)) return; // diggers there are heard already
        if (!Affordable(sim, def.Cost)) return;
        if (Hard)
        {
            if (!Room(sim, def, spot, PostRoom, out int x, out int z)) return;
            issue(new BuildCommand(Player, Array.Empty<int>(), def.Id, x, z));
            Spend(def.Cost);
        }
        else PlaceNear(sim, def, spot, PostRoom, issue);
        Note(sim, threat ? $"lays out a listening post towards the diggers at {spot.X},{spot.Z}"
            : $"lays out a listening post at {spot.X},{spot.Z}");
    }

    /// <summary>True if one of its finished listening posts hears column (x, z).</summary>
    private bool Heard(Simulation sim, int x, int z)
    {
        foreach (var b in sim.Buildings.All)
        {
            if (b.Owner != Player || b.Def.Kind != BuildingKind.ListeningPost || !b.Active) continue;
            int dx = x < b.X ? b.X - x : x > b.MaxX ? x - b.MaxX : 0;
            int dz = z < b.Z ? b.Z - z : z > b.MaxZ ? z - b.MaxZ : 0;
            if ((long)dx * dx + (long)dz * dz <= (long)b.Def.Hearing * b.Def.Hearing) return true;
        }
        return false;
    }
}
