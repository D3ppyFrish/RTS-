using System.Collections.Generic;

namespace Rts.Sim;

/// <summary>
/// The world as one player knows it under fog of war (issue #39): a copy of the world taken when it is made,
/// in which a block that changes where the player can see changes at once, and the others when their column
/// next comes into the player's sight (see <see cref="VisionSystem"/>). It is not part of the simulation's
/// state; it is what is drawn and clicked on for that player. It follows the real world through
/// <see cref="VoxelWorld.BlockChanged"/> until <see cref="Detach"/>.
/// </summary>
public sealed class WorldMemory
{
    private readonly Simulation _sim;
    private readonly int _player;
    // Columns (x + z * SizeX) that changed while out of sight, in a list and as flags.
    private readonly List<int> _stale = new();
    private readonly bool[] _isStale;
    private int _version = -1;
    private bool _lifted;

    public WorldMemory(Simulation sim, int player)
    {
        _sim = sim;
        _player = player;
        World = sim.World.Clone();
        _isStale = new bool[sim.World.SizeX * sim.World.SizeZ];
        // Buildings of a hidden village it has not found are not in the world it knows (VisionSystem.Villages.cs).
        foreach (var b in sim.Buildings.All)
            if (sim.Vision.HiddenFrom(player, b)) Conceal(b);
        sim.World.BlockChanged += OnBlockChanged;
    }

    /// <summary>The world as the player knows it.</summary>
    public VoxelWorld World { get; }

    /// <summary>Columns that have changed since the player last saw them.</summary>
    public int StaleColumns => _stale.Count;

    /// <summary>
    /// After the simulation has run: the columns that changed out of sight and are in sight now are brought
    /// up to date. True if any were.
    /// </summary>
    public bool Update()
    {
        if (_sim.Vision.Version == _version) return false;
        _version = _sim.Vision.Version;
        int sx = _sim.World.SizeX, before = _stale.Count;
        for (int k = _stale.Count - 1; k >= 0; k--)
        {
            int c = _stale[k];
            if (!Known(c % sx, c / sx)) continue;
            CopyColumn(c);
            _stale[k] = _stale[^1];
            _stale.RemoveAt(_stale.Count - 1);
        }
        return _stale.Count != before;
    }

    /// <summary>Everything shows from now on (the match is over): the world known catches up with the real one, and stays with it.</summary>
    public void Lift()
    {
        _lifted = true;
        foreach (int c in _stale) CopyColumn(c);
        _stale.Clear();
    }

    /// <summary>Stops following the real world.</summary>
    public void Detach() => _sim.World.BlockChanged -= OnBlockChanged;

    private void OnBlockChanged(int x, int y, int z)
    {
        int c = x + z * _sim.World.SizeX;
        if (_lifted || Known(x, z))
        {
            World.Set(x, y, z, _sim.World.Get(x, y, z));
            return;
        }
        if (_isStale[c]) return;
        _isStale[c] = true;
        _stale.Add(c);
    }

    // True if what is in column (x, z) is known as it is: it is in sight, and not under a building hidden from the player.
    private bool Known(int x, int z) => _sim.Vision.Sees(_player, x, z) && !_sim.Vision.HiddenAt(_player, x, z);

    // Takes building b out of the world known, as if it had never been built there: its blocks are air, and those
    // under the ground soil. Its columns are brought up to date once the player finds it and sees them.
    private void Conceal(Economy.Building b)
    {
        int sx = World.SizeX;
        foreach (var t in b.Shape.Blocks)
        {
            int x = b.X + t.X, y = b.Y + t.Y, z = b.Z + t.Z;
            if (!World.InBounds(x, y, z) || World.Get(x, y, z) != t.Block) continue;
            World.Set(x, y, z, t.Y < 0 ? BlockType.Soil : BlockType.Air);
            int c = x + z * sx;
            if (_isStale[c]) continue;
            _isStale[c] = true;
            _stale.Add(c);
        }
    }

    private void CopyColumn(int c)
    {
        var real = _sim.World;
        int x = c % real.SizeX, z = c / real.SizeX;
        for (int y = 0; y < real.SizeY; y++) World.Set(x, y, z, real.Get(x, y, z));
        _isStale[c] = false;
    }
}
