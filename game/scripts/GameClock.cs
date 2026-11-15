using System;

namespace Rts.Game;

/// <summary>
/// Pause and game speed (single player). The simulation always runs the same fixed ticks; the speed only
/// sets how much game time passes per real second. Orders can still be given while paused: they are
/// scheduled for the next tick and carried out once the game runs again.
/// </summary>
public sealed class GameClock
{
    private static readonly double[] NormalSpeeds = { 0.5, 1, 2, 4 };
    private static readonly double[] TestSpeeds = { 0.5, 1, 2, 4, 8 };

    /// <summary>Test mode's clock also runs at 8x (set before the HUD is made).</summary>
    public static bool Fast { get; set; }

    public static double[] Speeds => Fast ? TestSpeeds : NormalSpeeds;

    /// <summary>A single tick asked for while paused (test mode's Step), not yet run.</summary>
    public bool StepPending { get; set; }

    public int SpeedIndex { get; private set; } = 1;
    public bool Paused { get; private set; }
    public double Speed => Speeds[SpeedIndex];

    /// <summary>Game time that passes in <paramref name="realSeconds"/> of real time.</summary>
    public double Scale(double realSeconds) => Paused ? 0 : realSeconds * Speed;

    public void TogglePause() => Paused = !Paused;

    public void Pause() => Paused = true;

    /// <summary>Sets the speed; <paramref name="resume"/> also unpauses (clicking a speed button does).</summary>
    public void SetSpeed(int index, bool resume)
    {
        SpeedIndex = Math.Clamp(index, 0, Speeds.Length - 1);
        if (resume) Paused = false;
    }

    public void Faster() => SetSpeed(SpeedIndex + 1, resume: false);
    public void Slower() => SetSpeed(SpeedIndex - 1, resume: false);

    public static string Label(double speed) => speed == 0.5 ? "0.5×" : $"{speed:0}×";
}
