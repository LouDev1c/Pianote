using System;
using System.Collections.Generic;
using System.Linq;

namespace Pianote.ChartConverter;

public static class PianoHandAssigner
{
    public static IReadOnlyList<NoteHand> AssignHands(
        IReadOnlyList<HandAssignmentNote> notes,
        ChartConversionOptions? options = null)
    {
        options ??= new ChartConversionOptions();
        if (notes.Count == 0)
            return Array.Empty<NoteHand>();

        if (options.HandAssignmentStrategy == HandAssignmentStrategy.FixedSplit)
            return notes
                .Select(note => note.Pitch < options.HandSplitPitch ? NoteHand.Left : NoteHand.Right)
                .ToArray();

        var groups = BuildOnsetGroups(notes, Math.Max(0.0, options.ChordOnsetToleranceSec));
        var candidateLayers = groups.Select(group => BuildCandidates(group, options)).ToList();
        var stateLayers = new List<List<BeamState>>(groups.Count);
        var previousStates = new List<BeamState>
        {
            new(
                0.0,
                options.HandSplitPitch - 7.0,
                options.HandSplitPitch + 7.0,
                double.NegativeInfinity,
                double.NegativeInfinity,
                false,
                -1,
                -1)
        };

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var candidates = candidateLayers[groupIndex];
            var nextStates = new List<BeamState>(previousStates.Count * candidates.Count);

            for (var previousIndex = 0; previousIndex < previousStates.Count; previousIndex++)
            {
                var previous = previousStates[previousIndex];
                for (var candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    var candidate = candidates[candidateIndex];
                    var leftCenter = candidate.LeftCount > 0 ? candidate.LeftCenter : previous.LeftCenter;
                    var rightCenter = candidate.RightCount > 0 ? candidate.RightCenter : previous.RightCenter;
                    var leftTime = candidate.LeftCount > 0 ? group.StartSec : previous.LeftTime;
                    var rightTime = candidate.RightCount > 0 ? group.StartSec : previous.RightTime;
                    var crossed = leftCenter > rightCenter;

                    var cost = previous.Cost + candidate.BaseCost;
                    if (candidate.LeftCount > 0)
                        cost += MovementCost(previous.LeftCenter, leftCenter, group.StartSec - previous.LeftTime);
                    if (candidate.RightCount > 0)
                        cost += MovementCost(previous.RightCenter, rightCenter, group.StartSec - previous.RightTime);

                    if (crossed)
                        cost += 2.8 + Math.Min(4.0, (leftCenter - rightCenter) * 0.12);
                    if (crossed != previous.Crossed &&
                        !double.IsNegativeInfinity(previous.LeftTime) &&
                        !double.IsNegativeInfinity(previous.RightTime))
                    {
                        cost += 5.5;
                    }

                    nextStates.Add(new BeamState(
                        cost,
                        leftCenter,
                        rightCenter,
                        leftTime,
                        rightTime,
                        crossed,
                        previousIndex,
                        candidateIndex));
                }
            }

            var beamWidth = Math.Clamp(options.HandAssignmentBeamWidth, 8, 256);
            var beam = nextStates
                .OrderBy(state => state.Cost)
                .Take(beamWidth)
                .ToList();
            stateLayers.Add(beam);
            previousStates = beam;
        }

        var assignments = Enumerable.Repeat(NoteHand.Right, notes.Count).ToArray();
        var stateIndex = 0;
        for (var groupIndex = groups.Count - 1; groupIndex >= 0; groupIndex--)
        {
            var state = stateLayers[groupIndex][stateIndex];
            var group = groups[groupIndex];
            var candidate = candidateLayers[groupIndex][state.CandidateIndex];
            for (var noteIndex = 0; noteIndex < group.Notes.Count; noteIndex++)
                assignments[group.Notes[noteIndex].Index] = candidate.LeftAssignments[noteIndex]
                    ? NoteHand.Left
                    : NoteHand.Right;

            stateIndex = state.PreviousStateIndex;
        }

