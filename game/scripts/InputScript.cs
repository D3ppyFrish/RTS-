using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Rts.Sim;

namespace Rts.Game;

/// <summary>
/// Scripted mouse and keyboard input for automated screenshot tests (--input="..." or --input-file=PATH).
/// Steps are separated by ';' or new lines and run one after another, a frame at a time:
///   wait N               let N frames pass
///   ticks N              let the simulation run N ticks, or until the match is over for you (gives up
///                        after a few seconds if paused)
///   waitmatch            wait until a match is running (after Start match on the start screen; gives up
///                        after a minute)
///   key SPEC[*N]         press and release a key N times: space, pagedown, t, 1, ctrl+1, minus, ...
///   mouse X,Y            move the mouse (dragging if a button is held)
///   down X,Y [right]     press a mouse button (left by default); "here" for X,Y: where the mouse is
///   up X,Y [right]       release it
///   click X,Y [right] [ctrl] [shift]   press and release, optionally holding Ctrl or Shift
///   mouseworld X,Y,Z     move the mouse to the top of block (X, Y, Z) as the camera sees it now (a Y
///                        of g means the highest solid block in that column, g+N that many blocks above
///                        it: g+1 is the middle of a unit standing there)
///   clickworld X,Y,Z [right] [ctrl] [shift]   click the top of block (X, Y, Z)
///   clickui NAME         click the middle of the visible HUD control with that node name (tech_roads,
///                        train_worker, build_house, techs_button, ...)
///   select NAME N|TEXT   pick item N (from 0) of the visible option list with that node name, or the first
///                        whose text starts with TEXT (any case; _ for a space), as a click on it does
///   spin NAME V          set the visible number box with that node name to V
///   drag X1,Y1 X2,Y2     left-drag from one point to another and release
///   dragworld X1,Y1,Z1 X2,Y2,Z2   left-drag from the top of one block to the top of another
///   place X1,Y1,Z1 X2,Y2,Z2   place stone in every empty block of the box, as the terrain tool's right
///                        click does (a Y of g+N counts from the highest solid block at X1, Z1)
///   camera X,Z [YAW PITCH DISTANCE]   move the camera focus to block column (X, Z)
///   shot PATH            save a screenshot (after the frame has been drawn)
///   print                print a "UI ..." line describing the selection, clock and slice
///   quit                 exit (also happens after the last step)
/// Screen coordinates are viewport pixels (1600x900 by default).
/// </summary>
public sealed class InputScript
{
    private readonly Queue<string> _steps = new();
    private int _waitFrames;
    private long _waitUntilTick = -1;
    private int _tickTimeout;
    private int _waitForMatch;
    private string? _pendingShot;
    private MouseButtonMask _held;
    private Vector2 _mouse;

    public bool Finished { get; private set; }

    public InputScript(string text)
    {
        foreach (var raw in text.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var step = raw.Trim();
            if (step.Length > 0 && !step.StartsWith('#')) _steps.Enqueue(step);
        }
    }

    /// <summary>Runs the script for this frame. Call once per frame from the main node.</summary>
    public void Update(Main main)
    {
        if (Finished) return;
        if (_pendingShot != null)
        {
            // Give the frame with the latest input a chance to be drawn before grabbing it.
            if (_waitFrames-- > 0) return;
            main.SaveScreenshot(_pendingShot);
            _pendingShot = null;
        }
        if (_waitFrames > 0)
        {
            _waitFrames--;
            return;
        }
        if (_waitUntilTick >= 0)
        {
            if ((main.Sim?.Tick ?? 0) < _waitUntilTick && !main.MatchEnded && _tickTimeout-- > 0) return;
            _waitUntilTick = -1;
        }
        if (_waitForMatch > 0)
        {
            if (main.Sim == null && --_waitForMatch > 0) return;
            if (main.Sim == null) GD.PrintErr("Input script: no match started");
            _waitForMatch = 0;
        }
        while (_steps.Count > 0)
        {
            var step = _steps.Dequeue();
            if (!Run(step, main)) return; // the step waits for later frames
        }
        Finished = true;
    }

