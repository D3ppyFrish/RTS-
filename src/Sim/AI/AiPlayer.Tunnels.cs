using System;
using System.Collections.Generic;
using Rts.Sim.Economy;
using Rts.Sim.Units;

namespace Rts.Sim.AI;

// The hive's trick at the smart levels (Easy and up): a wave whose sappers burst out of the ground (the Deepfolk's
// burrowers) tunnels under an enemy wall rather than raising a ladder against it. Within SiegeReach of its target,
// knowing of an enemy wall, gate or tower within RamLook, it picks the crossing nearest the wave: a straight line
// across the wall, square to it, from a mouth TunnelLead columns out on the wave's side to an exit TunnelRise columns
// past the far face, with no other building on it and level ground at both ends. One burrower walks to the mouth and
// digs in three legs: down to TunnelDepth levels under the wall's foot by the column before it, on under the wall at
// that depth (deep enough that the wall keeps its footing and stands), and up to the surface at the exit. Once it is
// out, up to TunnelSwarm of the wave's soldiers nearest the mouth go through after it and, as each comes up at the
// exit, on at the target, bursting out of the ground. A tunnel that fails (the burrower killed, water or bedrock in
// the way) is tried again after TunnelAgainTicks, up to TunnelsPerWave a wave. Its digger and swarm are left be by
// the rest of the wave's orders while they are at it. Such a wave raises a ladder only where no tunnel will do.
public sealed partial class AiPlayer
{
    /// <summary>Columns out from the wall on the wave's side where the tunnel's mouth is dug.</summary>
    public const int TunnelLead = 5;
    /// <summary>Columns past the wall's far face where the tunnel comes up.</summary>
    public const int TunnelRise = 4;
    /// <summary>Levels under the wall's foot the tunnel runs at, so the wall keeps its footing.</summary>
    public const int TunnelDepth = 3;
    /// <summary>Soldiers of the wave sent through a tunnel once it is open.</summary>
    public const int TunnelSwarm = 16;
    /// <summary>Tunnels a wave digs at most.</summary>
    public const int TunnelsPerWave = 3;
    /// <summary>A tunnel that failed is tried again (elsewhere if it must) after this many ticks.</summary>
    public const int TunnelAgainTicks = 20 * Tps;
    /// <summary>A digger not at the mouth, or a leg not dug, after this long is given up on.</summary>
    public const int TunnelLegTicks = 90 * Tps;
    /// <summary>A soldier sent through that has not come up at the exit after this long goes back to the wave.</summary>
    public const int TunnelWalkTicks = 60 * Tps;

    private enum TunnelStage { None, ToMouth, Down, Under, Up, Open }

    private TunnelStage _tunnelStage;
    private int _tunnelDigger, _tunnelsDug;
    private long _tunnelAt, _swarmSent;
    // The crossing: the mouth, the column before the wall, the column past it, the exit, and the depth (feet level) under it.
    private (int X, int Z) _tunnelMouth, _tunnelBefore, _tunnelPast, _tunnelExit;
    private int _tunnelDeep, _tunnelExitY;
    // The wave's units at the tunnel (the digger, the swarm on its way through): the wave's other orders leave them be.
    private readonly List<int> _tunnelCrew = new(), _swarm = new();

    /// <summary>The wave's tunnel exit (column and feet level) once the burrower has come up there, else null.</summary>
    public (int X, int Y, int Z)? TunnelExit => _tunnelStage == TunnelStage.Open ? (_tunnelExit.X, _tunnelExitY, _tunnelExit.Z) : null;

    /// <summary>The burrower digging the wave's tunnel (unit id), or 0.</summary>
    public int TunnelDigger => _tunnelStage is TunnelStage.None or TunnelStage.Open ? 0 : _tunnelDigger;

    /// <summary>Tunnels the current wave has started.</summary>
    public int TunnelsDug => _tunnelsDug;

    /// <summary>True if the unit is at the wave's ladder or tunnel, so the wave's other orders leave it be.</summary>
    private bool Crewed(int id) => _ladderCrew.Contains(id) || _tunnelCrew.Contains(id);

