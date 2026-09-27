using Godot;
using Pianote.ChartConverter;
using System;
using System.IO;
using System.Linq;

namespace Pianote.Core;

public static class MidiChartLoader
{
    public static MidiChart LoadFromGodotPath(string path, int handSplitPitch = 60)
    {
        return LoadFromGodotPath(path, new ChartConversionOptions { HandSplitPitch = handSplitPitch });
    }

    public static MidiChart LoadFromGodotPath(string path, ChartConversionOptions options)
    {
        if (string.IsNullOrWhiteSpace(path))
            return MidiChart.Empty(path, "No MIDI chart path was provided.");

        if (path.StartsWith("res://", StringComparison.Ordinal) ||
            path.StartsWith("user://", StringComparison.Ordinal))
        {
            if (!Godot.FileAccess.FileExists(path))
                return MidiChart.Empty(path, $"MIDI chart not found: {path}");

            return LoadFromBytes(Godot.FileAccess.GetFileAsBytes(path), path, options);
        }

        return LoadFromFileSystemPath(path, path, options);
    }

    public static MidiChart LoadFromFileSystemPath(
        string resolvedPath,
        string sourcePath = "",
        int handSplitPitch = 60,
        string cacheOutputPath = "")
    {
        return LoadFromFileSystemPath(
            resolvedPath,
            sourcePath,
            new ChartConversionOptions { HandSplitPitch = handSplitPitch },
            cacheOutputPath);
    }

    public static MidiChart LoadFromFileSystemPath(
        string resolvedPath,
        string sourcePath,
        ChartConversionOptions options,
        string cacheOutputPath = "")
    {
        if (!File.Exists(resolvedPath))
            return MidiChart.Empty(sourcePath, $"MIDI chart not found: {sourcePath}");

        try
        {
            var converted = MidiChartConverter.ConvertFile(resolvedPath, options);

            if (!string.IsNullOrWhiteSpace(cacheOutputPath))
                MidiChartConverter.WriteJson(converted, cacheOutputPath);

            return ToGodotChart(converted, sourcePath);
        }
        catch (Exception ex)
        {
            return MidiChart.Empty(sourcePath, $"Failed to convert MIDI chart: {ex.Message}");
        }
    }

    public static MidiChart LoadFromBytes(
        byte[] bytes,
        string sourcePath = "",
        int handSplitPitch = 60,
        string cacheOutputPath = "")
    {
        return LoadFromBytes(
            bytes,
            sourcePath,
            new ChartConversionOptions { HandSplitPitch = handSplitPitch },
            cacheOutputPath);
    }

    public static MidiChart LoadFromBytes(
        byte[] bytes,
        string sourcePath,
        ChartConversionOptions options,
        string cacheOutputPath = "")
    {
        try
        {
            var converted = MidiChartConverter.ConvertBytes(bytes, sourcePath, options);

            if (!string.IsNullOrWhiteSpace(cacheOutputPath))
                MidiChartConverter.WriteJson(converted, cacheOutputPath);

            return ToGodotChart(converted, sourcePath);
        }
        catch (Exception ex)
        {
            return MidiChart.Empty(sourcePath, $"Failed to convert MIDI chart: {ex.Message}");
        }
    }

    private static MidiChart ToGodotChart(ChartDocument chart, string sourcePath)
    {
        var notes = chart.Notes
            .Select(note => new MidiNote(
                note.Index,
                note.Pitch,
                note.Channel,
                note.Track,
                note.StartSec,
                note.DurationSec,
                note.Velocity,
                note.Hand == NoteHand.Left ? PianoHand.Left : PianoHand.Right,
                note.IsBlackKey))
            .ToList();

        return new MidiChart(sourcePath, notes);
    }
}
