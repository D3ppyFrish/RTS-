using System.Collections.Generic;
using Rts.Sim.Economy;

namespace Rts.Sim.Units;

/// <summary>
/// Buildings are made of blocks, so damage to them knocks out real blocks and opens real gaps. Hits on a
/// building add up; each time they reach the toughness of a block (see <see cref="BuildingDef.BlockToughness"/>)
/// one comes out of the column nearest the attacker: its top block, so walls are worn down where they are
/// hit, or for a sapper its bottom block, so the column falls. Knocked-out blocks fly off as debris. Any building
/// block left with nothing under it falls, with the blocks on it, whatever took its support away (a sapper's blow, a tunnel dug under the wall, a dig), and a
/// building that has lost half the toughness of what was built crumbles. What falls lands as rubble.
/// </summary>
public sealed partial class CombatSystem
{
    /// <summary>Most rounds of falling blocks worked through in one tick; the rest fall the next tick.</summary>
    public const int MaxFallRounds = 32;

    // Blocks that turned to air or water (packed, see Key), checked at the end of the tick: some may have been
    // filled again since.
    private readonly List<long> _changed = new();
    private readonly List<long> _round = new();
    private readonly List<Building> _hurt = new();

    private void OnBlockChanged(int x, int y, int z)
    {
        if (!BlockInfo.IsSolid(World.Get(x, y, z))) _changed.Add(Key(x, y, z));
    }

    private static long Key(int x, int y, int z) => ((long)y << 40) | ((long)z << 20) | (long)x;

    /// <summary>
    /// The building takes a hit from unit i. Enough damage knocks out a block from the column nearest the
    /// unit: the top one, or with <paramref name="undermine"/> the bottom one, so the rest of the column falls.
    /// A site with nothing standing yet is wrecked by any hit.
    /// </summary>
    private void HitBuilding(Building b, int damage, int i, bool undermine)
    {
        if (_sim.Sandbox.Invulnerable(b.Owner)) return;
        b.Damage += damage;
        _sim.Events.BuildingAttacked(b, U.Owner[i]);
        if (b.Def.Kind == BuildingKind.Totem) _sim.Cities.TotemHit(b, U.Owner[i]);
        while (b.Damage >= BlockInfo.MinToughness)
        {
            int k = Pick(b, U.X[i], U.Z[i], undermine);
            if (k < 0)
            {
                Collapse(b);
                return;
            }
            int tough = b.Def.BlockToughness(k);
            if (b.Damage < tough) return;
            b.Damage -= tough;
            KnockOut(b, k, U.X[i].FloorToInt(), U.Z[i].FloorToInt());
        }
    }

    /// <summary>
    /// The block a hit from (x, z) knocks out: in the building's standing column nearest to that point (ties
    /// to the first in build order), the top block, or the bottom one when undermining. -1 if none stands.
    /// </summary>
    private int Pick(Building b, Fix64 x, Fix64 z, bool undermine)
    {
        var blocks = b.Def.Blocks;
        int bestX = 0, bestZ = 0;
        bool found = false;
        Fix64 bestD = Fix64.MaxValue;
        for (int k = 0; k < b.Placed; k++)
        {
            var t = blocks[k];
            int bx = b.X + t.X, bz = b.Z + t.Z;
            if (World.Get(bx, b.Y + t.Y, bz) != t.Block) continue;
            Fix64 dx = Fix64.FromInt(bx) + Fix64.Half - x, dz = Fix64.FromInt(bz) + Fix64.Half - z;
            Fix64 d = dx * dx + dz * dz;
            if (d >= bestD) continue;
            bestD = d;
            (bestX, bestZ) = (bx, bz);
            found = true;
        }
        if (!found) return -1;
        int pick = -1;
        for (int k = 0; k < b.Placed; k++)
        {
            var t = blocks[k];
            if (b.X + t.X != bestX || b.Z + t.Z != bestZ || World.Get(bestX, b.Y + t.Y, bestZ) != t.Block) continue;
            if (pick < 0 || (undermine ? t.Y < blocks[pick].Y : t.Y > blocks[pick].Y)) pick = k;
        }
        return pick;
    }