    /// <summary>True if the sapper bursts out of the ground (the Deepfolk's burrower): its wave tunnels under walls.</summary>
    private static bool Burrows(UnitDef def) => def.Role == UnitRole.Sapper && def.Burst > 0;

    /// <summary>
    /// The smart levels, a wave within <see cref="SiegeReach"/> of its target: digs a tunnel under a known enemy wall
    /// and sends a swarm through it (see the top of this file). True while it is at a tunnel, or has one open, so the
    /// wave raises no ladder; false if it has no burrower or finds no crossing (a ladder may do instead).
    /// </summary>
    private bool Undermine(Simulation sim, Foe foe, int cx, int cz, Action<Command> issue)
    {
        var u = sim.Units;
        _tunnelCrew.RemoveAll(id => !u.Exists(id) || !_wave.Contains(id));
        _swarm.RemoveAll(id => !_tunnelCrew.Contains(id));
        if (_tunnelStage != TunnelStage.None)
        {
            StepTunnelDig(sim, foe, issue);
            return true;
        }
        if (_tunnelsDug >= TunnelsPerWave || sim.Tick < _tunnelAt) return _tunnelsDug > 0;
        int digger = 0;
        long near = long.MaxValue;
        bool any = false;
        foreach (int id in _wave)
        {
            int i = u.IndexOf(id);
            if (i < 0 || !Burrows(sim.Content.Units[u.Def[i]])) continue;
            any = true;
            if (u.Rout[i] > 0) continue;
            long d = Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), cx, cz);
            if (d < near) (near, digger) = (d, id);
        }
        if (!any) return false;
        if (digger == 0 || !TunnelSpot(sim, cx, cz)) return _tunnelsDug > 0;
        _tunnelDigger = digger;
        _tunnelCrew.Add(digger);
        _tunnelStage = TunnelStage.ToMouth;
        _tunnelAt = sim.Tick;
        _tunnelsDug++;
        issue(new MoveCommand(Player, new[] { digger }, _tunnelMouth.X, _tunnelMouth.Z));
        Note(sim, $"has a burrower tunnel under the wall from {_tunnelMouth.X},{_tunnelMouth.Z}");
        return true;
    }

    /// <summary>The tunnel under way: the digger's next leg, or, once it is open, the swarm through it.</summary>
    private void StepTunnelDig(Simulation sim, Foe foe, Action<Command> issue)
    {
        var u = sim.Units;
        if (_tunnelStage == TunnelStage.Open)
        {
            Swarm(sim, foe, issue);
            return;
        }
        int i = u.IndexOf(_tunnelDigger);
        if (i < 0 || !_tunnelCrew.Contains(_tunnelDigger) || sim.Tick - _tunnelAt > TunnelLegTicks)
        {
            LoseTunnel(sim.Tick + TunnelAgainTicks);
            return;
        }
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
        bool digging = u.Job[i] == UnitJob.Tunnel;
        switch (_tunnelStage)
        {
            case TunnelStage.ToMouth:
                if (Math.Abs(x - _tunnelMouth.X) + Math.Abs(z - _tunnelMouth.Z) > 1)
                {
                    // Pushed off on the way, or stopped to fight: sent on again.
                    if (u.Order[i] == UnitOrder.Idle && u.Target[i] == 0)
                        issue(new MoveCommand(Player, new[] { _tunnelDigger }, _tunnelMouth.X, _tunnelMouth.Z));
                    return;
                }
                Leg(sim, TunnelStage.Down, _tunnelBefore, _tunnelDeep, issue);
                return;
            case TunnelStage.Down:
                if (digging) return;
                if ((x, z) != _tunnelBefore) break;
                Leg(sim, TunnelStage.Under, _tunnelPast, _tunnelDeep, issue);
                return;
            case TunnelStage.Under:
                if (digging) return;
                if ((x, z) != _tunnelPast) break;
                Leg(sim, TunnelStage.Up, _tunnelExit, _tunnelExitY, issue);
                return;
            case TunnelStage.Up:
                if (digging) return;
                if ((x, z) != _tunnelExit || u.Y[i] != _tunnelExitY) break;
                // Out on the far side: the burrower goes back to the wave, the swarm goes through.
                _tunnelStage = TunnelStage.Open;
                _tunnelCrew.Remove(_tunnelDigger);
                Note(sim, $"has its tunnel open at {_tunnelExit.X},{_tunnelExit.Z}");
                SendSwarm(sim, issue);
                return;
        }
        // The leg stopped short (water, bedrock, a building in the way): this crossing is given up.
        LoseTunnel(sim.Tick + TunnelAgainTicks);
    }

    /// <summary>The digger digs on toward column <paramref name="to"/> at feet level <paramref name="y"/>.</summary>
    private void Leg(Simulation sim, TunnelStage stage, (int X, int Z) to, int y, Action<Command> issue)
    {
        issue(new TunnelCommand(Player, new[] { _tunnelDigger }, to.X, y, to.Z));
        _tunnelStage = stage;
        _tunnelAt = sim.Tick;
    }

    /// <summary>Up to <see cref="TunnelSwarm"/> of the wave's soldiers nearest the mouth go through to the exit.</summary>
    private void SendSwarm(Simulation sim, Action<Command> issue)
    {
        var u = sim.Units;
        var free = new List<(long D, int Id)>();
        foreach (int id in _wave)
        {
            if (_engines.Contains(id) || _shield.Contains(id) || Crewed(id)) continue;
            int i = u.IndexOf(id);
            if (i < 0 || u.Rout[i] > 0) continue;
            var def = sim.Content.Units[u.Def[i]];
            if (def.Machine || def.Role is UnitRole.Sapper or UnitRole.Siege || !def.CanAttack) continue;
            free.Add((Dist2(u.X[i].FloorToInt(), u.Z[i].FloorToInt(), _tunnelMouth.X, _tunnelMouth.Z), id));
        }
        free.Sort();
        var send = new int[Math.Min(TunnelSwarm, free.Count)];
        for (int k = 0; k < send.Length; k++) send[k] = free[k].Id;
        _swarmSent = sim.Tick;
        if (send.Length == 0) return;
        issue(new MoveCommand(Player, send, _tunnelExit.X, _tunnelExit.Z, _tunnelExitY));
        _swarm.AddRange(send);
        _tunnelCrew.AddRange(send);
        Note(sim, $"sends {send.Length} through its tunnel");
    }

    /// <summary>Soldiers come up at the exit go on at the target; those not through after <see cref="TunnelWalkTicks"/> go back to the wave.</summary>
    private void Swarm(Simulation sim, Foe foe, Action<Command> issue)
    {
        var u = sim.Units;
        List<int>? through = null;
        foreach (int id in _swarm)
        {
            int i = u.IndexOf(id);
            if (Math.Abs(u.X[i].FloorToInt() - _tunnelExit.X) > 2 || Math.Abs(u.Z[i].FloorToInt() - _tunnelExit.Z) > 2) continue;
            (through ??= new()).Add(id);
        }
        if (through != null)
        {
            issue(new AttackMoveCommand(Player, through.ToArray(), foe.X, foe.Z));
            foreach (int id in through)
            {
                _swarm.Remove(id);
                _tunnelCrew.Remove(id);
            }
        }
        if (_swarm.Count > 0 && sim.Tick - _swarmSent > TunnelWalkTicks)
        {
            foreach (int id in _swarm) _tunnelCrew.Remove(id);
            _swarm.Clear();
        }
    }

    /// <summary>The wave's tunnel is given up: its crew goes back to the wave, and another may be dug from <paramref name="again"/> on.</summary>
    private void LoseTunnel(long again)
    {
        _tunnelStage = TunnelStage.None;
        _tunnelDigger = 0;
        _tunnelAt = again;
        _tunnelCrew.Clear();
        _swarm.Clear();
    }

    /// <summary>A new wave: no tunnel, none dug yet.</summary>
    private void ResetTunnels()
    {
        LoseTunnel(0);
        _tunnelsDug = 0;
    }

    /// <summary>
    /// Where the wave at (cx, cz) tunnels under a wall: across the known enemy wall, gate or tower within
    /// <see cref="RamLook"/> whose mouth is nearest the wave. Sets the crossing's fields; false if there is none.
    /// </summary>
    private bool TunnelSpot(Simulation sim, int cx, int cz)
    {
        long best = long.MaxValue, reach = (long)RamLook * RamLook;
        foreach (var k in _known.Values)
        {
            if (k.Kind is not (BuildingKind.Wall or BuildingKind.Tower) || !Rival(k.Owner)) continue;
            if (sim.Buildings.Get(k.Id) is not { } b || b.Owner != k.Owner || b.Planned || Dist2(k.X, k.Z, cx, cz) > reach) continue;
            for (int wx = b.X; wx <= b.MaxX; wx++)
            for (int wz = b.Z; wz <= b.MaxZ; wz++)
            for (int d = 0; d < 4; d++)
            {
                int dx = d == 0 ? 1 : d == 1 ? -1 : 0, dz = d == 2 ? 1 : d == 3 ? -1 : 0;
                // (wx, wz) is on the near face: the column before it, on the wave's side, is not the wall's.
                if (sim.Buildings.AtColumn(wx - dx, wz - dz) == b) continue;
                long dd = Dist2(wx - TunnelLead * dx, wz - TunnelLead * dz, cx, cz);
                if (dd >= best || !TunnelCrossing(sim, b, wx, wz, dx, dz)) continue;
                best = dd;
            }
        }
        return best != long.MaxValue;
    }

    /// <summary>
    /// True (and the crossing's fields set) if a tunnel can run straight across building <paramref name="b"/> at
    /// (wx, wz), its near face, in direction (dx, dz): nothing else built on the way, the wall at most a few columns
    /// thick, room above bedrock to run <see cref="TunnelDepth"/> under its foot, and ground at the mouth and the exit
    /// near enough to the wall's foot that each leg reaches its level in the columns it has.
    /// </summary>
    private bool TunnelCrossing(Simulation sim, Building b, int wx, int wz, int dx, int dz)
    {
        var w = sim.World;
        int deep = b.Y - TunnelDepth;
        if (deep < 2) return false;
        // The far face: the last of the wall's columns along the line.
        int thick = 0;
        while (sim.Buildings.AtColumn(wx + (thick + 1) * dx, wz + (thick + 1) * dz) == b)
            if (++thick > 4) return false;
        int px = wx + (thick + 1) * dx, pz = wz + (thick + 1) * dz;
        int ex = px + TunnelRise * dx, ez = pz + TunnelRise * dz;
        int mx = wx - TunnelLead * dx, mz = wz - TunnelLead * dz;
        if (!w.InBounds(mx, 1, mz) || !w.InBounds(ex, 1, ez)) return false;
        for (int s = -TunnelLead; s <= thick + 1 + TunnelRise; s++)
        {
            if (s >= 0 && s <= thick) continue;
            int x = wx + s * dx, z = wz + s * dz;
            if (sim.Buildings.AtColumn(x, z) != null) return false;
            for (int y = deep - 1; y <= deep + 1; y++)
                if (w.Get(x, y, z) == BlockType.Water) return false;
        }
        int gm = Walk.GroundLevel(w, mx, mz), ge = Walk.GroundLevel(w, ex, ez);
        // Down from the mouth to the depth by the column before the wall (TunnelLead - 1 columns); up from the
        // column past it to the exit (TunnelRise columns).
        if (gm < 0 || gm - deep > TunnelLead - 1 || gm < b.Y - 1) return false;
        if (ge < 0 || ge - deep > TunnelRise || ge < b.Y - 1) return false;
        if (KeepsOff(sim, mx, mz) || KeepsOff(sim, ex, ez)) return false;
        _tunnelMouth = (mx, mz);
        _tunnelBefore = (wx - dx, wz - dz);
        _tunnelPast = (px, pz);
        _tunnelExit = (ex, ez);
        _tunnelDeep = deep;
        _tunnelExitY = ge;
        return true;
    }
}
