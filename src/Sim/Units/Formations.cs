using System;
using System.Collections.Generic;

namespace Rts.Sim.Units;

/// <summary>
/// Where each unit of a formation move stands (see <see cref="FormationMoveCommand"/>). Plain geometry on
/// fixed-point numbers, so every machine plans the same places; the simulation then snaps each place to
/// ground a unit can stand on.
///
/// A line: the units spread out along it, facing forward from it (away from where the group stands now),
/// in ranks one block apart. Melee troops take the front ranks, ranged troops the ranks behind them, and
/// units that do not fight the back. Within a rank, units keep their left-to-right order, so nobody
/// crosses the line to get to their place.
///
/// Keeping the shape: each unit keeps its place relative to the group's middle. A group spread wider than
/// <see cref="MaxKeepSpread"/> (picked from all over the map) forms up in a block instead, facing the way
/// it moves.
/// </summary>
public static class Formations
{
    /// <summary>Gap between neighbours in a rank, and between ranks (blocks).</summary>
    public static readonly Fix64 Spacing = Fix64.One;
    /// <summary>A group with a unit farther than this (blocks) from its middle forms up in a block rather than keeping its shape.</summary>
    public static readonly Fix64 MaxKeepSpread = Fix64.FromInt(10);

    /// <summary>One unit to place: where it stands, what it is, and its id (for ties).</summary>
    public readonly record struct Member(int Id, Fix64 X, Fix64 Z, FormationRank Rank);

    /// <summary>A unit's place in the formation, and which way the formation faces (a unit vector, or zero).</summary>
    public readonly record struct Slot(int Id, Fix64 X, Fix64 Z);

    /// <summary>The places for the members along the line from (x1, z1) to (x2, z2), in the members' order.</summary>
    public static Slot[] Line(IReadOnlyList<Member> members, Fix64 x1, Fix64 z1, Fix64 x2, Fix64 z2, out Fix64 faceX, out Fix64 faceZ)
    {
        Fix64 dx = x2 - x1, dz = z2 - z1;
        Fix64 len = Fix64.Sqrt(dx * dx + dz * dz);
        if (len == Fix64.Zero) throw new ArgumentException("A formation line needs two different ends");
        dx /= len;
        dz /= len;
        // Forward is across the line, away from the group: the units walk up to the line and face on.
        var (cx, cz) = Middle(members);
        Fix64 mx = (x1 + x2) / 2, mz = (z1 + z2) / 2;
        faceX = -dz;
        faceZ = dx;
        if ((mx - cx) * faceX + (mz - cz) * faceZ < Fix64.Zero)
        {
            faceX = -faceX;
            faceZ = -faceZ;
        }
        return Ranks(members, mx, mz, dx, dz, len, faceX, faceZ);
    }

    /// <summary>
    /// The places for the members when the group moves to (x, z) keeping its shape, in the members' order.
    /// Faces the way the group moves (zero if it does not move).
    /// </summary>
    public static Slot[] KeepShape(IReadOnlyList<Member> members, Fix64 x, Fix64 z, out Fix64 faceX, out Fix64 faceZ)
    {
        var (cx, cz) = Middle(members);
        Fix64 mx = x - cx, mz = z - cz;
        Fix64 move = Fix64.Sqrt(mx * mx + mz * mz);
        faceX = move == Fix64.Zero ? Fix64.Zero : mx / move;
        faceZ = move == Fix64.Zero ? Fix64.Zero : mz / move;
        bool spread = false;
        foreach (var m in members)
        {
            Fix64 ox = m.X - cx, oz = m.Z - cz;
            if (Fix64.Abs(ox) > MaxKeepSpread || Fix64.Abs(oz) > MaxKeepSpread || ox * ox + oz * oz > MaxKeepSpread * MaxKeepSpread)
                spread = true;
        }
        if (spread)
        {
            // Too scattered to keep: a block about twice as wide as it is deep, facing the way it goes.
            if (move == Fix64.Zero) (faceX, faceZ) = (Fix64.Zero, Fix64.One);
            int perRank = 1;
            while (perRank * perRank < 2 * members.Count) perRank++;
            Fix64 width = Spacing * Math.Max(1, perRank - 1);
            return Ranks(members, x, z, faceZ, -faceX, width, faceX, faceZ, perRank);
        }
        var slots = new Slot[members.Count];
        for (int k = 0; k < members.Count; k++)
            slots[k] = new Slot(members[k].Id, members[k].X + mx, members[k].Z + mz);
        return slots;
    }

