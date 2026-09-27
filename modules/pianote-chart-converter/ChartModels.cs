using System.Collections.Generic;
using System.Linq;

namespace Pianote.ChartConverter;

public enum NoteHand
{
    Left,
    Right
}

public enum HandAssignmentStrategy
{
    FixedSplit,
    Contextual
}

public sealed class ChartNote
{
    public ChartNote(
        int index,
        int pitch,
        int channel,
        int track,
        double startSec,
        double durationSec,
        float velocity,
        NoteHand hand,
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
    public NoteHand Hand { get; }
    public bool IsBlackKey { get; }
}

public sealed class ChartDocument
{
    public ChartDocument(string sourcePath, int ticksPerQuarter, IReadOnlyList<ChartNote> notes)
    {
        SourcePath = sourcePath;
        TicksPerQuarter = ticksPerQuarter;
        Notes = notes;
        DurationSec = notes.Count == 0 ? 0.0 : notes.Max(note => note.EndSec);
        MinPitch = notes.Count == 0 ? 0 : notes.Min(note => note.Pitch);
        MaxPitch = notes.Count == 0 ? 0 : notes.Max(note => note.Pitch);
    }

    public int FormatVersion => 1;
    public string SourcePath { get; }
    public int TicksPerQuarter { get; }
    public IReadOnlyList<ChartNote> Notes { get; }
    public double DurationSec { get; }
    public int MinPitch { get; }
    public int MaxPitch { get; }
}

public sealed class ChartConversionOptions
{
    public int HandSplitPitch { get; init; } = 60;
    public double MinimumNoteDurationSec { get; init; } = 0.03;
    public HandAssignmentStrategy HandAssignmentStrategy { get; init; } = HandAssignmentStrategy.Contextual;
    public double ChordOnsetToleranceSec { get; init; } = 0.045;
    public int ComfortableHandSpanSemitones { get; init; } = 12;
    public int MaximumHandSpanSemitones { get; init; } = 17;
    public int HandAssignmentBeamWidth { get; init; } = 64;
    public bool AllowHandCrossing { get; init; } = true;
}

public sealed class HandAssignmentNote
{
    public HandAssignmentNote(
        int index,
        int pitch,
        int channel,
        int track,
        double startSec,
        double durationSec,
        float velocity)
    {
        Index = index;
        Pitch = pitch;
        Channel = channel;
        Track = track;
        StartSec = startSec;
        DurationSec = durationSec;
        Velocity = velocity;
    }

    public int Index { get; }
    public int Pitch { get; }
    public int Channel { get; }
    public int Track { get; }
    public double StartSec { get; }
    public double DurationSec { get; }
    public float Velocity { get; }
}