    /// <summary>Runs one step; false if the script should continue on a later frame.</summary>
    private bool Run(string step, Main main)
    {
        var parts = step.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();
        switch (cmd)
        {
            case "wait":
                _waitFrames = Int(parts, 1, 1);
                return false;
            case "ticks":
                _waitUntilTick = (main.Sim?.Tick ?? 0) + Int(parts, 1, 1);
                _tickTimeout = 600;
                return false;
            case "waitmatch":
                _waitForMatch = 3600;
                return false;
            case "key":
                PressKey(parts[1]);
                return false;
            case "mouse":
                MoveMouse(Point(parts[1]));
                return false;
            case "down":
                Button(parts[1] == "here" ? _mouse : Point(parts[1]), ButtonOf(parts), true);
                return false;
            case "up":
                Button(parts[1] == "here" ? _mouse : Point(parts[1]), ButtonOf(parts), false);
                return false;
            case "click":
                Button(Point(parts[1]), ButtonOf(parts), true, parts);
                Button(Point(parts[1]), ButtonOf(parts), false, parts);
                return false;
            case "mouseworld":
                MoveMouse(World(main, parts[1]));
                return false;
            case "clickworld":
            {
                var at = World(main, parts[1]);
                Button(at, ButtonOf(parts), true, parts);
                Button(at, ButtonOf(parts), false, parts);
                return false;
            }
            case "clickui":
            {
                if (main.FindChild(parts[1], recursive: true, owned: false) is not Control { } control || !control.IsVisibleInTree())
                {
                    GD.PrintErr($"Input script: no visible control named '{parts[1]}'");
                    return true;
                }
                var at = control.GetGlobalRect().GetCenter();
                Button(at, MouseButton.Left, true);
                Button(at, MouseButton.Left, false);
                return false;
            }
            case "select" or "spin":
            {
                var found = main.FindChild(parts[1], recursive: true, owned: false) as Control;
                int value = -1;
                if (found is OptionButton list && parts.Length >= 3 && !int.TryParse(parts[2], out value))
                {
                    string text = parts[2].Replace('_', ' ');
                    for (int k = list.ItemCount - 1; k >= 0; k--)
                        if (list.GetItemText(k).StartsWith(text, StringComparison.OrdinalIgnoreCase)) value = k;
                }
                if (found == null || !found.IsVisibleInTree() || parts.Length < 3 || value < 0 && !int.TryParse(parts[2], out value))
                {
                    GD.PrintErr($"Input script: no visible control named '{parts[1]}'");
                    return true;
                }
                if (found is OptionButton o && value >= 0 && value < o.ItemCount)
                {
                    o.Select(value);
                    o.EmitSignal(OptionButton.SignalName.ItemSelected, value);
                }
                else if (found is SpinBox sb) sb.Value = value;
                else GD.PrintErr($"Input script: cannot {parts[0]} {value} on '{parts[1]}'");
                return false;
            }
            case "drag":
            case "dragworld":
            {
                var a = cmd == "drag" ? Point(parts[1]) : World(main, parts[1]);
                var b = cmd == "drag" ? Point(parts[2]) : World(main, parts[2]);
                Button(a, MouseButton.Left, true);
                for (int k = 1; k <= 8; k++) MoveMouse(a.Lerp(b, k / 8f));
                Button(b, MouseButton.Left, false);
                return false;
            }
            case "place":
            {
                var sim = main.Sim;
                if (sim == null) return true;
                var (x1, y1, z1) = Block(sim, parts[1], null);
                var (x2, y2, z2) = Block(sim, parts[2], (x1, z1));
                for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
                for (int z = Math.Min(z1, z2); z <= Math.Max(z1, z2); z++)
                for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
                    sim.Schedule(sim.Tick, new PlaceCommand(PlayerControl.LocalPlayer, x, y, z, BlockType.Stone));
                return true;
            }
            case "camera":
            {
                var xz = Point(parts[1]);
                if (main.Sim == null) return true;
                main.FocusCamera((int)xz.X, (int)xz.Y,
                    parts.Length > 4 ? (float?)Float(parts[2]) : null,
                    parts.Length > 4 ? Float(parts[3]) : 0,
                    parts.Length > 4 ? Float(parts[4]) : 0);
                _waitFrames = 2;
                return false;
            }
            case "shot":
                _pendingShot = parts.Length > 1 ? parts[1] : main.ScreenshotPath ?? "user://shot.png";
                _waitFrames = 3;
                return false;
            case "print":
                GD.Print("UI " + main.DescribeUi());
                return true;
            case "quit":
                _steps.Clear();
                return true;
            default:
                GD.PrintErr($"Input script: unknown step '{step}'");
                return true;
        }
    }

