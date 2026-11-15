using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>
/// The map's one dusk tree (see <see cref="World.RegionGenerator"/>): a gnarled, leafless tree in the hills,
/// guarded by the Dusk Warden (<see cref="Content.ContentDb.DuskWarden"/>), a wild monster in a lair of its own
/// beside it (see <see cref="MonsterSystem"/>). Nobody can cut it or dig it.
///
/// Once the warden is gone (killed, or beaten by Orcs, who can then recruit it), what a player can do to the
/// tree depends on their faction (<see cref="Content.FactionDef.DuskTree"/>), with <see cref="DuskTreeCommand"/>:
/// - Heal (the Settlers): a unit that heals (<see cref="UnitDef.Heal"/>, the battle priest) walks to the tree and
///   tends it, standing within <see cref="TendRange"/> of the trunk, for <see cref="TendTicks"/>; an order of any
///   other kind, or a fight, stops it. The first to finish heals the tree, once a match: its bare branches grow a
///   violet canopy, and the healer gets the Dusk Staff.
/// - Raze (the Orcs): their fighting units walk to the tree and hack at it, together, until it has had
///   <see cref="RazeTicks"/> of work; then it comes down as a line of tree rubble (wood for whoever clears it),
///   and nobody can get the staff. A healed tree cannot be razed.
///
/// The Dusk Staff gives its bearer <see cref="StaffHp"/> more hit points, heals <see cref="StaffHeal"/> more
/// each time, and lets it use Dusk Bloom (<see cref="BloomCommand"/>): every ally within
/// <see cref="BloomRange"/> blocks, the bearer too, gets back <see cref="BloomHeal"/> hit points, after which
/// it can bloom again in <see cref="BloomCooldownTicks"/>. The staff is gone with its bearer.
/// All numbers are starting guesses to be tuned in playtests.
/// </summary>
public sealed class DuskTreeSystem
{
    /// <summary>How near the trunk (blocks, across) a unit tends the tree from.</summary>
    public const int TendRange = 3;
    /// <summary>How long (ticks) a unit tends the tree to heal it: 10 seconds.</summary>
    public const int TendTicks = 10 * Simulation.TicksPerSecond;
    /// <summary>Hit points the staff adds to its bearer's.</summary>
    public const int StaffHp = 20;
    /// <summary>Hit points the staff adds to each of its bearer's heals.</summary>
    public const int StaffHeal = 2;
    /// <summary>How far (blocks) Dusk Bloom reaches.</summary>
    public const int BloomRange = 6;
    /// <summary>Hit points Dusk Bloom gives back to each ally in reach.</summary>
    public const int BloomHeal = 25;
    /// <summary>Ticks after a bloom before the next: 45 seconds.</summary>
    public const int BloomCooldownTicks = 45 * Simulation.TicksPerSecond;
    /// <summary>Ticks of one unit's work (units hacking together add up) to bring the tree down: 30 seconds.</summary>
    public const int RazeTicks = 30 * Simulation.TicksPerSecond;
    /// <summary>How far (columns) the healed tree's leaves reach from its wood.</summary>
    private const int LeafReach = 1;

    private readonly Simulation _sim;
    // Units tending the tree (ids, in the order they started) and the column each walks to.
    private readonly List<Tender> _tenders = new();
    private readonly List<int> _near = new();

    private sealed class Tender
    {
        public int Id;
        public int X, Z, Y;
        public int Ticks;
        public bool Raze;
    }

    public DuskTreeSystem(Simulation sim)
    {
        _sim = sim;
    }

    private UnitStore U => _sim.Units;
    private VoxelWorld World => _sim.World;

    /// <summary>The map has a dusk tree (see <see cref="X"/>).</summary>
    public bool Exists { get; private set; }
    /// <summary>The tree's trunk column and the level of its lowest trunk block (the ground is just under it).</summary>
    public int X { get; private set; } = -1;
    public int Y { get; private set; } = -1;
    public int Z { get; private set; } = -1;
    /// <summary>The Dusk Warden's lair id, or 0 if the tree has no warden.</summary>
    public int Lair { get; private set; }
    /// <summary>The tree has been healed and wears its canopy.</summary>
    public bool Healed { get; private set; }
    /// <summary>The player who healed it, or -1.</summary>
    public int HealedBy { get; private set; } = -1;
    /// <summary>The tree was hacked down: it can no longer be healed.</summary>
    public bool Razed { get; private set; }
    /// <summary>The player whose units hacked it down, or -1.</summary>
    public int RazedBy { get; private set; } = -1;
    /// <summary>Ticks of work done on hacking the tree down (see <see cref="RazeTicks"/>).</summary>
    public int RazeWork { get; private set; }
    /// <summary>The unit (id) bearing the Dusk Staff, or 0 if nobody does (yet, or any more).</summary>
    public int StaffBearer { get; private set; }
    /// <summary>The tick the staff's bearer can next use Dusk Bloom.</summary>
    public long BloomReady { get; private set; }
    /// <summary>The tick of the last Dusk Bloom, and where it was (for drawing it), or -1.</summary>
    public long LastBloom { get; private set; } = -1;
    public (Fix64 X, int Y, Fix64 Z) LastBloomAt { get; private set; }

