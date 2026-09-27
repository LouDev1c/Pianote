namespace Pianote.AudioCapture;

public static class WavFileWriter
{
    public static void Write16BitMono(string path, IReadOnlyList<short> samples, int sampleRate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Count * sizeof(short);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(AudioCaptureSettings.Channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * AudioCaptureSettings.Channels * AudioCaptureSettings.BitsPerSample / 8);
        writer.Write((short)(AudioCaptureSettings.Channels * AudioCaptureSettings.BitsPerSample / 8));
        writer.Write(AudioCaptureSettings.BitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        foreach (var sample in samples)
            writer.Write(sample);
    }
}
