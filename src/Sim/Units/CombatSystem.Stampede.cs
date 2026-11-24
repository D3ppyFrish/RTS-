using System;
using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// Beast pens and the stampede (issue #152): the Orcs' Beast pens tech builds the beast pen (<see cref="BuildingKind.Pen"/>,
/// numbers in <see cref="PenDef"/>).
/// <para>
/// Penning: tamed beasts (<see cref="UnitDef.Pennable"/>: wolves and boars an Orc player recruited) sent to their owner's
/// finished pen (<see cref="PenCommand"/>) walk into its yard and are kept there once they stand in it, up to
/// <see cref="PenDef.Keeps"/> (those on their way count). A penned beast stands at its place in the yard, takes no orders
/// and does not fight; enemies can still strike it. Another order on the way calls its penning off. When the pen is lost
/// (destroyed, or no longer its owner's), or its owner lets them out (<see cref="UnpenCommand"/>), the beasts are free
/// where they stand.
/// </para>
/// <para>
/// The stampede (<see cref="StampedeCommand"/>): every beast kept in the pen bursts out, runs straight to the start of the
/// line the player drew and on along it to its end, at <see cref="PenDef.Speed"/> columns a second, for at most
/// <see cref="PenDef.Run"/> columns. The beasts run two abreast, half a column either side of the line. Each beast that
/// comes within <see cref="TrampleReach"/> of an enemy unit (within <see cref="TrampleHeight"/> levels) tramples it once in
/// the run: <see cref="PenDef.Trample"/> damage, landing with the tick's blows, and the unit is knocked down for
/// <see cref="PenDef.DownTicks"/> (it neither walks nor strikes; ships and machines are not knocked down). A running beast
/// steps up a level and drops any height; a beast that runs into a building of an enemy's that is built of wood (palisade,
/// planks or timber) strikes it once with <see cref="PenDef.Smash"/> and runs on if that clears its way, and it stops
/// there otherwise, as it stops at stone, at its own side's buildings and at ground too high to climb. A running beast
/// takes no orders and does not fight. Once it stops, it is tired: for <see cref="PenDef.TiredTicks"/> it walks at
/// <see cref="PenDef.Slow"/> percent of its pace, but it takes orders and fights as usual, and can be sent back to a pen.
/// </para>
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>A running beast tramples enemies this near (columns).</summary>
    public static readonly Fix64 TrampleReach = Fix64.One;
    /// <summary>... and within this many levels of it.</summary>
    public const int TrampleHeight = 2;
    /// <summary>A beast blocked by a wooden building it struck waits this many ticks for the way to clear before it stops.</summary>
    public const int SmashWait = 6;
    /// <summary>How far either side of the line (columns) the two files of a stampede run.</summary>
    public static readonly Fix64 StampedeSpread = Fix64.Half;

    // Who each running beast has trampled (unit ids) or struck (minus the building id) in its run, sorted.
    private readonly List<(int Runner, int Victim)> _trampled = new();
    private readonly List<int> _trample = new();

    /// <summary>Enemy units trampled by stampedes so far (for the game's tests and its scripted screenshots).</summary>
    public int Trampled { get; private set; }

    /// <summary>True if unit i runs in a stampede now.</summary>
    public bool Stampeding(int i) => (uint)i < (uint)U.Count && U.RunStage[i] != 0;

    /// <summary>True if unit i is kept in a beast pen.</summary>
    public bool IsPenned(int i) => (uint)i < (uint)U.Count && U.Penned[i] != 0;

    /// <summary>The beasts kept in pen <paramref name="pen"/> (a building id), and with <paramref name="coming"/> those on their way into it too.</summary>
    public int PenCount(int pen, bool coming = false)
    {
        var u = U;
        int n = 0;
        for (int i = 0; i < u.Count; i++)
            if (u.Penned[i] == pen || coming && u.PenFor[i] == pen) n++;
        return n;
    }

    /// <summary>The beasts kept in pen <paramref name="pen"/>, by unit index, lowest id first.</summary>
    public List<int> PennedIn(int pen)
    {
        var u = U;
        var list = new List<int>();
        for (int i = 0; i < u.Count; i++)
            if (u.Penned[i] == pen) list.Add(i);
        list.Sort((a, b) => u.Id[a].CompareTo(u.Id[b]));
        return list;
    }

    /// <summary>Percent of its pace unit i walks at: less while it is tired after a stampede, else 100.</summary>
    public int PaceOf(int i)
    {
        var u = U;
        if ((uint)i >= (uint)u.Count || u.Tired[i] == 0) return 100;
        return _sim.Content.Buildings[u.RunDef[i]].Pen?.Slow ?? 100;
    }

    /// <summary>The units of an order other than to a pen stop going into one.</summary>
    internal void CallOffPen(UnitsCommand c)
    {
        var u = U;
        foreach (int id in c.Units)
        {
            int i = u.IndexOf(id);
            if (i >= 0 && u.Owner[i] == c.PlayerId) u.PenFor[i] = 0;
        }
    }

    /// <summary>The pen's beasts are let out.</summary>
    internal void Apply(UnpenCommand c)
    {
        var pen = Buildings.Get(c.Building);
        if (pen == null || pen.Owner != c.PlayerId) return;
        foreach (int i in PennedIn(pen.Id)) U.Penned[i] = 0;
    }

    /// <summary>The pen's beasts burst out as a stampede along the line (see the top of this file).</summary>
    internal void Apply(StampedeCommand c)
    {
        var pen = Buildings.Get(c.Building);
        if (pen == null || pen.Owner != c.PlayerId || !pen.Active || pen.Def.Pen is not { } def) return;
        if (!World.InBounds(c.X1, 0, c.Z1) || !World.InBounds(c.X2, 0, c.Z2)) return;
        var beasts = PennedIn(pen.Id);
        if (beasts.Count == 0) return;
        var u = U;
        Fix64 ax = Fix64.FromInt(c.X1) + Fix64.Half, az = Fix64.FromInt(c.Z1) + Fix64.Half;
        Fix64 bx = Fix64.FromInt(c.X2) + Fix64.Half, bz = Fix64.FromInt(c.Z2) + Fix64.Half;
        // Which way the line runs: from its start to its end, or for a line of one column, from the pen to it.
        Fix64 dx = bx - ax, dz = bz - az;
        if (dx == Fix64.Zero && dz == Fix64.Zero)
        {
            dx = ax - (Fix64.FromInt(pen.X) + Fix64.FromRatio(pen.Def.SizeX, 2));
            dz = az - (Fix64.FromInt(pen.Z) + Fix64.FromRatio(pen.Def.SizeZ, 2));
        }
        Fix64 len = Fix64.Sqrt(dx * dx + dz * dz);
        Fix64 px = Fix64.Zero, pz = Fix64.Zero;
        if (len > Fix64.Zero)
        {
            px = -dz / len * StampedeSpread;
            pz = dx / len * StampedeSpread;
        }
        for (int k = 0; k < beasts.Count; k++)
        {
            int i = beasts[k];
            Fix64 side = k % 2 == 0 ? -Fix64.One : Fix64.One;
            u.Penned[i] = 0;
            u.PenFor[i] = 0;
            u.ClearJob(i);
            u.ClearCombat(i);
            u.ClearFormation(i);
            u.Order[i] = UnitOrder.Idle;
            u.RunStage[i] = 1;
            u.RunDef[i] = pen.Def.Index;
            u.RunAX[i] = ax + px * side;
            u.RunAZ[i] = az + pz * side;
            u.RunBX[i] = bx + px * side;
            u.RunBZ[i] = bz + pz * side;
            u.RunLeft[i] = Fix64.FromInt(def.Run);
            u.Tired[i] = 0;
            u.Blocked[i] = 0;
            _trampled.RemoveAll(t => t.Runner == u.Id[i]);
        }
    }

    /// <summary>
    /// Once a tick, before the units fight: beasts on their way into a pen are kept there once they stand in its yard, the
    /// penned stand at their places, the beasts of a lost pen go free, and tired beasts count down.
    /// </summary>
    private void StepPens()
    {
        var u = U;
        bool any = false;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.Tired[i] > 0) u.Tired[i]--;
            if (u.Penned[i] != 0)
            {
                if (Pen(u.Penned[i], u.Owner[i]) == null) u.Penned[i] = 0;
                else any = true;
                continue;
            }
            if (u.PenFor[i] == 0) continue;
            var pen = Pen(u.PenFor[i], u.Owner[i]);
            if (pen == null || u.Subdued[i] || u.Rout[i] > 0 || u.RunStage[i] != 0)
            {
                u.PenFor[i] = 0;
                continue;
            }
            int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt();
            if (pen.Covers(x, z) && Math.Abs(u.Y[i] - pen.Y) <= 1)
            {
                u.PenFor[i] = 0;
                if (PenCount(pen.Id) >= pen.Def.Pen!.Keeps) continue;
                u.Penned[i] = pen.Id;
                u.ClearJob(i);
                u.ClearCombat(i);
                u.ClearFormation(i);
                u.Order[i] = UnitOrder.Idle;
                any = true;
            }
            else if (u.Order[i] == UnitOrder.Idle) u.PenFor[i] = 0; // it stopped short (no way in)
        }
        if (!any) return;
        // Each pen's beasts stand in rows across its yard, a column in from the fence, lowest id first.
        var placed = new List<(int Pen, int Id, int I)>();
        for (int i = 0; i < u.Count; i++)
            if (u.Penned[i] != 0) placed.Add((u.Penned[i], u.Id[i], i));
        placed.Sort((a, b) => a.Pen != b.Pen ? a.Pen.CompareTo(b.Pen) : a.Id.CompareTo(b.Id));
        int k = 0, last = 0;
        foreach (var (penId, _, i) in placed)
        {
            if (penId != last) (k, last) = (0, penId);
            var pen = Buildings.Get(penId)!;
            var (sx, sz) = PenSpot(pen, k++);
            u.X[i] = sx;
            u.Z[i] = sz;
            u.Y[i] = pen.Y;
            u.FaceX[i] = Fix64.Zero;
            u.FaceZ[i] = Fix64.One;
        }
    }

    /// <summary>The finished beast pen with this id, if it is still <paramref name="owner"/>'s; else null.</summary>
    private Building? Pen(int id, int owner) =>
        Buildings.Get(id) is { Active: true, Def.Pen: not null } b && b.Owner == owner ? b : null;

    /// <summary>Where (columns) the k-th beast kept in a pen stands: rows two columns apart, a column in from the fence.</summary>
    public static (Fix64 X, Fix64 Z) PenSpot(Building pen, int k)
    {
        int across = Math.Max(1, pen.Def.SizeX - 2), rows = Math.Max(1, (pen.Def.SizeZ - 1) / 2);
        int col = k % across, row = k / across % rows;
        return (Fix64.FromInt(pen.X + 1 + col) + Fix64.Half, Fix64.FromInt(pen.Z + 1 + row * 2) + Fix64.Half);
    }

    /// <summary>
    /// Once a tick, after the grid is built and before the units fight: the running beasts run on (see the top of this
    /// file), trampling the enemies they meet and striking wooden buildings in their way.
    /// </summary>
    private void StepStampede()
    {
        var u = U;
        for (int i = 0; i < u.Count; i++)
        {
            if (u.RunStage[i] == 0) continue;
            if (u.Hp[i] <= 0 || u.Aboard[i] != 0 || u.Subdued[i]) { EndRun(i); continue; }
            var def = _sim.Content.Buildings[u.RunDef[i]].Pen!;
            Fix64 step = Fix64.FromRatio(def.Speed, Tps);
            Fix64 tx = u.RunStage[i] == 1 ? u.RunAX[i] : u.RunBX[i], tz = u.RunStage[i] == 1 ? u.RunAZ[i] : u.RunBZ[i];
            Fix64 dx = tx - u.X[i], dz = tz - u.Z[i];
            Fix64 dist = Fix64.Sqrt(dx * dx + dz * dz);
            bool arrive = dist <= step;
            Fix64 nx = arrive ? tx : u.X[i] + dx / dist * step, nz = arrive ? tz : u.Z[i] + dz / dist * step;
            if (dist > Fix64.Zero)
            {
                u.FaceX[i] = dx / dist;
                u.FaceZ[i] = dz / dist;
            }
            if (!Stride(i, nx, nz, def))
            {
                if (++u.Blocked[i] > SmashWait) EndRun(i);
                else Trample(i, def);
                continue;
            }
            u.Blocked[i] = 0;
            u.RunLeft[i] -= arrive ? dist : step;
            Trample(i, def);
            if (arrive && u.RunStage[i] == 1) u.RunStage[i] = 2;
            else if (arrive || u.RunLeft[i] <= Fix64.Zero) EndRun(i);
        }
    }

    /// <summary>
    /// Running beast i steps to (nx, nz): into another column only where it can stand there (a level up at most, any drop);
    /// a wooden building of an enemy's in the way is struck once, and the step taken if that cleared it. False if it cannot.
    /// </summary>
    private bool Stride(int i, Fix64 nx, Fix64 nz, PenDef def)
    {
        var u = U;
        int x = u.X[i].FloorToInt(), z = u.Z[i].FloorToInt(), y = u.Y[i];
        int cx = nx.FloorToInt(), cz = nz.FloorToInt();
        if (cx == x && cz == z)
        {
            u.X[i] = nx;
            u.Z[i] = nz;
            return true;
        }
        if (!World.InBounds(cx, y, cz)) return false;
        if (!Walk.TryStep(World, x, z, y, cx, cz, out int ny))
        {
            // What is in the way: a building of an enemy's, of wood, is struck once.
            var b = Buildings.At(cx, y, cz) ?? Buildings.At(cx, y + 1, cz);
            if (b == null || b.Owner == u.Owner[i] || !Hostile(u.Owner[i], b.Owner) || !Wooden(b.Shape.Wall)) return false;
            if (!Struck(u.Id[i], -b.Id))
            {
                Mark(u.Id[i], -b.Id);
                HitBuildingFrom(b, def.Smash, u.X[i], u.Z[i], u.Owner[i], undermine: false);
            }
            if (!Walk.TryStep(World, x, z, y, cx, cz, out ny)) return false;
        }
        u.X[i] = nx;
        u.Z[i] = nz;
        u.Y[i] = ny;
        return true;
    }

    /// <summary>True for a building material a stampede smashes through: palisade, planks, thatch or a watchtower's timber.</summary>
    public static bool Wooden(BlockType wall) => wall is BlockType.Palisade or BlockType.Planks or BlockType.Thatch or BlockType.Trestle;

    /// <summary>Running beast i tramples the enemy units it has come up to that it has not trampled yet in this run.</summary>
    private void Trample(int i, PenDef def)
    {
        var u = U;
        Near(u.X[i], u.Y[i], u.Z[i], TrampleReach, TrampleHeight, u.Owner[i], allies: false, _trample);
        foreach (int j in _trample)
        {
            if (j == i || u.Hp[j] <= 0 || u.Aboard[j] != 0 || Struck(u.Id[i], u.Id[j])) continue;
            var victim = Def(j);
            if (victim.IsBoat) continue;
            Mark(u.Id[i], u.Id[j]);
            Trampled++;
            if (def.Trample > 0 && !_sim.Sandbox.Invulnerable(u.Owner[j]))
            {
                _pending[j] += def.Trample;
                int w = j * Body.Parts + (int)BodyPart.Torso;
                u.Wounds[w] = (byte)Math.Min(255, u.Wounds[w] + def.Trample);
                if (_hitBy[j] == 0)
                {
                    Fix64 dx = u.X[j] - u.X[i], dz = u.Z[j] - u.Z[i];
                    _hitBy[j] = u.Id[i];
                    _hitFrom[j] = dx * dx + dz * dz;
                }
            }
            if (victim.Machine || def.DownTicks == 0) continue;
            u.Stunned[j] = Math.Max(u.Stunned[j], def.DownTicks);
            u.Cooldown[j] = Math.Max(u.Cooldown[j], def.DownTicks);
        }
    }

    private bool Struck(int runner, int victim) => _trampled.BinarySearch((runner, victim)) >= 0;

    private void Mark(int runner, int victim)
    {
        int at = _trampled.BinarySearch((runner, victim));
        if (at < 0) _trampled.Insert(~at, (runner, victim));
    }

    /// <summary>Beast i's run is over: it stops where it is, tired.</summary>
    private void EndRun(int i)
    {
        var u = U;
        int id = u.Id[i];
        _trampled.RemoveAll(t => t.Runner == id);
        u.RunStage[i] = 0;
        u.RunLeft[i] = Fix64.Zero;
        u.Blocked[i] = 0;
        u.Order[i] = UnitOrder.Idle;
        u.Tired[i] = _sim.Content.Buildings[u.RunDef[i]].Pen?.TiredTicks ?? 0;
    }

    /// <summary>Mixes the stampedes' own state into the fighting's hash.</summary>
    private void HashStampedes(Action<long> mix)
    {
        mix(_trampled.Count);
        foreach (var (r, v) in _trampled)
        {
            mix(r);
            mix(v);
        }
        mix(Trampled);
    }
}
