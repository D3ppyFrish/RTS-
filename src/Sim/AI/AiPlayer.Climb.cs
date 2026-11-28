using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Wall climbing (Clan Kage's, see "Clan Kage" in docs/rules.md), at the smart levels (Easy and up). A wave within
// SiegeReach of its target that knows of a rival wall, gate or tower within RamLook of it sends up to WallClimbers of its
// climbers that are ready and not fighting up the face of it nearest the wave (a ClimbCommand). Each one up on the top
// goes on at the target from there (an attack-move: it fights the defenders up there, then climbs down inside). The
// climbers are left be by the wave's other orders while they climb, up to ClimbGiveUpTicks; then a while later
// (ClimbAgainTicks) more may go, up to ClimbsPerWave times a wave.
public sealed partial class AiPlayer
{
    /// <summary>Climbers of a wave sent up a wall at once.</summary>
    public const int WallClimbers = 6;
    /// <summary>Times a wave sends climbers up a wall at most.</summary>
    public const int ClimbsPerWave = 3;
    /// <summary>Ticks after climbers are sent before more may go.</summary>
    public const int ClimbAgainTicks = 20 * Tps;
    /// <summary>A climber not up the wall after this long is let go back to the wave.</summary>
    public const int ClimbGiveUpTicks = 40 * Tps;

    private readonly List<int> _climbCrew = new();
    private long _climbSentAt = long.MinValue / 2;
    private int _climbsThisWave;

    /// <summary>The wave's climbers on their way up a wall now (unit ids).</summary>
    public IReadOnlyList<int> ClimbCrew => _climbCrew;

    /// <summary>Times its waves have sent climbers up a wall (for the tests and the match runner's trace).</summary>
    public int ClimbsSent { get; private set; }

    /// <summary>
    /// The smart levels, a wave within <see cref="SiegeReach"/> of its target at (cx, cz) from it: follows its climbers,
    /// or sends some up the known rival wall nearest the wave (see the top of this file).
    /// </summary>
    private void ClimbWalls(Simulation sim, Foe foe, int cx, int cz, Action<Command> issue)
    {
        var u = sim.Units;
        var econ = sim.Economy;
        if (_climbCrew.Count > 0)
        {
            List<int>? up = null;
            bool late = sim.Tick - _climbSentAt > ClimbGiveUpTicks;
            for (int k = _climbCrew.Count - 1; k >= 0; k--)
            {
                int id = _climbCrew[k];
                int i = u.IndexOf(id);
                if (i < 0 || !_wave.Contains(id)) { _climbCrew.RemoveAt(k); continue; }
                if (u.Job[i] == UnitJob.Climb && !late) continue;
                if (u.Job[i] != UnitJob.Climb && econ.WallUnder(i) != null) (up ??= new()).Add(id);
                _climbCrew.RemoveAt(k);
            }
            if (up != null)
            {
                up.Reverse();
                issue(new AttackMoveCommand(Player, up.ToArray(), foe.X, foe.Z));
                Note(sim, $"has {up.Count} up on the wall");
            }
            return;
        }
        if (_climbsThisWave >= ClimbsPerWave || sim.Tick - _climbSentAt < ClimbAgainTicks) return;

        var free = NotFighting(sim, _wave);
        free.RemoveAll(id => _engines.Contains(id) || _shield.Contains(id) || Crewed(id) || id == _saboteur
            || _raidParty.Contains(id) || !econ.ReadyToClimb(u.IndexOf(id)) || econ.WallUnder(u.IndexOf(id)) != null);
        if (free.Count == 0 || !ClimbSpot(sim, cx, cz, out int x, out int z)) return;
        var near = new List<(long D, int Id)>();
        foreach (int id in free)
        {
            int i = u.IndexOf(id);
            near.Add((Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), x, z), id));
        }
        near.Sort();
        var send = new int[Math.Min(WallClimbers, near.Count)];
        for (int k = 0; k < send.Length; k++) send[k] = near[k].Id;
        issue(new ClimbCommand(Player, send, x, z));
        _climbCrew.AddRange(send);
        _climbSentAt = sim.Tick;
        _climbsThisWave++;
        ClimbsSent++;
        Note(sim, $"sends {send.Length} climbers up the wall at {x},{z}");
    }

    /// <summary>
    /// The column on top of a known rival wall, gate or tower within <see cref="RamLook"/> of the wave at (cx, cz) that a
    /// climber from the wave's side can climb onto, the nearest the wave; false if none.
    /// </summary>
    private bool ClimbSpot(Simulation sim, int cx, int cz, out int x, out int z)
    {
        x = z = -1;
        var econ = sim.Economy;
        int cy = Walk.GroundLevel(sim.World, cx, cz);
        if (cy < 0) return false;
        long reach = (long)RamLook * RamLook;
        var spots = new List<(long D, int X, int Z)>();
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Gate or BuildingKind.Tower) || !Rival(k.Owner)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || !b.Active || Dist2(k.X, k.Z, cx, cz) > reach) continue;
            for (int wz = b.Z; wz <= b.MaxZ; wz++)
            for (int wx = b.X; wx <= b.MaxX; wx++)
                spots.Add((Dist2(wx, wz, cx, cz), wx, wz));
        }
        spots.Sort();
        // The nearest few only: a climb is planned round each, and a long wall has many columns.
        for (int n = 0; n < Math.Min(ClimbSpotsTried, spots.Count); n++)
        {
            var (_, wx, wz) = spots[n];
            if (!econ.PlanClimb(wx, wz, cx, cy, cz, out var r) || r.ToY <= r.FootY) continue;
            (x, z) = (wx, wz);
            return true;
        }
        return false;
    }

    /// <summary>Columns of rival walls nearest the wave that <see cref="ClimbSpot"/> tries a climb at.</summary>
    private const int ClimbSpotsTried = 8;

    /// <summary>A new wave: no climbers out, none sent yet.</summary>
    private void ResetClimbs()
    {
        _climbCrew.Clear();
        _climbsThisWave = 0;
        _climbSentAt = long.MinValue / 2;
    }
}
