using System;
using System.Collections.Generic;
using System.Linq;

namespace Pianote.Core;

public enum PianoHand
{
    Left,
    Right
}

public sealed class MidiNote
{
    public MidiNote(int index, int pitch, int channel, double startSec, double durationSec, float velocity)
        : this(
            index,
            pitch,
            channel,
            0,
            startSec,
            durationSec,
            velocity,
            pitch < 60 ? PianoHand.Left : PianoHand.Right,
            IsBlackPitch(pitch))
    {
    }

    public MidiNote(
        int index,
        int pitch,
        int channel,
        int track,
        double startSec,
        double durationSec,
        float velocity,
        PianoHand hand,
        bool isBlackKey)
    {
        Index = index;
        Pitch = pitch;
        Channel = channel;
        Track = track;
        StartSec = startSec;
        DurationSec = durationSec;
        Velocity = velocity;
        Hand = hand;
        IsBlackKey = isBlackKey;
    }

    public int Index { get; }
    public int Pitch { get; }
    public int Channel { get; }
    public int Track { get; }
    public double StartSec { get; }
    public double DurationSec { get; }
    public double EndSec => StartSec + DurationSec;
    public float Velocity { get; }
    public PianoHand Hand { get; }
    public bool IsBlackKey { get; }

    private static bool IsBlackPitch(int pitch)
    {
        var pitchClass = ((pitch % 12) + 12) % 12;
        return pitchClass is 1 or 3 or 6 or 8 or 10;
    }
}

public sealed class MidiChart
{
    public MidiChart(string sourcePath, IReadOnlyList<MidiNote> notes, string error = "")
    {
        SourcePath = sourcePath;
        Notes = notes;
        Error = error;
        DurationSec = notes.Count == 0 ? 0.0 : notes.Max(note => note.EndSec);
    }

    public string SourcePath { get; }
    public IReadOnlyList<MidiNote> Notes { get; }
    public string Error { get; }
    public double DurationSec { get; }
    public bool IsValid => string.IsNullOrEmpty(Error);

    public static MidiChart Empty(string sourcePath = "", string error = "") =>
        new(sourcePath, Array.Empty<MidiNote>(), error);
}