        return assignments;
    }

    private static List<OnsetGroup> BuildOnsetGroups(
        IReadOnlyList<HandAssignmentNote> notes,
        double toleranceSec)
    {
        var ordered = notes.OrderBy(note => note.StartSec).ThenBy(note => note.Pitch).ToList();
        var groups = new List<OnsetGroup>();
        var index = 0;

        while (index < ordered.Count)
        {
            var startSec = ordered[index].StartSec;
            var groupNotes = new List<HandAssignmentNote>();
            while (index < ordered.Count && ordered[index].StartSec - startSec <= toleranceSec)
            {
                groupNotes.Add(ordered[index]);
                index++;
            }

            groupNotes.Sort((left, right) =>
            {
                var pitchComparison = left.Pitch.CompareTo(right.Pitch);
                return pitchComparison != 0 ? pitchComparison : left.Index.CompareTo(right.Index);
            });
            groups.Add(new OnsetGroup(startSec, groupNotes));
        }

        return groups;
    }

    private static List<AssignmentCandidate> BuildCandidates(
        OnsetGroup group,
        ChartConversionOptions options)
    {
        var candidates = new List<AssignmentCandidate>();
        var noteCount = group.Notes.Count;

        for (var split = 0; split <= noteCount; split++)
        {
            var leftAssignments = new bool[noteCount];
            for (var index = 0; index < split; index++)
                leftAssignments[index] = true;
            candidates.Add(CreateCandidate(group, leftAssignments, options));
        }

        if (options.AllowHandCrossing && noteCount > 1)
        {
            for (var split = 1; split < noteCount; split++)
            {
                var leftAssignments = new bool[noteCount];
                for (var index = split; index < noteCount; index++)
                    leftAssignments[index] = true;
                candidates.Add(CreateCandidate(group, leftAssignments, options));
            }
        }

        return candidates;
    }

    private static AssignmentCandidate CreateCandidate(
        OnsetGroup group,
        bool[] leftAssignments,
        ChartConversionOptions options)
    {
        var leftPitches = new List<int>();
        var rightPitches = new List<int>();
        var pitchPriorCost = 0.0;

        for (var index = 0; index < group.Notes.Count; index++)
        {
            var pitch = group.Notes[index].Pitch;
            if (leftAssignments[index])
            {
                leftPitches.Add(pitch);
                pitchPriorCost += Math.Max(0, pitch - options.HandSplitPitch) * 0.055;
            }
            else
            {
                rightPitches.Add(pitch);
                pitchPriorCost += Math.Max(0, options.HandSplitPitch - pitch) * 0.055;
            }
        }

        var leftCenter = Center(leftPitches);
        var rightCenter = Center(rightPitches);
        var cost = pitchPriorCost +
                   SpanCost(leftPitches, options) +
                   SpanCost(rightPitches, options) +
                   FingerCountCost(leftPitches.Count) +
                   FingerCountCost(rightPitches.Count);

        if (leftPitches.Count > 0 && rightPitches.Count > 0 && leftCenter > rightCenter)
            cost += 1.5;

        return new AssignmentCandidate(
            leftAssignments,
            leftPitches.Count,
            rightPitches.Count,
            leftCenter,
            rightCenter,
            cost);
    }

    private static double SpanCost(IReadOnlyList<int> pitches, ChartConversionOptions options)
    {
        if (pitches.Count < 2)
            return 0.0;

        var span = pitches[^1] - pitches[0];
        var comfortable = Math.Max(1, options.ComfortableHandSpanSemitones);
        var maximum = Math.Max(comfortable, options.MaximumHandSpanSemitones);
        var excess = Math.Max(0, span - comfortable);
        var cost = excess * excess * 0.42;

        if (span > maximum)
        {
            var impossibleExcess = span - maximum;
            cost += 28.0 + impossibleExcess * impossibleExcess * 4.0;
        }

        return cost;
    }

    private static double FingerCountCost(int noteCount)
    {
        var excess = Math.Max(0, noteCount - 5);
        return excess * excess * 9.0;
    }

    private static double MovementCost(double previousCenter, double center, double inactiveSec)
    {
        if (double.IsNegativeInfinity(inactiveSec) || inactiveSec > 2.0)
            return Math.Abs(center - previousCenter) * 0.015;

        var distance = Math.Abs(center - previousCenter);
        var freeDistance = 4.5 + Math.Clamp(inactiveSec, 0.0, 2.0) * 8.0;
        var excess = Math.Max(0.0, distance - freeDistance);
        return distance * 0.025 + excess * excess * 0.24;
    }

    private static double Center(IReadOnlyList<int> pitches)
    {
        if (pitches.Count == 0)
            return 0.0;
        return pitches.Average();
    }

    private sealed record OnsetGroup(double StartSec, List<HandAssignmentNote> Notes);

    private sealed record AssignmentCandidate(
        bool[] LeftAssignments,
        int LeftCount,
        int RightCount,
        double LeftCenter,
        double RightCenter,
        double BaseCost);

    private sealed record BeamState(
        double Cost,
        double LeftCenter,
        double RightCenter,
        double LeftTime,
        double RightTime,
        bool Crossed,
        int PreviousStateIndex,
        int CandidateIndex);
}