    private void PressKey(string spec)
    {
        int times = 1;
        int star = spec.IndexOf('*');
        if (star > 0)
        {
            times = int.Parse(spec[(star + 1)..], CultureInfo.InvariantCulture);
            spec = spec[..star];
        }
        bool ctrl = false, shift = false, alt = false;
        var names = spec.ToLowerInvariant().Split('+');
        var key = Key.None;
        foreach (var n in names)
        {
            switch (n)
            {
                case "ctrl": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default: key = KeyFromName(n); break;
            }
        }
        for (int t = 0; t < times; t++)
        {
            foreach (bool pressed in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = key,
                    PhysicalKeycode = key,
                    Pressed = pressed,
                    CtrlPressed = ctrl,
                    ShiftPressed = shift,
                    AltPressed = alt,
                });
            }
        }
    }

    private static Key KeyFromName(string n) => n switch
    {
        "space" => Key.Space,
        "pagedown" or "pgdn" => Key.Pagedown,
        "pageup" or "pgup" => Key.Pageup,
        "home" => Key.Home,
        "escape" or "esc" => Key.Escape,
        "minus" => Key.Minus,
        "equal" or "plus" => Key.Equal,
        "bracketleft" or "[" => Key.Bracketleft,
        "bracketright" or "]" => Key.Bracketright,
        "apostrophe" or "'" => Key.Apostrophe,
        "comma" or "," => Key.Comma,
        _ when n.Length == 1 && char.IsDigit(n[0]) => Key.Key0 + (n[0] - '0'),
        _ when n.Length == 1 && char.IsLetter(n[0]) => Key.A + (char.ToUpperInvariant(n[0]) - 'A'),
        _ => throw new FormatException($"Input script: unknown key '{n}'"),
    };

    private void MoveMouse(Vector2 to)
    {
        var relative = to - _mouse;
        _mouse = to;
        Input.WarpMouse(to);
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = to,
            GlobalPosition = to,
            Relative = relative,
            ButtonMask = _held,
        });
    }

    private void Button(Vector2 at, MouseButton button, bool pressed, string[]? parts = null)
    {
        if (at != _mouse) MoveMouse(at);
        var mask = button == MouseButton.Right ? MouseButtonMask.Right : MouseButtonMask.Left;
        _held = pressed ? _held | mask : _held & ~mask;
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = at,
            GlobalPosition = at,
            ButtonIndex = button,
            Pressed = pressed,
            ButtonMask = _held,
            CtrlPressed = parts != null && Has(parts, "ctrl"),
            ShiftPressed = parts != null && Has(parts, "shift"),
        });
    }

    private static MouseButton ButtonOf(string[] parts) => Has(parts, "right") ? MouseButton.Right : MouseButton.Left;

    /// <summary>True if one of the step's words after the position is <paramref name="word"/>.</summary>
    private static bool Has(string[] parts, string word)
    {
        for (int k = 2; k < parts.Length; k++)
            if (parts[k].Equals(word, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The screen point of block "X,Y,Z"; a Y of "g" means the highest solid block in the column.</summary>
    private static Vector2 World(Main main, string xyz)
    {
        var p = xyz.Split(',');
        int x = int.Parse(p[0], CultureInfo.InvariantCulture), z = int.Parse(p[2], CultureInfo.InvariantCulture);
        int y = p[1].StartsWith('g')
            ? main.Sim!.World.SurfaceHeight(x, z) + (p[1].Length > 1 ? int.Parse(p[1][1..], CultureInfo.InvariantCulture) : 0)
            : int.Parse(p[1], CultureInfo.InvariantCulture);
        return main.BlockOnScreen(x, y, z);
    }

    /// <summary>Block X,Y,Z, where a Y of g+N counts from the highest solid block in column <paramref name="ground"/> (or its own).</summary>
    private static (int X, int Y, int Z) Block(Simulation sim, string xyz, (int X, int Z)? ground)
    {
        var p = xyz.Split(',');
        int x = int.Parse(p[0], CultureInfo.InvariantCulture), z = int.Parse(p[2], CultureInfo.InvariantCulture);
        var (gx, gz) = ground ?? (x, z);
        int y = p[1].StartsWith('g')
            ? sim.World.SurfaceHeight(gx, gz) + (p[1].Length > 1 ? int.Parse(p[1][1..], CultureInfo.InvariantCulture) : 0)
            : int.Parse(p[1], CultureInfo.InvariantCulture);
        return (x, y, z);
    }

    private static Vector2 Point(string s)
    {
        var xy = s.Split(',');
        return new Vector2(Float(xy[0]), Float(xy[1]));
    }

    private static float Float(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static int Int(string[] parts, int index, int fallback) =>
        parts.Length > index ? int.Parse(parts[index], CultureInfo.InvariantCulture) : fallback;
}