    /// <summary>The warden is gone: the tree can be healed.</summary>
    public bool Unguarded => Exists && !Razed && (Lair == 0 || _sim.Monsters.GetLair(Lair) is not { } l || l.Cleared);

    /// <summary>Unit i bears the Dusk Staff.</summary>
    public bool HasStaff(int i) => StaffBearer != 0 && i >= 0 && U.Id[i] == StaffBearer;

    /// <summary>Units (ids) tending the tree, in the order they started.</summary>
    public IEnumerable<int> Tending
    {
        get
        {
            foreach (var t in _tenders) yield return t.Id;
        }
    }

    /// <summary>True if the unit (id) is hacking the tree down rather than healing it.</summary>
    public bool Razing(int id)
    {
        foreach (var t in _tenders)
            if (t.Id == id) return t.Raze;
        return false;
    }

    /// <summary>How far along (ticks of <see cref="TendTicks"/>) the unit (id) is with tending the tree; 0 if it is not.</summary>
    public int TendProgress(int id)
    {
        foreach (var t in _tenders)
            if (t.Id == id) return t.Ticks;
        return 0;
    }

    public ulong ComputeHash()
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(Exists ? 1 : 0); Mix(X); Mix(Y); Mix(Z); Mix(Lair);
        Mix(Healed ? 1 : 0); Mix(HealedBy); Mix(Razed ? 1 : 0); Mix(RazedBy); Mix(RazeWork); Mix(StaffBearer); Mix(BloomReady); Mix(LastBloom);
        Mix(_tenders.Count);
        foreach (var t in _tenders)
        {
            Mix(t.Id); Mix(t.X); Mix(t.Y); Mix(t.Z); Mix(t.Ticks); Mix(t.Raze ? 1 : 0);
        }
        return h;
    }

    // --- Setup -------------------------------------------------------------------------------------

    /// <summary>
    /// Match setup: finds the map's dusk tree (the lowest <see cref="BlockType.DuskWood"/> with ground under it,
    /// the first by z then x) and puts the Dusk Warden's lair two columns off its trunk. Call with the lairs.
    /// </summary>
    public void Place()
    {
        if (!Find(World, out int x, out int y, out int z)) return;
        (Exists, X, Y, Z) = (true, x, y, z);
        if (_sim.Content.DuskWarden is not { } warden) return;
        foreach (var (dx, dz) in new[] { (2, 0), (-2, 0), (0, 2), (0, -2) })
            if (_sim.Monsters.Place(warden, x + dx, z + dz, near: y) is { } lair)
            {
                Lair = lair.Id;
                return;
            }
    }

    /// <summary>The dusk tree's trunk foot in the world, if there is one.</summary>
    public static bool Find(VoxelWorld w, out int x, out int y, out int z)
    {
        for (z = 0; z < w.SizeZ; z++)
        for (x = 0; x < w.SizeX; x++)
        {
            int top = w.SurfaceHeight(x, z);
            for (y = 1; y <= top; y++)
            {
                if (w.Get(x, y, z) != BlockType.DuskWood || w.Get(x, y - 1, z) == BlockType.DuskWood) continue;
                // The trunk: the column whose wood stands tallest from the ground.
                int t = y;
                while (w.Get(x, t + 1, z) == BlockType.DuskWood) t++;
                if (t - y >= 4) return true;
            }
        }
        x = y = z = -1;
        return false;
    }

    // --- Healing the tree --------------------------------------------------------------------------

    /// <summary>What the player's faction can do to the tree.</summary>
    public Content.DuskTreeUse UseFor(int player) =>
        player >= 0 && player != Simulation.WildPlayer ? _sim.Economy.Faction(player).DuskTree : Content.DuskTreeUse.None;

    /// <summary>
    /// True if unit i can work on the tree as its owner's faction does: heal it (a unit that heals) or hack it
    /// down (a unit that fights); a player's unit, not beaten or running away.
    /// </summary>
    public bool CanWork(int i)
    {
        var u = U;
        if (i < 0 || u.Owner[i] == Simulation.WildPlayer || u.Subdued[i] || u.Rout[i] > 0 || u.Town[i] != 0) return false;
        var def = _sim.Content.Units[u.Def[i]];
        return UseFor(u.Owner[i]) switch
        {
            Content.DuskTreeUse.Heal => def.Heal > 0,
            Content.DuskTreeUse.Raze => def.CanAttack,
            _ => false,
        };
    }

    /// <summary>True if the player can set these units (indexes) to work on the tree now.</summary>
    public bool CanUse(int player, IEnumerable<int> units)
    {
        if (!Unguarded || Healed) return false;
        foreach (int i in units)
            if (i >= 0 && U.Owner[i] == player && CanWork(i)) return true;
        return false;
    }

    /// <summary>The player's units that can work on the tree walk to it and start: healing it, or hacking it down.</summary>
    internal void Apply(DuskTreeCommand c)
    {
        if (!Unguarded || Healed) return;
        bool raze = UseFor(c.PlayerId) == Content.DuskTreeUse.Raze;
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!CanWork(i) || IsTender(U.Id[i])) continue;
            if (!Spot(i, out int sx, out int sy, out int sz)) continue;
            U.ClearJob(i);
            U.ClearCombat(i);
            U.ClearFormation(i);
            _tenders.Add(new Tender { Id = U.Id[i], X = sx, Y = sy, Z = sz, Raze = raze });
            if (!InReach(i)) _sim.SendUnit(i, sx, sz, sy);
            else U.Order[i] = UnitOrder.Idle;
        }
    }

    /// <summary>The player's units among the command's stop working on the tree (they were given another order).</summary>
    internal void Release(Simulation sim, UnitsCommand c)
    {
        if (_tenders.Count == 0) return;
        foreach (int i in sim.OwnedUnits(c))
            _tenders.RemoveAll(t => t.Id == U.Id[i]);
    }

    private bool IsTender(int id)
    {
        foreach (var t in _tenders)
            if (t.Id == id) return true;
        return false;
    }

    /// <summary>Where unit i stands to tend the tree: the open column two from the trunk nearest it.</summary>
    private bool Spot(int i, out int x, out int y, out int z)
    {
        var u = U;
        int ux = u.X[i].FloorToInt(), uz = u.Z[i].FloorToInt();
        int best = int.MaxValue;
        x = y = z = -1;
        for (int dz = -2; dz <= 2; dz++)
        for (int dx = -2; dx <= 2; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != 2) continue;
            int cx = X + dx, cz = Z + dz;
            int cy = Walk.NearestStandLevel(World, cx, cz, Y, MonsterSystem.GuardHeight);
            if (cy < 0) continue;
            int d = (cx - ux) * (cx - ux) + (cz - uz) * (cz - uz);
            if (d >= best) continue;
            best = d;
            (x, y, z) = (cx, cy, cz);
        }
        return best != int.MaxValue;
    }

    /// <summary>Unit i is near enough the trunk to tend it.</summary>
    private bool InReach(int i)
    {
        var u = U;
        Fix64 dx = u.X[i] - (Fix64.FromInt(X) + Fix64.Half), dz = u.Z[i] - (Fix64.FromInt(Z) + Fix64.Half);
        Fix64 r = Fix64.FromInt(TendRange) + Fix64.Half;
        return dx * dx + dz * dz <= r * r && Math.Abs(u.Y[i] - Y) <= MonsterSystem.GuardHeight;
    }

    /// <summary>
    /// After the monsters: tenders walk to the tree and tend it; one that has another order, is fighting, can no
    /// longer tend, or stopped short of the tree gives up. The first to tend long enough heals the tree.
    /// </summary>
    public void Step()
    {
        Forget();
        if (!Exists || _tenders.Count == 0) return;
        var u = U;
        for (int k = 0; k < _tenders.Count; k++)
        {
            var t = _tenders[k];
            int i = u.IndexOf(t.Id);
            bool walking = i >= 0 && u.Order[i] == UnitOrder.Move && u.OrderX[i] == t.X && u.OrderZ[i] == t.Z;
            bool stays = i >= 0 && CanWork(i) && !Healed && !Razed && u.Target[i] == 0 && u.Job[i] == UnitJob.None
                && (walking || (u.Order[i] == UnitOrder.Idle && InReach(i)));
            if (!stays)
            {
                _tenders.RemoveAt(k--);
                continue;
            }
            if (walking) continue;
            t.Ticks++;
            if (t.Raze && ++RazeWork >= RazeTicks)
            {
                Raze(i);
                _tenders.Clear();
                return;
            }
            if (!t.Raze && t.Ticks >= TendTicks)
            {
                Heal(i);
                _tenders.Clear();
                return;
            }
        }
    }

    /// <summary>Unit i has healed the tree: its branches grow leaves and the unit takes up the Dusk Staff.</summary>
    private void Heal(int i)
    {
        var u = U;
        Healed = true;
        HealedBy = u.Owner[i];
        GrowCanopy();
        StaffBearer = u.Id[i];
        BloomReady = _sim.Tick;
        u.Hp[i] = Math.Min(_sim.MaxHpOf(i), u.Hp[i] + StaffHp);
    }

    /// <summary>
    /// Unit i's side hacked the tree down: every block of it is gone, and it lies as a line of tree rubble along
    /// the ground away from unit i (as a felled tree does, see <see cref="Economy.Harvest.LayFelled"/>).
    /// </summary>
    private void Raze(int i)
    {
        var u = U;
        var w = World;
        Razed = true;
        RazedBy = u.Owner[i];
        int logs = 0;
        for (int y = Y; y <= Y + 16; y++)
        for (int z = Z - 6; z <= Z + 6; z++)
        for (int x = X - 6; x <= X + 6; x++)
        {
            if (!w.InBounds(x, y, z) || !BlockInfo.IsDuskTree(w.Get(x, y, z))) continue;
            w.Set(x, y, z, BlockType.Air);
            if (x == X && z == Z) logs++;
        }
        int dx = X - u.X[i].FloorToInt(), dz = Z - u.Z[i].FloorToInt();
        (int dirX, int dirZ) = Math.Abs(dx) >= Math.Abs(dz) ? (dx >= 0 ? 1 : -1, 0) : (0, dz >= 0 ? 1 : -1);
        Economy.Harvest.LayFelled(w, X, Y, Z, logs, dirX, dirZ, new List<(int X, int Y, int Z)>());
    }

    /// <summary>
    /// Leaves round every branch of the tree (the wood off the trunk column, and the trunk's top) and a dome
    /// over its crown, in open air only.
    /// </summary>
    private void GrowCanopy()
    {
        var w = World;
        int r = 6, top = Y + 16;
        var wood = new List<(int X, int Y, int Z)>();
        for (int y = Y + 4; y <= top; y++)
        for (int z = Z - r; z <= Z + r; z++)
        for (int x = X - r; x <= X + r; x++)
            if (w.Get(x, y, z) == BlockType.DuskBranch || (x == X && z == Z && w.Get(x, y, z) == BlockType.DuskWood && w.Get(x, y + 1, z) != BlockType.DuskWood))
                wood.Add((x, y, z));
        foreach (var (x, y, z) in wood)
            for (int dy = -LeafReach; dy <= LeafReach + 1; dy++)
            for (int dz = -LeafReach; dz <= LeafReach; dz++)
            for (int dx = -LeafReach; dx <= LeafReach; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dz) + Math.Abs(dy) > 2) continue;
                int lx = x + dx, ly = y + dy, lz = z + dz;
                if (ly < Y + 4 || !w.InBounds(lx, ly, lz) || w.Get(lx, ly, lz) != BlockType.Air) continue;
                w.Set(lx, ly, lz, BlockType.DuskLeaves);
            }
    }

    // --- The Dusk Staff ------------------------------------------------------------------------------

    /// <summary>Hit points unit i's heals give, with the staff's.</summary>
    public int HealOf(int i) => _sim.Content.Units[U.Def[i]].Heal + (HasStaff(i) ? StaffHeal : 0);

    /// <summary>The bearer can use Dusk Bloom now.</summary>
    public bool CanBloom(int i) => HasStaff(i) && _sim.Tick >= BloomReady && U.Rout[i] == 0 && !U.Subdued[i];

    /// <summary>The staff's bearer, if among the player's units and ready, heals every ally near it.</summary>
    internal void Apply(BloomCommand c)
    {
        foreach (int i in _sim.OwnedUnits(c))
        {
            if (!CanBloom(i)) continue;
            Bloom(i);
            return;
        }
    }

    private void Bloom(int i)
    {
        var u = U;
        _sim.Combat.Allies(u.X[i], u.Y[i], u.Z[i], Fix64.FromInt(BloomRange), CombatSystem.HealHeight, u.Owner[i], _near);
        _near.Sort((a, b) => u.Id[a].CompareTo(u.Id[b]));
        foreach (int j in _near)
        {
            int max = _sim.MaxHpOf(j);
            if (u.Hp[j] >= max || u.Subdued[j]) continue;
            int was = u.Hp[j];
            u.Hp[j] = Math.Min(max, u.Hp[j] + BloomHeal);
            _sim.Combat.Mend(j, u.Hp[j] - was, u.Hp[j] >= max);
        }
        BloomReady = _sim.Tick + BloomCooldownTicks;
        LastBloom = _sim.Tick;
        LastBloomAt = (u.X[i], u.Y[i], u.Z[i]);
    }

    /// <summary>The staff is gone with its bearer.</summary>
    private void Forget()
    {
        if (StaffBearer != 0 && U.IndexOf(StaffBearer) < 0) StaffBearer = 0;
    }
}
