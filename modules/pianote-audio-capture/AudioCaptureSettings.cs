namespace Pianote.AudioCapture;

public static class AudioCaptureSettings
{
    public const int SampleRate = 44100;
    public const short Channels = 1;
    public const short BitsPerSample = 16;

    // The game streaming interval should be aligned with the later AMT or
    // score-following module. Keep it editable here instead of hard-coding it.
    public static TimeSpan StreamChunkInterval { get; set; } = TimeSpan.FromMilliseconds(20);

    public static double OnsetThresholdMultiplier { get; set; } = 6.0;
    public static double OnsetMinimumAmplitude { get; set; } = 0.045;
}
