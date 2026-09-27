using Pianote.ChartConverter;

if (args.Length is < 1 or > 4)
{
    Console.Error.WriteLine("Usage: Pianote.ChartConverter.Cli <score.midi> [output.json] [hand-split-pitch] [contextual|fixed]");
    return 2;
}

try
{
    var splitPitch = args.Length >= 3 ? int.Parse(args[2]) : 60;
    var strategy = args.Length >= 4 && args[3].Equals("fixed", StringComparison.OrdinalIgnoreCase)
        ? HandAssignmentStrategy.FixedSplit
        : HandAssignmentStrategy.Contextual;
    var chart = MidiChartConverter.ConvertFile(
        args[0],
        new ChartConversionOptions
        {
            HandSplitPitch = splitPitch,
            HandAssignmentStrategy = strategy
        });

    if (args.Length >= 2 && args[1] != "-")
        MidiChartConverter.WriteJson(chart, args[1]);

    var leftNotes = chart.Notes.Count(note => note.Hand == NoteHand.Left);
    var rightNotes = chart.Notes.Count - leftNotes;
    var tracks = string.Join(",", chart.Notes
        .GroupBy(note => note.Track)
        .OrderBy(group => group.Key)
        .Select(group => $"{group.Key}:{group.Count()}"));
    var channels = string.Join(",", chart.Notes
        .GroupBy(note => note.Channel)
        .OrderBy(group => group.Key)
        .Select(group => $"{group.Key}:{group.Count()}"));
    var diagnostics = GetHandDiagnostics(chart.Notes, 0.045, 17, 0.25, 17);
    Console.WriteLine($"notes={chart.Notes.Count}");
    Console.WriteLine($"duration_sec={chart.DurationSec:F3}");
    Console.WriteLine($"pitch_range={chart.MinPitch}-{chart.MaxPitch}");
    Console.WriteLine($"left_notes={leftNotes}");
    Console.WriteLine($"right_notes={rightNotes}");
    Console.WriteLine($"tracks={tracks}");
    Console.WriteLine($"channels={channels}");
    Console.WriteLine($"over_span_groups={diagnostics.OverSpanGroups}");
    Console.WriteLine($"rapid_large_jumps={diagnostics.RapidLargeJumps}");
    return chart.Notes.Count > 0 ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static (int OverSpanGroups, int RapidLargeJumps) GetHandDiagnostics(
    IReadOnlyList<ChartNote> notes,
    double chordToleranceSec,
    int maximumSpan,
    double rapidWindowSec,
    int largeJumpSemitones)
{
    var ordered = notes.OrderBy(note => note.StartSec).ThenBy(note => note.Pitch).ToList();
    var overSpanGroups = 0;
    var rapidLargeJumps = 0;
    double? previousLeftCenter = null;
    double? previousRightCenter = null;
    var previousLeftTime = double.NegativeInfinity;
    var previousRightTime = double.NegativeInfinity;

    for (var index = 0; index < ordered.Count;)
    {
        var startSec = ordered[index].StartSec;
        var group = new List<ChartNote>();
        while (index < ordered.Count && ordered[index].StartSec - startSec <= chordToleranceSec)
            group.Add(ordered[index++]);

        CheckHand(group.Where(note => note.Hand == NoteHand.Left).ToList(), ref previousLeftCenter, ref previousLeftTime);
        CheckHand(group.Where(note => note.Hand == NoteHand.Right).ToList(), ref previousRightCenter, ref previousRightTime);

        void CheckHand(List<ChartNote> handNotes, ref double? previousCenter, ref double previousTime)
        {
            if (handNotes.Count == 0)
                return;

            var pitches = handNotes.Select(note => note.Pitch).ToList();
            if (pitches.Max() - pitches.Min() > maximumSpan)
                overSpanGroups++;

            var center = pitches.Average();
            if (previousCenter.HasValue &&
                startSec - previousTime <= rapidWindowSec &&
                Math.Abs(center - previousCenter.Value) > largeJumpSemitones)
            {
                rapidLargeJumps++;
            }

            previousCenter = center;
            previousTime = startSec;
        }
    }

    return (overSpanGroups, rapidLargeJumps);
}
