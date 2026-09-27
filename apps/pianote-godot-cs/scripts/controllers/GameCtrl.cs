using Godot;
using Pianote.ChartConverter;
using Pianote.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Pianote.Controllers;

public partial class GameCtrl : Node2D
{
    private const int FirstPianoPitch = 21;
    private const int LastPianoPitch = 108;
    private const int WhiteKeyCount = 52;

    [Signal]
    public delegate void ChartLoadedEventHandler(string chartPath, int noteCount, double durationSec);

    [Signal]
    public delegate void ChartPreparationStartedEventHandler(string songName);

    [Signal]
    public delegate void ChartPreparedEventHandler(bool success, string error, int noteCount, double durationSec);

    [Signal]
    public delegate void GameStartedEventHandler(string songName);

    [Signal]
    public delegate void GamePausedEventHandler(double playheadSec);

    [Signal]
    public delegate void GameResumedEventHandler(double playheadSec);

    [Signal]
    public delegate void GameStoppedEventHandler();

    [Export] public NodePath InputCtrlPath { get; set; } = new("../InputCtrl");
    [Export] public NodePath EvalCtrlPath { get; set; } = new("../EvalCtrl");
    [Export] public NodePath LaneBackgroundPath { get; set; } = new("LaneBackground");
    [Export] public NodePath LaneGridRootPath { get; set; } = new("LaneGridRoot");
    [Export] public NodePath NoteLaneRootPath { get; set; } = new("NoteLaneRoot");
    [Export] public NodePath KeyboardRootPath { get; set; } = new("KeyboardRoot");
    [Export] public NodePath HitLinePath { get; set; } = new("HitLine");
    [Export] public double LookaheadSec { get; set; } = 5.0;
    [Export] public int HandSplitPitch { get; set; } = 60;
    [Export] public bool AllowDebugChartFallback { get; set; }

    [ExportGroup("Hand Assignment")]
    [Export] public bool UseContextualHandAssignment { get; set; } = true;
    [Export] public double ChordOnsetToleranceMs { get; set; } = 45.0;
    [Export] public int ComfortableHandSpanSemitones { get; set; } = 12;
    [Export] public int MaximumHandSpanSemitones { get; set; } = 17;
    [Export] public int HandAssignmentBeamWidth { get; set; } = 64;
    [Export] public bool AllowHandCrossing { get; set; } = true;

    [ExportGroup("Note Colors")]
    [Export] public Color LeftWhiteColor { get; set; } = new("59b8f6");
    [Export] public Color LeftBlackColor { get; set; } = new("2478ed");
    [Export] public Color RightWhiteColor { get; set; } = new("cf4ce0");
    [Export] public Color RightBlackColor { get; set; } = new("82458f");

    private InputCtrl? _inputCtrl;
    private EvalCtrl? _evalCtrl;
    private ColorRect? _laneBackground;
    private Node2D? _laneGridRoot;
    private Node2D? _noteLaneRoot;
    private Node2D? _keyboardRoot;
    private ColorRect? _hitLine;
    private MidiChart _chart = MidiChart.Empty();
    private readonly Dictionary<int, Control> _activeVisuals = new();
    private Task<MidiChart>? _preparationTask;
    private int _nextVisualIndex;
    private string _songName = "";
    private string _chartPath = "";
    private string _difficulty = "Normal";
    private bool _isPreparing;
    private bool _isPlaying;
    private bool _isPaused;
    private double _songStartSec;
    private double _playheadSec;
    private float _hitLineY;
    private float _noteFallSpeed;
    private float _whiteKeyWidth;
    private float _blackKeyWidth;
    private float _keyboardHeight;

