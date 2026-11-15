using Godot;

namespace Rts.Game;

/// <summary>
/// Free 3D RTS camera orbiting a focus point on the ground.
///   Pan:    WASD / arrow keys, or hold Shift + middle mouse and drag
///   Orbit:  hold middle mouse and drag (horizontal = rotate, vertical = tilt), or Q/E and R/F
///   Zoom:   mouse wheel (moves the camera toward or away from the focus point)
/// Command-line overrides for screenshots: --cam-yaw, --cam-pitch, --cam-distance.
/// </summary>
public partial class CameraRig : Node3D
{
    public float MinDistance { get; set; } = 6f;
    public float MaxDistance { get; set; } = 320f;
    /// <summary>Low enough to look out to the horizon and see the sun rise and set (see <see cref="WorldLighting"/>).</summary>
    public float MinPitch { get; set; } = 3f;
    public float MaxPitch { get; set; } = 85f;
    public float PanSpeed { get; set; } = 25f;
    public float KeyRotateSpeed { get; set; } = 90f;
    public float MouseRotateSpeed { get; set; } = 0.3f;

    public float Yaw { get; set; } = 45f;
    public float Pitch { get; set; } = 50f;
    public float Distance { get; set; } = 60f;

    private float _targetDistance;
    private Camera3D _camera = null!;
    private bool _orbiting;
    private bool _panning;

    public Camera3D Camera => _camera;

    public override void _Ready()
    {
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Perspective, Fov = 50f, Near = 0.1f, Far = 1000f };
        AddChild(_camera);
        _targetDistance = Distance;
        UpdateTransform();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var input = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) input.Y += 1;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) input.X += 1;
        if (input != Vector2.Zero)
            Pan(input.Normalized() * PanSpeed * dt * (Distance / 60f));

        if (Input.IsKeyPressed(Key.Q)) Yaw -= KeyRotateSpeed * dt;
        if (Input.IsKeyPressed(Key.E)) Yaw += KeyRotateSpeed * dt;
        if (Input.IsKeyPressed(Key.R)) Pitch += KeyRotateSpeed * 0.5f * dt;
        if (Input.IsKeyPressed(Key.F)) Pitch -= KeyRotateSpeed * 0.5f * dt;

        Distance = Mathf.Lerp(Distance, _targetDistance, Mathf.Min(1f, dt * 10f));
        UpdateTransform();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } mb:
                _panning = mb.Pressed && mb.ShiftPressed;
                _orbiting = mb.Pressed && !mb.ShiftPressed;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _targetDistance = Mathf.Max(MinDistance, _targetDistance * 0.9f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _targetDistance = Mathf.Min(MaxDistance, _targetDistance * 1.1f);
                break;
            case InputEventMouseMotion motion when _orbiting:
                Yaw -= motion.Relative.X * MouseRotateSpeed;
                Pitch += motion.Relative.Y * MouseRotateSpeed;
                break;
            case InputEventMouseMotion motion when _panning:
                Pan(-motion.Relative * 0.002f * Distance);
                break;
        }
    }

    /// <summary>Instantly sets the view (used by tests and screenshots).</summary>
    public void SetView(float yaw, float pitch, float distance)
    {
        Yaw = yaw; Pitch = pitch; Distance = _targetDistance = distance;
        if (_camera != null) UpdateTransform();
    }

    private void Pan(Vector2 screenDelta)
    {
        // Move the focus point along the ground relative to where the camera faces.
        var move = new Vector3(screenDelta.X, 0, screenDelta.Y).Rotated(Vector3.Up, Mathf.DegToRad(-Yaw));
        Position += move;
    }

    private void UpdateTransform()
    {
        Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);
        Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
        float yaw = Mathf.DegToRad(-Yaw), pitch = Mathf.DegToRad(Pitch);
        var offset = new Vector3(
            Mathf.Sin(yaw) * Mathf.Cos(pitch),
            Mathf.Sin(pitch),
            Mathf.Cos(yaw) * Mathf.Cos(pitch)) * Distance;
        _camera.Position = offset;
        _camera.LookAt(GlobalPosition, Vector3.Up);
    }
}
