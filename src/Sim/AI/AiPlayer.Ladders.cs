using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// Siege ladders at the smart levels (Easy and up). A wave within SiegeReach of its target that knows of an enemy wall
// or tower within RamLook of it (or, with none, has got no nearer its target for LadderStallTicks and finds a cliff of
// 2 to Walk.MaxLadder levels on the straight way there) has up to LadderBuilders of its sappers raise a ladder against
// it, from the side the wave stands on. Once the ladder is up, up to LadderClimbers of the wave's soldiers climb it:
// with the sappers who raised it (who would otherwise dig in under the face it leans on); the first soldier onto the
// top stays there and holds it, so an enemy up there cannot throw it down unopposed, and the
// rest go on over at the target. A ladder thrown down is raised again after LadderAgainTicks, up to LaddersPerWave a
// wave. Its sappers, climbers and holder are left be by the rest of the wave's orders while they are at it.
public sealed partial class AiPlayer
{
    /// <summary>Sappers of a wave sent to raise a ladder.</summary>
    public const int LadderBuilders = 2;
    /// <summary>Soldiers of a wave sent up a ladder once it is up (the first one up holds the top).</summary>
    public const int LadderClimbers = 6;
    /// <summary>Ladders a wave raises at most.</summary>
    public const int LaddersPerWave = 3;
    /// <summary>A ladder lost (thrown down, or never raised) is tried again after this many ticks.</summary>
    public const int LadderAgainTicks = 20 * Tps;
    /// <summary>With no wall known, a wave that has got no nearer its target for this long looks for a cliff to raise a ladder against.</summary>
    public const int LadderStallTicks = 15 * Tps;
    /// <summary>A climber not up on the top after this long is let go back to the wave.</summary>
    public const int ClimbTicks = 45 * Tps;

    // The ladder the wave is raising or has up: its foot, and when it was ordered (or when the next may be).
    private (int X, int Z)? _ladderFoot;
    private long _ladderAt;
    private int _laddersRaised, _ladderHolder;
    private bool _ladderClimbed;
    private long _climbSent;
    // The wave's units at a ladder (builders, climbers on their way, the holder): the wave's other orders leave them be.
    private readonly List<int> _ladderCrew = new(), _ladderBuilders = new(), _climbers = new();
    // How near the wave has got to its target (columns), and since when: a wave held up by a cliff looks for a ladder spot.
    private int _ladderBest = int.MaxValue;
    private long _ladderBestAt;

    /// <summary>The wave's ladder: the one of this player at the foot it ordered, or null.</summary>
    public Ladder? WaveLadder(Simulation sim)
    {
        if (_ladderFoot is not var (fx, fz)) return null;
        foreach (var l in sim.Economy.Ladders)
            if (l.Owner == Player && l.X == fx && l.Z == fz) return l;
        return null;
    }

    /// <summary>The wave's soldier holding the top of its ladder (unit id), or 0.</summary>
    public int LadderHolder => _ladderHolder;