    public override void _Ready()
    {
        _inputCtrl = GetNodeOrNull<InputCtrl>(InputCtrlPath);
        _evalCtrl = GetNodeOrNull<EvalCtrl>(EvalCtrlPath);
        _laneBackground = GetNodeOrNull<ColorRect>(LaneBackgroundPath);
        _laneGridRoot = GetNodeOrNull<Node2D>(LaneGridRootPath);
        _noteLaneRoot = GetNodeOrNull<Node2D>(NoteLaneRootPath);
        _keyboardRoot = GetNodeOrNull<Node2D>(KeyboardRootPath);
        _hitLine = GetNodeOrNull<ColorRect>(HitLinePath);

        if (_inputCtrl != null)
            _inputCtrl.MidiNoteDetected += OnMidiNoteDetected;

        GetViewport().SizeChanged += OnViewportSizeChanged;
        UpdatePlayfieldLayout();
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (_inputCtrl != null)
            _inputCtrl.MidiNoteDetected -= OnMidiNoteDetected;
        if (GetViewport() != null)
            GetViewport().SizeChanged -= OnViewportSizeChanged;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        PollChartPreparation();

        if (!_isPlaying || _isPaused || !_chart.IsValid)
            return;

        _playheadSec = NowSec() - _songStartSec;
        _evalCtrl?.SetPlayhead(_playheadSec);
        UpdateNoteVisuals();

        if (_chart.DurationSec > 0.0 && _playheadSec > _chart.DurationSec + 2.0)
            StopSong();
    }

    public void PrepareSongAsync(Node songNode, string difficulty)
    {
        StopSong();
        _songName = ReadString(songNode, "song_name", "Untitled");
        _chartPath = ReadString(songNode, "chart_file", "");
        _difficulty = difficulty;
        _chart = MidiChart.Empty(_chartPath);
        _isPreparing = true;
        _preparationTask = null;

        ClearVisuals();
        _nextVisualIndex = 0;
        EmitSignal(SignalName.ChartPreparationStarted, _songName);

        if (string.IsNullOrWhiteSpace(_chartPath))
        {
            _preparationTask = Task.FromResult(MidiChart.Empty(_chartPath, "No MIDI chart path was provided."));
            return;
        }

        var songId = SanitizeFileName(ReadString(songNode, "song_id", "song"));
        var cachePath = ProjectSettings.GlobalizePath($"user://chart_cache/{songId}.json");
        var handAssignmentOptions = CreateHandAssignmentOptions();

        if (!Godot.FileAccess.FileExists(_chartPath))
        {
            _preparationTask = Task.FromResult(
                MidiChart.Empty(_chartPath, $"MIDI chart not found: {_chartPath}"));
            return;
        }

        var midiBytes = Godot.FileAccess.GetFileAsBytes(_chartPath);

        _preparationTask = Task.Run(() =>
            MidiChartLoader.LoadFromBytes(midiBytes, _chartPath, handAssignmentOptions, cachePath));
    }

    public bool StartPreparedSong()
    {
        if (_isPreparing || !_chart.IsValid || _chart.Notes.Count == 0)
            return false;

        _ = _difficulty;
        _songStartSec = NowSec();
        _playheadSec = 0.0;
        _nextVisualIndex = 0;
        _isPlaying = true;
        _isPaused = false;
        Visible = true;
        UpdatePlayfieldLayout();
        _inputCtrl?.StartCapture();
        EmitSignal(SignalName.GameStarted, _songName);
        return true;
    }

    public bool LoadSong(Node songNode, string difficulty)
    {
        _songName = ReadString(songNode, "song_name", "Untitled");
        _chartPath = ReadString(songNode, "chart_file", "");
        _difficulty = difficulty;
        _chart = MidiChartLoader.LoadFromGodotPath(_chartPath, CreateHandAssignmentOptions());
        FinishChartLoad();
        return _chart.IsValid && _chart.Notes.Count > 0;
    }

    public bool StartSong(Node songNode, string difficulty)
    {
        return LoadSong(songNode, difficulty) && StartPreparedSong();
    }

    public bool PauseSong()
    {
        if (!_isPlaying || _isPaused)
            return false;

        _playheadSec = Math.Max(0.0, NowSec() - _songStartSec);
        _isPaused = true;
        _inputCtrl?.StopCapture();
        EmitSignal(SignalName.GamePaused, _playheadSec);
        return true;
    }

    public bool ResumeSong()
    {
        if (!_isPlaying || !_isPaused)
            return false;

        _songStartSec = NowSec() - _playheadSec;
        _isPaused = false;
        _inputCtrl?.StartCapture();
        EmitSignal(SignalName.GameResumed, _playheadSec);
        return true;
    }

