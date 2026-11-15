using System;
using System.IO;
using Godot;
using Rts.Sim.Replay;

namespace Rts.Game;

/// <summary>
/// Saves a finished match's command log as a replay: by default in user://replays (on Windows,
/// %APPDATA%\Godot\app_userdata\RTS\replays), or in the folder given with --replays. A replay plays back
/// with the match runner: <c>dotnet run --project tools/MatchRunner -- --log=FILE</c>.
/// </summary>
public static class Replays
{
    public const string DefaultFolder = "user://replays";

    /// <summary>
    /// Writes the log to a new file named after the time and seed, under a comment line saying how the match
    /// went. Returns the file's full path, or null if it could not be written (the error is printed).
    /// </summary>
    public static string? Save(CommandLog log, string comment, string? folder = null, string prefix = "match")
    {
        try
        {
            string dir = ProjectSettings.GlobalizePath(folder ?? DefaultFolder);
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(dir, $"{prefix}-{stamp}-seed{log.Seed}.log");
            for (int k = 2; File.Exists(path); k++) path = Path.Combine(dir, $"{prefix}-{stamp}-seed{log.Seed}-{k}.log");
            using var w = new StreamWriter(path) { NewLine = "\n" };
            w.WriteLine("# " + comment);
            log.Write(w);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            GD.PrintErr($"Could not save the replay: {e.Message}");
            return null;
        }
    }
}
