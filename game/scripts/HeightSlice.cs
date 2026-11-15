using System;

namespace Rts.Game;

/// <summary>
/// The height slice: hides every block layer from <see cref="Level"/> up, so tunnels, caves and the units
/// in them can be seen and clicked. Off by default.
/// </summary>
public sealed class HeightSlice
{
    private readonly int _sizeY;

    /// <summary>First hidden block layer, or -1 when the slice is off.</summary>
    public int Level { get; private set; } = -1;
    public bool On => Level >= 0;
    public int MaxLevel => _sizeY;

    public event Action? Changed;

    public HeightSlice(int sizeY) => _sizeY = sizeY;

    /// <summary>True if something standing with its feet at block level y is shown.</summary>
    public bool Shows(int y) => Level < 0 || y < Level;

    /// <summary>World height of the cut in metres, or null when off.</summary>
    public float? CutHeight => Level < 0 ? null : Level * ChunkMesher.BlockSize;

    /// <summary>Lowers the cut by one layer; when off, starts just above head height at <paramref name="startLevel"/>.</summary>
    public void Lower(int startLevel) => Set(Level < 0 ? startLevel : Level - 1);

    /// <summary>Raises the cut by one layer; past the top of the world the slice turns off.</summary>
    public void Raise()
    {
        if (Level >= 0) Set(Level + 1);
    }

    public void Off() => Set(-1);

    public void Set(int level)
    {
        int next = level < 0 || level >= _sizeY ? -1 : Math.Max(1, level);
        if (next == Level) return;
        Level = next;
        Changed?.Invoke();
    }
}
