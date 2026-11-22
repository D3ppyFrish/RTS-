using System;
using System.Collections.Generic;
using Rts.Sim.Content;
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

    /// <summary>
    /// The building has just been put up again whole, bigger, in place of what stood there (see
    /// <see cref="UpgradeCommand"/>): the blocks cleared for it this tick were not knocked
    /// out of it, and nothing falls for their going.
    /// </summary>
    internal void Rebuilt(Building b)
    {
        _changed.RemoveAll(key =>
        {
            int x = (int)(key & 0xFFFFF), z = (int)((key >> 20) & 0xFFFFF), y = (int)(key >> 40);
            return b.Covers(x, z) && y >= b.Y - 1 && y <= b.Y + b.Def.Height;
        });
    }

    private static long Key(int x, int y, int z) => ((long)y << 40) | ((long)z << 20) | (long)x;

    /// <summary>
    /// What a hit by a unit of this type on this building is multiplied by: full for a role strong against
    /// structures, and for a blast on a thick wall, gate or tower; a quarter (<see cref="CombatRules.Structures"/>)
    /// for everyone else, and less again (<see cref="CombatRules.Thick"/>) for a blow struck close up at a thick
    /// one, where hacking at solid stone or timber gets little done.
    /// </summary>
    public Fix64 StructureMultiplier(UnitDef def, BuildingDef b)
    {
        if (b.Thick && def.Blast) return Fix64.One;
        var m = Rules.StructureMultiplier(def.Role);
        if (b.Thick && !def.IsRanged && !Rules.StrongAgainstStructures(def.Role)) m *= Rules.Thick;
        return m;
    }

    /// <summary>
    /// What one blow of <paramref name="attack"/> by a unit of this type deals to this building: the attack times
    /// <see cref="StructureMultiplier"/>, and on a gate times the type's <see cref="UnitDef.Gates"/> (a ram's blows
    /// break a gate's timbers sooner). At least 1.
    /// </summary>
    public int BuildingDamage(UnitDef def, BuildingDef b, int attack)
    {
        var damage = Fix64.FromInt(attack) * StructureMultiplier(def, b);
        if (b.Kind == BuildingKind.Gate) damage = damage * def.Gates / 100;
        return Math.Max(1, damage.RoundToInt());
    }

    /// <summary>
    /// The building takes a hit from unit i. Enough damage knocks out a block from the column nearest the
    /// unit: the top one, or with <paramref name="undermine"/> the bottom one, so the rest of the column falls.
    /// A <paramref name="blast"/> on a thick wall, gate or tower knocks out the block under the top one, and what
    /// stands on it and beside it topples away from the blast (see <see cref="Topple"/>).
    /// A site with nothing standing yet is wrecked by any hit.
    /// </summary>
    private void HitBuilding(Building b, int damage, int i, bool undermine, bool blast = false)
    {
        bool topple = blast && !undermine && b.Def.Thick;
        if (_sim.Sandbox.Invulnerable(b.Owner)) return;
        b.Damage += damage;
        _sim.Events.BuildingAttacked(b, U.Owner[i]);
        if (b.Def.Kind == BuildingKind.Totem) _sim.Cities.TotemHit(b, U.Owner[i]);
        while (b.Damage >= BlockInfo.MinToughness)
        {
            int k = Pick(b, U.X[i], U.Z[i], undermine, topple);
            if (k < 0)
            {
                Collapse(b);
                return;
            }
            int tough = b.Def.BlockToughness(k);
            if (b.Damage < tough) return;
            b.Damage -= tough;
            int ax = U.X[i].FloorToInt(), az = U.Z[i].FloorToInt();
            KnockOut(b, k, ax, az);
            if (!topple) continue;
            Topple(b, k, ax, az);
            return; // the rest of the blast went into what fell
        }
    }

    /// <summary>
    /// The block a hit from (x, z) knocks out: in the building's standing column nearest to that point (ties
    /// to the first in build order), the top block, or the bottom one when undermining, or for a blast the one
    /// under the top (the top itself when it stands alone). -1 if none stands.
    /// </summary>
    private int Pick(Building b, Fix64 x, Fix64 z, bool undermine, bool blast = false)
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
        if (!blast) return pick;
        int under = -1;
        for (int k = 0; k < b.Placed; k++)
        {
            var t = blocks[k];
            if (b.X + t.X != bestX || b.Z + t.Z != bestZ || t.Y >= blocks[pick].Y || World.Get(bestX, b.Y + t.Y, bestZ) != t.Block) continue;
            if (under < 0 || t.Y > blocks[under].Y) under = k;
        }
        return under >= 0 ? under : pick;
    }

    /// <summary>Most columns deep, the way a blast goes, that the thick wall (or tower) toppling from it reaches.</summary>
    public const int ToppleDepth = 6;

    /// <summary>
    /// A blast from (fromX, fromZ) has knocked block k out of the thick building b. What it held up topples away
    /// from the blast: the rest of its column above it, and the columns beside it (one each side, across the way
    /// the blast went) from its level up, each together with the columns behind it the way the blast went, as
    /// far as the thickness of the wall (or tower) goes, at most <see cref="ToppleDepth"/>. Only thick blocks of
    /// the same owner's buildings go. They come away as one piece, thrown up and outwards (see
    /// <see cref="World.PhysicsSystem.Topple"/>), and come down past the far side of the wall on whoever stands
    /// there.
    /// </summary>
    private void Topple(Building b, int k, int fromX, int fromZ)
    {
        var t = b.Def.Blocks[k];
        int hx = b.X + t.X, hy = b.Y + t.Y, hz = b.Z + t.Z;
        int ddx = hx - fromX, ddz = hz - fromZ;
        if (ddx == 0 && ddz == 0) ddx = 1;
        var (dx, dz) = Math.Abs(ddx) >= Math.Abs(ddz) ? (Math.Sign(ddx), 0) : (0, Math.Sign(ddz));
        int px = dz, pz = dx; // across the way the blast went
        _topple.Clear();
        for (int side = -1; side <= 1; side++)
        for (int along = 0; along < ToppleDepth; along++)
        {
            int x = hx + dx * along + px * side, z = hz + dz * along + pz * side;
            bool hit = side == 0 && along == 0;
            // The wall goes on this deep only where it stands at the blast's level.
            if (!hit && !Thick(b.Owner, x, hy, z)) break;
            for (int y = hit ? hy + 1 : hy; y < World.SizeY && Thick(b.Owner, x, y, z); y++) _topple.Add((x, y, z));
        }
        if (_topple.Count > 0) _sim.Physics.Topple(_topple, fromX, fromZ);
    }

    /// <summary>True if the block at (x, y, z) is a standing block of a thick building of the owner.</summary>
    private bool Thick(int owner, int x, int y, int z)
    {
        if (!World.InBounds(x, y, z)) return false;
        var w = Buildings.At(x, y, z);
        if (w == null || w.Owner != owner || !w.Def.Thick) return false;
        int k = w.Def.BlockAt(x - w.X, y - w.Y, z - w.Z);
        return k >= 0 && k < w.Placed && World.Get(x, y, z) == w.Def.Blocks[k].Block;
    }

    private readonly List<(int X, int Y, int Z)> _topple = new();

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
        _sim.Automation.Destroyed(b);
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
    /// The building's blocks standing on (x, y, z), now that it is empty, fall: the column of them above it,
    /// up through its room to the roof over it, drops (see <see cref="World.PhysicsSystem.Release"/>), and lands as rubble.
    /// </summary>
    private void Fall(int x, int y, int z)
    {
        if (BlockInfo.IsSolid(World.Get(x, y, z)) || y + 1 >= World.SizeY) return;
        var b = Buildings.At(x, y + 1, z);
        if (b == null || b.Def.Walkable) return;
        _column.Clear();
        int top = b.Y + b.Def.Height;
        for (int cy = y + 1; cy < World.SizeY; cy++)
        {
            var t = World.Get(x, cy, z);
            // Up through the building's room (open air under its roof) to the roof over it.
            if (t == BlockType.Air && cy < top) continue;
            if (!BlockInfo.IsBuilt(t) || t is BlockType.Road or BlockType.Farmland || Buildings.At(x, cy, z) != b) break;
            _column.Add((x, cy, z));
        }
        if (_column.Count > 0) _sim.Physics.Release(_column, x, z, 0, b.Def.IsWall);
    }

    private readonly List<(int X, int Y, int Z)> _column = new();
}