    public void StopSong()
    {
        var wasActive = _isPlaying || _isPreparing;
        _isPreparing = false;
        _preparationTask = null;
        _isPlaying = false;
        _isPaused = false;
        _inputCtrl?.StopCapture();
        ClearVisuals();
        Visible = false;

        if (wasActive)
            EmitSignal(SignalName.GameStopped);
    }

    public double GetPlayheadSec() => _playheadSec;

    public bool IsSongPaused() => _isPlaying && _isPaused;

    private void PollChartPreparation()
    {
        if (!_isPreparing || _preparationTask == null || !_preparationTask.IsCompleted)
            return;

        _isPreparing = false;
        if (_preparationTask.IsFaulted)
        {
            var message = _preparationTask.Exception?.GetBaseException().Message ?? "Unknown chart conversion error.";
            _chart = MidiChart.Empty(_chartPath, message);
        }
        else
        {
            _chart = _preparationTask.Result;
        }

        _preparationTask = null;
        FinishChartLoad();
    }

    private void FinishChartLoad()
    {
        ClearVisuals();
        _nextVisualIndex = 0;

        if (!_chart.IsValid && AllowDebugChartFallback)
        {
            GD.PushWarning($"{_chart.Error} Using built-in debug chart instead.");
            _chart = CreateDebugChart(_chartPath);
        }

        if (_chart.IsValid && _chart.Notes.Count > 0)
            _evalCtrl?.StartChart(_chart);

        EmitSignal(SignalName.ChartLoaded, _chartPath, _chart.Notes.Count, _chart.DurationSec);
        EmitSignal(
            SignalName.ChartPrepared,
            _chart.IsValid && _chart.Notes.Count > 0,
            _chart.Error,
            _chart.Notes.Count,
            _chart.DurationSec);
    }

    private void OnMidiNoteDetected(int pitch, float velocity, double timestampSec)
    {
        if (!_isPlaying || _isPaused || _evalCtrl == null)
            return;

        _evalCtrl.OnMidiNoteDetected(pitch, velocity, Math.Max(0.0, timestampSec - _songStartSec));
    }

    private void OnViewportSizeChanged()
    {
        UpdatePlayfieldLayout();
        if (_isPlaying && _chart.IsValid)
            UpdateNoteVisuals();
    }

    private void UpdatePlayfieldLayout()
    {
        var viewportSize = GetViewportRect().Size;
        if (viewportSize.X <= 1.0f || viewportSize.Y <= 1.0f)
            return;

        _keyboardHeight = Mathf.Clamp(viewportSize.Y * 0.24f, 120.0f, 220.0f);
        _hitLineY = viewportSize.Y - _keyboardHeight;
        _noteFallSpeed = Mathf.Max(120.0f, _hitLineY / (float)Math.Max(1.0, LookaheadSec));
        _whiteKeyWidth = viewportSize.X / WhiteKeyCount;
        _blackKeyWidth = _whiteKeyWidth * 0.62f;

        if (_laneBackground != null)
        {
            _laneBackground.Position = Vector2.Zero;
            _laneBackground.Size = viewportSize;
            _laneBackground.ZIndex = -20;
        }

        if (_hitLine != null)
        {
            _hitLine.Position = new Vector2(0.0f, _hitLineY - 2.0f);
            _hitLine.Size = new Vector2(viewportSize.X, 3.0f);
            _hitLine.ZIndex = 20;
        }

        BuildLaneGrid(viewportSize);
        BuildKeyboard(viewportSize);
    }

