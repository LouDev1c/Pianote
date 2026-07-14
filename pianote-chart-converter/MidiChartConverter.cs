using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Pianote.ChartConverter;

public static class MidiChartConverter
{
    private const int DefaultTempoMicrosecondsPerQuarter = 500000;

    public static ChartDocument ConvertFile(string midiPath, ChartConversionOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(midiPath))
            throw new ArgumentException("A MIDI file path is required.", nameof(midiPath));
        if (!File.Exists(midiPath))
            throw new FileNotFoundException("MIDI file not found.", midiPath);

        return ConvertBytes(File.ReadAllBytes(midiPath), midiPath, options);
    }

    public static ChartDocument ConvertBytes(
        byte[] bytes,
        string sourcePath = "",
        ChartConversionOptions? options = null)
    {
        options ??= new ChartConversionOptions();
        var reader = new MidiReader(bytes);

        if (reader.ReadAscii(4) != "MThd")
            throw new InvalidDataException("The file is not a Standard MIDI File.");

        var headerLength = checked((int)reader.ReadUInt32());
        if (headerLength < 6)
            throw new InvalidDataException("The MIDI header is shorter than six bytes.");

        var format = reader.ReadUInt16();
        var trackCount = reader.ReadUInt16();
        var division = reader.ReadUInt16();
        reader.Skip(headerLength - 6);

        if (format > 1)
            throw new NotSupportedException("MIDI format 2 is not supported because its tracks have independent timelines.");
        if ((division & 0x8000) != 0)
            throw new NotSupportedException("SMPTE MIDI timing is not supported.");
        if (division == 0)
            throw new InvalidDataException("The MIDI ticks-per-quarter value is zero.");

        var rawNotes = new List<RawNote>();
        var tempos = new List<TempoEvent> { new(0, DefaultTempoMicrosecondsPerQuarter) };

        for (var track = 0; track < trackCount; track++)
        {
            if (!reader.HasBytes(8))
                throw new EndOfStreamException("The MIDI file ended before all declared tracks were read.");

            var chunkId = reader.ReadAscii(4);
            var chunkLength = checked((int)reader.ReadUInt32());
            var chunkEnd = checked(reader.Position + chunkLength);
            if (!reader.HasBytes(chunkLength))
                throw new EndOfStreamException("A MIDI track extends beyond the end of the file.");

            if (chunkId == "MTrk")
                ParseTrack(reader, chunkEnd, track, rawNotes, tempos);

            reader.Position = chunkEnd;
        }

        var orderedTempos = tempos
            .GroupBy(tempo => tempo.Tick)
            .Select(group => group.Last())
            .OrderBy(tempo => tempo.Tick)
            .ToList();

        var timedNotes = rawNotes
            .OrderBy(note => note.StartTick)
            .ThenBy(note => note.Pitch)
            .ThenBy(note => note.Track)
            .Select((note, index) => CreateHandAssignmentNote(index, note, orderedTempos, division, options))
            .ToList();

        var handAssignments = PianoHandAssigner.AssignHands(timedNotes, options);
        var notes = timedNotes
            .Select(note => new ChartNote(
                note.Index,
                note.Pitch,
                note.Channel,
                note.Track,
                note.StartSec,
                note.DurationSec,
                note.Velocity,
                handAssignments[note.Index],
                IsBlackKey(note.Pitch)))
            .ToList();

        return new ChartDocument(sourcePath, division, notes);
    }

    public static void WriteJson(ChartDocument chart, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(chart, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json, new UTF8Encoding(false));
    }

    public static bool IsBlackKey(int pitch)
    {
        var pitchClass = ((pitch % 12) + 12) % 12;
        return pitchClass is 1 or 3 or 6 or 8 or 10;
    }

    private static HandAssignmentNote CreateHandAssignmentNote(
        int index,
        RawNote note,
        IReadOnlyList<TempoEvent> tempos,
        int ticksPerQuarter,
        ChartConversionOptions options)
    {
        var startSec = TicksToSeconds(note.StartTick, tempos, ticksPerQuarter);
        var endSec = TicksToSeconds(note.EndTick, tempos, ticksPerQuarter);
        return new HandAssignmentNote(
            index,
            note.Pitch,
            note.Channel,
            note.Track,
            startSec,
            Math.Max(options.MinimumNoteDurationSec, endSec - startSec),
            note.Velocity / 127.0f);
    }

    private static void ParseTrack(
        MidiReader reader,
        int trackEnd,
        int trackIndex,
        List<RawNote> notes,
        List<TempoEvent> tempos)
    {
        var openNotes = new Dictionary<int, Queue<OpenNote>>();
        int? runningStatus = null;
        long tick = 0;

        while (reader.Position < trackEnd)
        {
            tick += reader.ReadVariableLengthQuantity();
            var status = reader.ReadByte();

            if (status < 0x80)
            {
                if (runningStatus == null)
                    throw new InvalidDataException("MIDI running status appeared before a channel status byte.");

                reader.Position--;
                status = runningStatus.Value;
            }
            else if (status < 0xF0)
            {
                runningStatus = status;
            }

            if (status == 0xFF)
            {
                var metaType = reader.ReadByte();
                var length = checked((int)reader.ReadVariableLengthQuantity());
                if (metaType == 0x51 && length == 3)
                {
                    var tempo = (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
                    if (tempo > 0)
                        tempos.Add(new TempoEvent(tick, tempo));
                }
                else
                {
                    reader.Skip(length);
                }

                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                reader.Skip(checked((int)reader.ReadVariableLengthQuantity()));
                continue;
            }

            var eventType = status & 0xF0;
            var channel = status & 0x0F;
            if (eventType is 0xC0 or 0xD0)
            {
                reader.Skip(1);
                continue;
            }

            var pitchOrData = reader.ReadByte();
            var velocityOrData = reader.ReadByte();
            if (eventType == 0x90 && velocityOrData > 0)
            {
                var key = channel * 128 + pitchOrData;
                if (!openNotes.TryGetValue(key, out var queue))
                {
                    queue = new Queue<OpenNote>();
                    openNotes[key] = queue;
                }

                queue.Enqueue(new OpenNote(tick, velocityOrData));
            }
            else if (eventType is 0x80 or 0x90)
            {
                var key = channel * 128 + pitchOrData;
                if (!openNotes.TryGetValue(key, out var queue) || queue.Count == 0)
                    continue;

                var opened = queue.Dequeue();
                if (tick > opened.Tick)
                    notes.Add(new RawNote(pitchOrData, channel, trackIndex, opened.Tick, tick, opened.Velocity));
            }
        }
    }

    private static double TicksToSeconds(long targetTick, IReadOnlyList<TempoEvent> tempos, int ticksPerQuarter)
    {
        var seconds = 0.0;
        var previousTick = 0L;
        var microsecondsPerQuarter = DefaultTempoMicrosecondsPerQuarter;

        foreach (var tempo in tempos)
        {
            if (tempo.Tick > targetTick)
                break;

            seconds += (tempo.Tick - previousTick) * microsecondsPerQuarter / 1_000_000.0 / ticksPerQuarter;
            previousTick = tempo.Tick;
            microsecondsPerQuarter = tempo.MicrosecondsPerQuarter;
        }

        seconds += (targetTick - previousTick) * microsecondsPerQuarter / 1_000_000.0 / ticksPerQuarter;
        return seconds;
    }

    private readonly record struct RawNote(
        int Pitch,
        int Channel,
        int Track,
        long StartTick,
        long EndTick,
        int Velocity);

    private readonly record struct OpenNote(long Tick, int Velocity);
    private readonly record struct TempoEvent(long Tick, int MicrosecondsPerQuarter);

    private sealed class MidiReader
    {
        private readonly byte[] _bytes;

        public MidiReader(byte[] bytes)
        {
            _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public int Position { get; set; }
        public bool HasBytes(int count) => count >= 0 && Position + count <= _bytes.Length;

        public int ReadByte()
        {
            if (!HasBytes(1))
                throw new EndOfStreamException("Unexpected end of MIDI data.");
            return _bytes[Position++];
        }

        public ushort ReadUInt16() => (ushort)((ReadByte() << 8) | ReadByte());

        public uint ReadUInt32() =>
            ((uint)ReadByte() << 24) | ((uint)ReadByte() << 16) | ((uint)ReadByte() << 8) | (uint)ReadByte();

        public string ReadAscii(int count)
        {
            if (!HasBytes(count))
                throw new EndOfStreamException("Unexpected end of MIDI data.");

            var value = Encoding.ASCII.GetString(_bytes, Position, count);
            Position += count;
            return value;
        }

        public long ReadVariableLengthQuantity()
        {
            long value = 0;
            var byteCount = 0;
            int current;
            do
            {
                if (++byteCount > 4)
                    throw new InvalidDataException("A MIDI variable-length value exceeds four bytes.");
                current = ReadByte();
                value = (value << 7) | (uint)(current & 0x7F);
            }
            while ((current & 0x80) != 0);

            return value;
        }

        public void Skip(int count)
        {
            if (count < 0 || !HasBytes(count))
                throw new EndOfStreamException("Unexpected end of MIDI data.");
            Position += count;
        }
    }
}
