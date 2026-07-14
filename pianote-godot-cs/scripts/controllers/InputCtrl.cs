using Godot;
using System.Collections.Generic;

namespace Pianote.Controllers;

public partial class InputCtrl : Node2D
{
    [Signal]
    public delegate void MidiNoteDetectedEventHandler(int pitch, float velocity, double timestampSec);

    [Export] public bool AutoStartCapture { get; set; } = true;
    [Export] public bool EnableDebugKeyboard { get; set; } = true;

    private readonly Dictionary<Key, int> _debugKeyboardMap = new()
    {
        [Key.A] = 60,
        [Key.W] = 61,
        [Key.S] = 62,
        [Key.E] = 63,
        [Key.D] = 64,
        [Key.F] = 65,
        [Key.T] = 66,
        [Key.G] = 67,
        [Key.Y] = 68,
        [Key.H] = 69,
        [Key.U] = 70,
        [Key.J] = 71,
        [Key.K] = 72
    };

    public bool IsCaptureActive { get; private set; }

    public override void _Ready()
    {
        if (AutoStartCapture)
            StartCapture();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsCaptureActive || !EnableDebugKeyboard)
            return;

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return;

        if (_debugKeyboardMap.TryGetValue(keyEvent.Keycode, out var pitch))
            PushDetectedNote(pitch, 1.0f);
    }

    public void StartCapture()
    {
        IsCaptureActive = true;
    }

    public void StopCapture()
    {
        IsCaptureActive = false;
    }

    public void PushDetectedNote(int pitch, float velocity)
    {
        PushDetectedNoteAtTime(pitch, velocity, NowSec());
    }

    public void PushDetectedNoteAtTime(int pitch, float velocity, double timestampSec)
    {
        if (pitch < 0 || pitch > 127)
        {
            GD.PushWarning($"Ignored invalid MIDI pitch: {pitch}");
            return;
        }

        EmitSignal(SignalName.MidiNoteDetected, pitch, Mathf.Clamp(velocity, 0.0f, 1.0f), timestampSec);
    }

    private static double NowSec() => Time.GetTicksUsec() / 1_000_000.0;
}
