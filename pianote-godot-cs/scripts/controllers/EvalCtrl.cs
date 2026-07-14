using Godot;
using Pianote.Core;
using System;
using System.Collections.Generic;

namespace Pianote.Controllers;

public partial class EvalCtrl : Node2D
{
    [Signal]
    public delegate void JudgementEventHandler(string label, double offsetSec, int scoreDelta, int combo);

    [Signal]
    public delegate void ScoreChangedEventHandler(int score, int combo);

    [Export] public double PerfectWindowSec { get; set; } = 0.050;
    [Export] public double GoodWindowSec { get; set; } = 0.120;
    [Export] public double MissWindowSec { get; set; } = 0.200;

    private IReadOnlyList<MidiNote> _notes = Array.Empty<MidiNote>();
    private bool[] _judged = Array.Empty<bool>();
    private int _nextMissIndex;
    private double _playheadSec;

    public int Score { get; private set; }
    public int Combo { get; private set; }

    public void StartChart(MidiChart chart)
    {
        _notes = chart.Notes;
        _judged = new bool[_notes.Count];
        _nextMissIndex = 0;
        _playheadSec = 0.0;
        Score = 0;
        Combo = 0;
        EmitSignal(SignalName.ScoreChanged, Score, Combo);
    }

    public void SetPlayhead(double playheadSec)
    {
        _playheadSec = Math.Max(0.0, playheadSec);
        EmitExpiredMisses();
    }

    public void OnMidiNoteDetected(int pitch, float velocity, double timestampSec)
    {
        _ = velocity;
        EvaluateNote(pitch, timestampSec);
    }

    private void EvaluateNote(int pitch, double timestampSec)
    {
        var bestIndex = -1;
        var bestAbsOffset = double.MaxValue;
        var bestOffset = 0.0;

        for (var i = 0; i < _notes.Count; i++)
        {
            if (_judged[i] || _notes[i].Pitch != pitch)
                continue;

            var offset = timestampSec - _notes[i].StartSec;
            var absOffset = Math.Abs(offset);
            if (absOffset > MissWindowSec || absOffset >= bestAbsOffset)
                continue;

            bestIndex = i;
            bestAbsOffset = absOffset;
            bestOffset = offset;
        }

        if (bestIndex < 0)
        {
            Combo = 0;
            EmitSignal(SignalName.Judgement, "Bad", 0.0, 0, Combo);
            EmitSignal(SignalName.ScoreChanged, Score, Combo);
            return;
        }

        _judged[bestIndex] = true;
        var label = bestAbsOffset <= PerfectWindowSec ? "Perfect" :
                    bestAbsOffset <= GoodWindowSec ? "Good" :
                    "Late";
        var scoreDelta = label == "Perfect" ? 1000 : label == "Good" ? 600 : 200;

        Combo++;
        Score += scoreDelta;

        EmitSignal(SignalName.Judgement, label, bestOffset, scoreDelta, Combo);
        EmitSignal(SignalName.ScoreChanged, Score, Combo);
    }

    private void EmitExpiredMisses()
    {
        while (_nextMissIndex < _notes.Count)
        {
            var note = _notes[_nextMissIndex];
            if (_judged[_nextMissIndex])
            {
                _nextMissIndex++;
                continue;
            }

            if (note.StartSec + MissWindowSec > _playheadSec)
                break;

            _judged[_nextMissIndex] = true;
            Combo = 0;
            EmitSignal(SignalName.Judgement, "Miss", _playheadSec - note.StartSec, 0, Combo);
            EmitSignal(SignalName.ScoreChanged, Score, Combo);
            _nextMissIndex++;
        }
    }
}
