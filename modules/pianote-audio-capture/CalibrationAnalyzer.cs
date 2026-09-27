namespace Pianote.AudioCapture;

public static class CalibrationAnalyzer
{
    public static double? DetectOnsetSeconds(IReadOnlyList<short> samples, int sampleRate, double ignoreFirstSeconds = 0.08)
    {
        if (samples.Count == 0)
            return null;

        var windowSize = Math.Max(128, sampleRate / 100);
        var ignoreSamples = Math.Min(samples.Count, (int)(ignoreFirstSeconds * sampleRate));
        var baselineEnd = Math.Min(samples.Count, Math.Max(ignoreSamples + sampleRate / 2, windowSize));
        var baseline = ComputeRms(samples, ignoreSamples, baselineEnd - ignoreSamples);
        var threshold = Math.Max(
            AudioCaptureSettings.OnsetMinimumAmplitude,
            baseline * AudioCaptureSettings.OnsetThresholdMultiplier);

        for (var start = ignoreSamples; start + windowSize <= samples.Count; start += windowSize / 4)
        {
            var rms = ComputeRms(samples, start, windowSize);
            if (rms >= threshold)
                return start / (double)sampleRate;
        }

        return null;
    }

    private static double ComputeRms(IReadOnlyList<short> samples, int start, int count)
    {
        if (count <= 0)
            return 0.0;

        var end = Math.Min(samples.Count, start + count);
        var sum = 0.0;
        for (var i = start; i < end; i++)
        {
            var normalized = samples[i] / 32768.0;
            sum += normalized * normalized;
        }

        return Math.Sqrt(sum / Math.Max(1, end - start));
    }
}
