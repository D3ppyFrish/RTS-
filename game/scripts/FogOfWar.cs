using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// Fog of war as you see it (issue #39), drawn from what the core works out (<see cref="VisionSystem"/>).
/// Ground you have never seen is dark; ground you have seen but cannot see now is dimmed and shows the
/// terrain and buildings as they were when you last saw them; other players' units, and their hits and
/// health bars, show only where you can see. The world drawn and clicked on (<see cref="World"/>) is the
/// world as you know it (<see cref="WorldMemory"/>): the land as the match was set up, and after that what
/// you have seen of it. With fog of war off everything shows, and once the match is over
/// (<see cref="Reveal"/>) it is lifted.
/// </summary>
public sealed class FogOfWar
{
    // Texture values for Unseen, Seen and Visible: the terrain shaders read them back as 0, a half and 1.
    private static readonly byte[] Shades = { 0, 128, 255 };

    private readonly Simulation _sim;
    private readonly int _player;
    private readonly WorldMemory? _memory;
    private readonly byte[] _shades;
    private readonly Image? _image;
    private int _version = -1;

    public FogOfWar(Simulation sim, int player)
    {
        _sim = sim;
        _player = player;
        Enabled = sim.Fog;
        _shades = new byte[Enabled ? sim.World.SizeX * sim.World.SizeZ : 0];
        if (!Enabled) return;
        _memory = new WorldMemory(sim, player);
        // Dark everywhere until the first look.
        _image = Image.CreateFromData(sim.World.SizeX, sim.World.SizeZ, false, Image.Format.R8, _shades);
        Texture = ImageTexture.CreateFromImage(_image);
    }

    /// <summary>Fog of war is on, and the match is not over.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The world as you know it: what is drawn and what clicks hit.</summary>
    public VoxelWorld World => _memory?.World ?? _sim.World;

    /// <summary>The fog, a column per texel (0 never seen, a half seen before, 1 in sight), or null with fog of war off.</summary>
    public ImageTexture? Texture { get; }

    /// <summary>True if you can see column (x, z) now.</summary>
    public bool Sees(int x, int z) => !Enabled || _sim.Vision.Sees(_player, x, z);

    /// <summary>True if you have ever seen column (x, z).</summary>
    public bool HasSeen(int x, int z) => !Enabled || _sim.Vision.At(_player, x, z) != Sight.Unseen;

    /// <summary>True if unit i is drawn: it is yours, or it stands where you can see.</summary>
    public bool Shows(int i) => !Enabled || _sim.Vision.SeesUnit(_player, i);

    /// <summary>True if any column of the building's footprint is in sight.</summary>
    public bool Shows(Rts.Sim.Economy.Building b)
    {
        if (!Enabled || b.Owner == _player) return true;
        for (int z = b.Z; z <= b.MaxZ; z++)
        for (int x = b.X; x <= b.MaxX; x++)
            if (_sim.Vision.Sees(_player, x, z)) return true;
        return false;
    }

    /// <summary>
    /// After the simulation has run: brings what you have just come to see up to date in the world you know,
    /// and the fog texture with it. True if the fog changed.
    /// </summary>
    public bool Update()
    {
        if (!Enabled || _sim.Vision.Version == _version) return false;
        _version = _sim.Vision.Version;
        _memory!.Update();
        var grid = _sim.Vision.Grid(_player);
        if (grid.IsEmpty) return false;
        for (int c = 0; c < grid.Length; c++) _shades[c] = Shades[grid[c]];
        _image!.SetData(_sim.World.SizeX, _sim.World.SizeZ, false, Image.Format.R8, _shades);
        Texture!.Update(_image);
        return true;
    }

    /// <summary>The match is over: everything shows, and the world drawn catches up with the real one.</summary>
    public void Reveal()
    {
        if (!Enabled) return;
        Enabled = false;
        _memory!.Lift();
    }

    /// <summary>Stops following the world (the match is being left).</summary>
    public void Detach() => _memory?.Detach();
}