    private void BuildLaneGrid(Vector2 viewportSize)
    {
        if (_laneGridRoot == null)
            return;

        QueueFreeChildren(_laneGridRoot);
        _laneGridRoot.ZIndex = -10;

        for (var i = 0; i <= WhiteKeyCount; i++)
        {
            var line = new ColorRect
            {
                Position = new Vector2(i * _whiteKeyWidth, 0.0f),
                Size = new Vector2(1.0f, _hitLineY),
                Color = new Color(1.0f, 1.0f, 1.0f, 0.12f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _laneGridRoot.AddChild(line);
        }

        for (var i = 1; i < 4; i++)
        {
            var y = _hitLineY * i / 4.0f;
            var line = new ColorRect
            {
                Position = new Vector2(0.0f, y),
                Size = new Vector2(viewportSize.X, 1.0f),
                Color = new Color(1.0f, 1.0f, 1.0f, 0.13f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _laneGridRoot.AddChild(line);
        }
    }

    private void BuildKeyboard(Vector2 viewportSize)
    {
        if (_keyboardRoot == null)
            return;

        QueueFreeChildren(_keyboardRoot);
        _keyboardRoot.Position = new Vector2(0.0f, _hitLineY);
        _keyboardRoot.ZIndex = 30;

        var whiteIndex = 0;
        for (var pitch = FirstPianoPitch; pitch <= LastPianoPitch; pitch++)
        {
            if (IsBlackPitch(pitch))
                continue;

            var key = new ColorRect
            {
                Name = $"WhiteKey_{pitch}",
                Position = new Vector2(whiteIndex * _whiteKeyWidth, 0.0f),
                Size = new Vector2(_whiteKeyWidth - 1.0f, _keyboardHeight),
                Color = new Color("f4f5f7"),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _keyboardRoot.AddChild(key);

            if (pitch % 12 == 0)
            {
                var label = new Label
                {
                    Text = $"C{pitch / 12 - 1}",
                    Position = new Vector2(0.0f, _keyboardHeight - 34.0f),
                    Size = new Vector2(_whiteKeyWidth - 1.0f, 28.0f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                label.AddThemeColorOverride("font_color", new Color(0.38f, 0.40f, 0.44f, 0.75f));
                key.AddChild(label);
            }

            whiteIndex++;
        }

        for (var pitch = FirstPianoPitch; pitch <= LastPianoPitch; pitch++)
        {
            if (!IsBlackPitch(pitch))
                continue;

            var whiteKeysBefore = CountWhiteKeysThrough(pitch - 1);
            var key = new ColorRect
            {
                Name = $"BlackKey_{pitch}",
                Position = new Vector2(whiteKeysBefore * _whiteKeyWidth - _blackKeyWidth * 0.5f, 0.0f),
                Size = new Vector2(_blackKeyWidth, _keyboardHeight * 0.64f),
                Color = new Color("08090b"),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ZIndex = 2
            };
            _keyboardRoot.AddChild(key);
        }

        _ = viewportSize;
    }

    private void UpdateNoteVisuals()
    {
        if (_noteLaneRoot == null)
            return;

        while (_nextVisualIndex < _chart.Notes.Count &&
               _chart.Notes[_nextVisualIndex].StartSec <= _playheadSec + LookaheadSec)
        {
            CreateNoteVisual(_chart.Notes[_nextVisualIndex]);
            _nextVisualIndex++;
        }

        var staleVisuals = new List<int>();
        foreach (var (noteIndex, visual) in _activeVisuals)
        {
            var note = _chart.Notes[noteIndex];
            var geometry = GetPitchGeometry(note.Pitch);
            var y = _hitLineY - (float)(note.StartSec - _playheadSec) * _noteFallSpeed;
            var visualTop = y - visual.Size.Y;
            visual.Position = new Vector2(geometry.X, visualTop);
            visual.Size = new Vector2(geometry.Width, visual.Size.Y);

            // Keep sustained notes alive until the tail has completely left the viewport.
            if (visualTop > GetViewportRect().Size.Y + 8.0f)
                staleVisuals.Add(noteIndex);
        }

        foreach (var noteIndex in staleVisuals)
        {
            _activeVisuals[noteIndex].QueueFree();
            _activeVisuals.Remove(noteIndex);
        }
    }

    private void CreateNoteVisual(MidiNote note)
    {
        if (_noteLaneRoot == null || _activeVisuals.ContainsKey(note.Index))
            return;

        var geometry = GetPitchGeometry(note.Pitch);
        var height = Mathf.Max(12.0f, (float)note.DurationSec * _noteFallSpeed);
        var color = GetNoteColor(note);
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5
        };
        style.BorderWidthLeft = 1;
        style.BorderWidthRight = 1;
        style.BorderWidthTop = 1;
        style.BorderWidthBottom = 1;
        style.BorderColor = color.Lightened(0.18f);

        var visual = new Panel
        {
            Name = $"Note_{note.Index}",
            Size = new Vector2(geometry.Width, height),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        visual.AddThemeStyleboxOverride("panel", style);
        visual.SetMeta("note_index", note.Index);
        visual.SetMeta("pitch", note.Pitch);
        visual.SetMeta("hand", note.Hand.ToString());

        _noteLaneRoot.AddChild(visual);
        _activeVisuals[note.Index] = visual;
    }

    private Color GetNoteColor(MidiNote note)
    {
        if (note.Hand == PianoHand.Left)
            return note.IsBlackKey ? LeftBlackColor : LeftWhiteColor;
        return note.IsBlackKey ? RightBlackColor : RightWhiteColor;
    }

    private (float X, float Width) GetPitchGeometry(int pitch)
    {
        pitch = Math.Clamp(pitch, FirstPianoPitch, LastPianoPitch);
        if (IsBlackPitch(pitch))
        {
            var whiteKeysBefore = CountWhiteKeysThrough(pitch - 1);
            return (whiteKeysBefore * _whiteKeyWidth - _blackKeyWidth * 0.5f, _blackKeyWidth);
        }

        var whiteIndex = CountWhiteKeysThrough(pitch) - 1;
        var margin = Mathf.Min(3.0f, _whiteKeyWidth * 0.12f);
        return (whiteIndex * _whiteKeyWidth + margin, Mathf.Max(4.0f, _whiteKeyWidth - margin * 2.0f));
    }

    private void ClearVisuals()
    {
        foreach (var visual in _activeVisuals.Values)
            visual.QueueFree();
        _activeVisuals.Clear();
    }

    private static void QueueFreeChildren(Node node)
    {
        foreach (var child in node.GetChildren())
            child.QueueFree();
    }

    private static int CountWhiteKeysThrough(int pitch)
    {
        var count = 0;
        var upper = Math.Min(pitch, LastPianoPitch);
        for (var value = FirstPianoPitch; value <= upper; value++)
        {
            if (!IsBlackPitch(value))
                count++;
        }
        return count;
    }

    private static bool IsBlackPitch(int pitch)
    {
        var pitchClass = ((pitch % 12) + 12) % 12;
        return pitchClass is 1 or 3 or 6 or 8 or 10;
    }

    private static MidiChart CreateDebugChart(string sourcePath)
    {
        var notes = new List<MidiNote>();
        var pitches = new[] { 48, 52, 55, 60, 64, 67, 72, 67, 64, 60, 55, 52, 48 };
        for (var i = 0; i < pitches.Length; i++)
            notes.Add(new MidiNote(i, pitches[i], 0, 1.0 + i * 0.45, 0.32, 0.8f));
        return new MidiChart(sourcePath, notes);
    }

    private static string ReadString(Node node, string propertyName, string fallback)
    {
        var value = node.Get(propertyName);
        return value.VariantType == Variant.Type.Nil ? fallback : value.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "song" : value;
    }

    private ChartConversionOptions CreateHandAssignmentOptions()
    {
        return new ChartConversionOptions
        {
            HandSplitPitch = HandSplitPitch,
            HandAssignmentStrategy = UseContextualHandAssignment
                ? HandAssignmentStrategy.Contextual
                : HandAssignmentStrategy.FixedSplit,
            ChordOnsetToleranceSec = Math.Clamp(ChordOnsetToleranceMs, 0.0, 200.0) / 1000.0,
            ComfortableHandSpanSemitones = Math.Clamp(ComfortableHandSpanSemitones, 5, 24),
            MaximumHandSpanSemitones = Math.Clamp(MaximumHandSpanSemitones, 8, 30),
            HandAssignmentBeamWidth = Math.Clamp(HandAssignmentBeamWidth, 8, 256),
            AllowHandCrossing = AllowHandCrossing
        };
    }

    private static double NowSec() => Time.GetTicksUsec() / 1_000_000.0;
}
