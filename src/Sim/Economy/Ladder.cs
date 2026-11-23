namespace Rts.Sim.Economy;

/// <summary>
/// A siege ladder (<see cref="LadderCommand"/>): a column of rungs (<see cref="BlockType.Ladder"/>) raised in the foot
/// column (<see cref="X"/>, <see cref="Z"/>) against the face of a wall, a tower or a cliff in the column beside it
/// (<see cref="FaceX"/>, <see cref="FaceZ"/>), from the ground (<see cref="Foot"/>) up to a level under the top of the
/// face (<see cref="Top"/>, where units stand on it). Workers and sappers raise it a rung at a time, from the bottom.
/// Once it is up, units at its foot climb it onto the top, and anyone up there climbs down it, the enemy too. An enemy
/// on the top beside it, with none of the ladder's side there, throws it down (see <see cref="EconomySystem.StepLadders"/>).
/// </summary>
public sealed class Ladder
{
    public int Id { get; }
    public int Owner { get; }
    /// <summary>The foot column, where the rungs go.</summary>
    public int X { get; }
    public int Z { get; }
    /// <summary>The column it leans on.</summary>
    public int FaceX { get; }
    public int FaceZ { get; }
    /// <summary>The level units stand at at its foot: its bottom rung's.</summary>
    public int Foot { get; }
    /// <summary>The level units stand at on top of what it leans on: one over its top rung.</summary>
    public int Top { get; }
    /// <summary>Rungs raised so far, from the bottom.</summary>
    public int Built { get; internal set; }
    /// <summary>Ticks an enemy has held the top beside it, unopposed; it is thrown down at <see cref="EconomySystem.LadderPushTicks"/>.</summary>
    public int Push { get; internal set; }

    internal Ladder(int id, int owner, int x, int z, int faceX, int faceZ, int foot, int top)
    {
        Id = id;
        Owner = owner;
        X = x;
        Z = z;
        FaceX = faceX;
        FaceZ = faceZ;
        Foot = foot;
        Top = top;
    }

    /// <summary>How many rungs it has when it is up.</summary>
    public int Rungs => Top - Foot;

    /// <summary>True once every rung is in: units climb it.</summary>
    public bool Done => Built >= Rungs;

    internal void Mix(ref ulong h)
    {
        void M(ref ulong hh, long v) => hh = (hh ^ (ulong)v) * 1099511628211UL;
        M(ref h, Id);
        M(ref h, Owner);
        M(ref h, X);
        M(ref h, Z);
        M(ref h, FaceX);
        M(ref h, FaceZ);
        M(ref h, Foot);
        M(ref h, Top);
        M(ref h, Built);
        M(ref h, Push);
    }
}