    /// <summary>
    /// Ranks centred on (mx, mz), running along (dx, dz) for <paramref name="len"/> blocks, the first on the
    /// line and the rest behind it (against the facing). Units per rank: as many as fit at
    /// <see cref="Spacing"/> (or <paramref name="perRank"/>); a group that fits in one rank spreads along all of it.
    /// </summary>
    private static Slot[] Ranks(IReadOnlyList<Member> members, Fix64 mx, Fix64 mz, Fix64 dx, Fix64 dz, Fix64 len,
        Fix64 faceX, Fix64 faceZ, int perRank = 0)
    {
        int n = members.Count;
        if (perRank <= 0) perRank = (len / Spacing).FloorToInt() + 1;
        perRank = Math.Max(1, Math.Min(perRank, n));
        // A full rank spans the line; one that is not full is centred with the same gaps.
        Fix64 gap = perRank <= 1 ? Fix64.Zero : len / (perRank - 1);

        var slots = new Slot[n];
        var order = new List<int>(n);
        int rank = 0;
        for (var r = FormationRank.Melee; r <= FormationRank.Support; r++)
        {
            order.Clear();
            for (int k = 0; k < n; k++)
                if (members[k].Rank == r) order.Add(k);
            if (order.Count == 0) continue;
            // The most forward of the class take its front rank.
            order.Sort((a, b) =>
            {
                int c = Ahead(members[b], faceX, faceZ).CompareTo(Ahead(members[a], faceX, faceZ));
                return c != 0 ? c : members[a].Id.CompareTo(members[b].Id);
            });
            for (int start = 0; start < order.Count; start += perRank, rank++)
            {
                int count = Math.Min(perRank, order.Count - start);
                var row = order.GetRange(start, count);
                // Left to right along the line, as they stand now.
                row.Sort((a, b) =>
                {
                    int c = Along(members[a], dx, dz).CompareTo(Along(members[b], dx, dz));
                    return c != 0 ? c : members[a].Id.CompareTo(members[b].Id);
                });
                Fix64 back = Spacing * rank;
                for (int j = 0; j < count; j++)
                {
                    // j - (count - 1) / 2 places along, in halves to stay exact.
                    Fix64 along = gap * (2 * j - (count - 1)) / 2;
                    slots[row[j]] = new Slot(members[row[j]].Id, mx + dx * along - faceX * back, mz + dz * along - faceZ * back);
                }
            }
        }
        return slots;
    }

    private static Fix64 Ahead(Member m, Fix64 faceX, Fix64 faceZ) => m.X * faceX + m.Z * faceZ;
    private static Fix64 Along(Member m, Fix64 dx, Fix64 dz) => m.X * dx + m.Z * dz;

    private static (Fix64 X, Fix64 Z) Middle(IReadOnlyList<Member> members)
    {
        Fix64 x = Fix64.Zero, z = Fix64.Zero;
        foreach (var m in members)
        {
            x += m.X;
            z += m.Z;
        }
        return members.Count == 0 ? (x, z) : (x / members.Count, z / members.Count);
    }
}

/// <summary>Which ranks of a formation a unit takes: melee in front, then ranged, then units that do not fight.</summary>
public enum FormationRank : byte
{
    Melee,
    Ranged,
    Support,
}