    /// <summary>
    /// The smart levels, a wave within <see cref="SiegeReach"/> of its target at (cx, cz) from it: raises a ladder and
    /// sends soldiers up it (see the top of this file). Its crew is listed in <see cref="_ladderCrew"/>.
    /// </summary>
    private void Escalade(Simulation sim, Foe foe, int cx, int cz, int far, Action<Command> issue)
    {
        var u = sim.Units;
        _ladderCrew.RemoveAll(id => !u.Exists(id) || !_wave.Contains(id));
        _ladderBuilders.RemoveAll(id => !_ladderCrew.Contains(id));
        _climbers.RemoveAll(id => !_ladderCrew.Contains(id));
        if (_ladderHolder != 0 && !_ladderCrew.Contains(_ladderHolder)) _ladderHolder = 0;
        if (far < _ladderBest)
        {
            _ladderBest = far;
            _ladderBestAt = sim.Tick;
        }

        if (_ladderFoot != null)
        {
            var ladder = WaveLadder(sim);
            if (ladder == null)
            {
                // Thrown down, fallen, or never raised: another may go up in a while.
                if (sim.Tick - _ladderAt > 2 * ArmyTicks) LoseLadder(sim.Tick + LadderAgainTicks);
                return;
            }
            if (!ladder.Done) return;
            if (!_ladderClimbed)
            {
                SendClimbers(sim, ladder, issue);
                return;
            }
            Climb(sim, ladder, foe, issue);
            return;
        }

        if (_laddersRaised >= LaddersPerWave || sim.Tick < _ladderAt) return;
        if (!LadderSpot(sim, foe, cx, cz, out int x1, out int z1, out int x2, out int z2)) return;
        var builders = new List<(long D, int Id)>();
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0 || sim.Content.Units[u.Def[i]].Role != UnitRole.Sapper) continue;
            builders.Add((Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), x1, z1), id));
        }
        if (builders.Count == 0) return;
        builders.Sort();
        var send = new int[Math.Min(LadderBuilders, builders.Count)];
        for (int k = 0; k < send.Length; k++) send[k] = builders[k].Id;
        issue(new LadderCommand(Player, send, x1, z1, x2, z2));
        _ladderBuilders.AddRange(send);
        _ladderCrew.AddRange(send);
        _ladderFoot = (x1, z1);
        _ladderAt = sim.Tick;
        _ladderClimbed = false;
        _laddersRaised++;
        Note(sim, $"has its sappers raise a ladder at {x1},{z1}");
    }

    /// <summary>Its builders and up to <see cref="LadderClimbers"/> of the wave's soldiers nearest the ladder's foot are sent up it, onto the top.</summary>
    private void SendClimbers(Simulation sim, Ladder ladder, Action<Command> issue)
    {
        var u = sim.Units;
        var free = new List<(long D, int Id)>();
        foreach (int id in _wave)
        {
            if (_engines.Contains(id) || _shield.Contains(id) || _ladderCrew.Contains(id)) continue;
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.Machine || def.Role is UnitRole.Sapper or UnitRole.Siege || !def.CanAttack) continue;
            free.Add((Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), ladder.X, ladder.Z), id));
        }
        free.Sort();
        // The sappers who raised it go up too, so they do not dig their way in under the face it leans on.
        var send = new List<int>(_ladderBuilders);
        for (int k = 0; k < Math.Min(LadderClimbers, free.Count); k++) send.Add(free[k].Id);
        _ladderBuilders.Clear();
        _ladderClimbed = true;
        _climbSent = sim.Tick;
        if (send.Count == 0) return;
        issue(new MoveCommand(Player, send.ToArray(), ladder.FaceX, ladder.FaceZ, ladder.Top));
        foreach (int id in send)
        {
            _climbers.Add(id);
            if (!_ladderCrew.Contains(id)) _ladderCrew.Add(id);
        }
        Note(sim, $"sends {send.Count} up its ladder");
    }

    /// <summary>
    /// Climbers up on the top: the first holds it (it stays there), the rest go on at the target. Those not up after
    /// <see cref="ClimbTicks"/> go back to the wave.
    /// </summary>
    private void Climb(Simulation sim, Ladder ladder, Foe foe, Action<Command> issue)
    {
        var u = sim.Units;
        List<int>? over = null;
        foreach (int id in _climbers)
        {
            int i = u.IndexOf(id);
            if (u.Y[i] != ladder.Top || Math.Abs(u.X[i].FloorToInt() - ladder.X) > 2 || Math.Abs(u.Z[i].FloorToInt() - ladder.Z) > 2) continue;
            if (_ladderHolder == 0 && sim.Content.Units[u.Def[i]].Role != UnitRole.Sapper)
            {
                _ladderHolder = id;
                continue;
            }
            (over ??= new()).Add(id);
        }
        if (_ladderHolder != 0) _climbers.Remove(_ladderHolder);
        if (over != null)
        {
            issue(new AttackMoveCommand(Player, over.ToArray(), foe.X, foe.Z));
            foreach (int id in over)
            {
                _climbers.Remove(id);
                _ladderCrew.Remove(id);
            }
        }
        if (_climbers.Count > 0 && sim.Tick - _climbSent > ClimbTicks)
        {
            foreach (int id in _climbers) _ladderCrew.Remove(id);
            _climbers.Clear();
        }
    }

    /// <summary>The wave's ladder is gone: its crew goes back to the wave, and another may be raised from <paramref name="again"/> on.</summary>
    private void LoseLadder(long again)
    {
        _ladderFoot = null;
        _ladderAt = again;
        _ladderHolder = 0;
        _ladderClimbed = false;
        _ladderCrew.Clear();
        _ladderBuilders.Clear();
        _climbers.Clear();
    }

    /// <summary>A new wave: no ladder, none raised yet.</summary>
    private void ResetLadders()
    {
        LoseLadder(0);
        _laddersRaised = 0;
        _ladderBest = int.MaxValue;
        _ladderBestAt = 0;
    }

    /// <summary>
    /// Where the wave at (cx, cz) raises a ladder: against the known enemy wall or tower within <see cref="RamLook"/>
    /// whose spot is nearest the wave, else, once it has been held up <see cref="LadderStallTicks"/>, against the first
    /// cliff on the straight way to the target. (x1, z1) is the foot and (x2, z2) the column it leans on.
    /// </summary>
    private bool LadderSpot(Simulation sim, Foe foe, int cx, int cz, out int x1, out int z1, out int x2, out int z2)
    {
        x1 = z1 = x2 = z2 = -1;
        var econ = sim.Economy;
        long best = long.MaxValue, reach = (long)RamLook * RamLook;
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Tower) || !Rival(k.Owner)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || b.Planned || Dist2(k.X, k.Z, cx, cz) > reach) continue;
            for (int wx = b.X; wx <= b.MaxX; wx++)
            for (int wz = b.Z; wz <= b.MaxZ; wz++)
            for (int d = 0; d < 4; d++)
            {
                int fx = wx + (d == 0 ? -1 : d == 1 ? 1 : 0), fz = wz + (d == 2 ? -1 : d == 3 ? 1 : 0);
                if (fx >= b.X && fx <= b.MaxX && fz >= b.Z && fz <= b.MaxZ) continue;
                long dd = Dist2(fx, fz, cx, cz);
                if (dd >= best || econ.PlanLadder(fx, fz, wx, wz, out int foot) < 0 || Tunnelled(sim, wx, wz, foot)) continue;
                (best, x1, z1, x2, z2) = (dd, fx, fz, wx, wz);
            }
        }
        if (best != long.MaxValue) return true;
        return sim.Tick - _ladderBestAt >= LadderStallTicks && CliffSpot(sim, cx, cz, foe.AX, foe.AZ, out x1, out z1, out x2, out z2);
    }

    /// <summary>
    /// Walks the straight way from (cx, cz) to (tx, tz) a column at a time (never on a diagonal) to the first rise of 2
    /// to <see cref="Walk.MaxLadder"/> levels that a ladder can be raised against; false if it meets a higher one, a
    /// building other than a wall, or none.
    /// </summary>
    private static bool CliffSpot(Simulation sim, int cx, int cz, int tx, int tz, out int x1, out int z1, out int x2, out int z2)
    {
        x1 = z1 = x2 = z2 = -1;
        var w = sim.World;
        int x = cx, z = cz;
        int g = Walk.GroundLevel(w, x, z);
        for (int steps = 0; steps < 2 * RamLook && (x != tx || z != tz) && g >= 0; steps++)
        {
            int nx = x, nz = z;
            if (Math.Abs(tx - x) >= Math.Abs(tz - z)) nx += Math.Sign(tx - x);
            else nz += Math.Sign(tz - z);
            if (sim.Buildings.AtColumn(nx, nz) is { } b && b.Def.Kind != BuildingKind.Wall) return false;
            int ng = Walk.GroundLevel(w, nx, nz);
            if (ng < 0) return false;
            int rise = ng - g;
            if (rise > Walk.MaxLadder) return false;
            if (rise >= 2)
            {
                if (sim.Economy.PlanLadder(x, z, nx, nz, out int foot) < 0 || Tunnelled(sim, nx, nz, foot)) return false;
                (x1, z1, x2, z2) = (x, z, nx, nz);
                return true;
            }
            (x, z, g) = (nx, nz, ng);
        }
        return false;
    }

    /// <summary>
    /// True if a unit at the foot level of a ladder could walk straight into the column it would lean on (a tunnel or
    /// a gap dug in under it): a step into the column goes in there, not up the ladder, so none is raised.
    /// </summary>
    private static bool Tunnelled(Simulation sim, int faceX, int faceZ, int foot) =>
        Walk.CanStand(sim.World, faceX, foot, faceZ) || Walk.CanStand(sim.World, faceX, foot + 1, faceZ);
}