    /// <summary>Speed (64ths of a block per tick) a knocked-out block flies off at, away from the blow.</summary>
    public const int KnockSpeed = 12;

    /// <summary>
    /// Block k is knocked out of the building by a blow from (fromX, fromZ): it flies off away from the blow
    /// and comes down as rubble (see <see cref="World.PhysicsSystem"/>). A wall's or gate's rubble spreads
    /// out, so the gap it leaves can be walked through. A field's block just turns to soil.
    /// </summary>
    private void KnockOut(Building b, int k, int fromX, int fromZ)
    {
        var t = b.Def.Blocks[k];
        int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
        if (t.Y < 0) World.Set(x, y, z, BlockType.Soil);
        else _sim.Physics.Release(new[] { (x, y, z) }, fromX, fromZ, KnockSpeed, b.Def.IsWall);
        _changed.Add(Key(x, y, z)); // a field's block turns to soil, which the change event does not report
    }

    /// <summary>
    /// The building falls: what is left of it crumbles (see <see cref="World.PhysicsSystem.Crumble"/>), and it
    /// goes, with its goods and queue.
    /// </summary>
    private void Collapse(Building b)
    {
        _sim.Physics.Crumble(b);
        Buildings.Remove(b);
    }

    /// <summary>
    /// Works through the blocks that went this tick, round by round: each counts against the building it
    /// belonged to, and a building block it held up falls (and is checked in the next round). Then buildings
    /// that have lost half of what was built collapse.
    /// </summary>
    private void DrainBlocks()
    {
        for (int round = 0; _changed.Count > 0 && round < MaxFallRounds; round++)
        {
            _round.Clear();
            _round.AddRange(_changed);
            _changed.Clear();
            _round.Sort();
            long last = -1;
            foreach (long key in _round)
            {
                if (key == last) continue;
                last = key;
                int x = (int)(key & 0xFFFFF), z = (int)((key >> 20) & 0xFFFFF), y = (int)(key >> 40);
                Lose(x, y, z);
                Fall(x, y, z);
            }
            if (_changed.Count > 0 || _hurt.Count == 0) continue;
            // Nothing more is falling: collapse what has lost too much (which may set more falling).
            _hurt.Sort((a, c) => a.Id.CompareTo(c.Id));
            Building? previous = null;
            foreach (var b in _hurt)
            {
                if (b == previous) continue;
                previous = b;
                if (Buildings.Get(b.Id) == b && b.Lost * 2 >= b.Built) Collapse(b);
            }
            _hurt.Clear();
        }
    }

    /// <summary>If the block at (x, y, z) was one of a building's placed blocks and is gone, it counts against it.</summary>
    private void Lose(int x, int y, int z)
    {
        var b = Buildings.At(x, y, z);
        if (b == null) return;
        int k = b.Def.BlockAt(x - b.X, y - b.Y, z - b.Z);
        if (k < 0 || k >= b.Placed) return;
        var t = b.Def.Blocks[k];
        if (World.Get(x, y, z) == t.Block) return;
        b.Gone ??= new bool[b.Def.Blocks.Count];
        if (b.Gone[k]) return;
        b.Gone[k] = true;
        b.Lost += b.Def.BlockToughness(k);
        _hurt.Add(b);
    }

    /// <summary>
    /// The building's blocks standing on (x, y, z), now that it is empty, fall: the column of them above it
    /// drops as one piece (see <see cref="World.PhysicsSystem.Release"/>), and lands as rubble.
    /// </summary>
    private void Fall(int x, int y, int z)
    {
        if (BlockInfo.IsSolid(World.Get(x, y, z)) || y + 1 >= World.SizeY) return;
        var b = Buildings.At(x, y + 1, z);
        if (b == null || b.Def.Walkable) return;
        _column.Clear();
        for (int cy = y + 1; cy < World.SizeY; cy++)
        {
            var t = World.Get(x, cy, z);
            if (!BlockInfo.IsBuilt(t) || t is BlockType.Road or BlockType.Farmland || Buildings.At(x, cy, z) != b) break;
            _column.Add((x, cy, z));
        }
        if (_column.Count > 0) _sim.Physics.Release(_column, x, z, 0, b.Def.IsWall);
    }

    private readonly List<(int X, int Y, int Z)> _column = new();
}
