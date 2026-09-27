using System.Globalization;
using System.Text.Json;
using Pianote.AudioCapture;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var options = ParseOptions(args.Skip(1).ToArray());

try
{
    var result = command switch
    {
        "check" => Check(),
        "probe" => await Probe(),
        "record" => await Record(options),
        "calibrate" => await Calibrate(options),
        "stream-info" => StreamInfo(),
        _ => new Dictionary<string, object?> { ["ok"] = false, ["error"] = "Unknown audio-capture command." },
    };

    WriteStatus(options, result);
    return Convert.ToBoolean(result["ok"], CultureInfo.InvariantCulture) ? 0 : 1;
}
catch (Exception ex)
{
    WriteStatus(options, new Dictionary<string, object?> { ["ok"] = false, ["error"] = ex.Message });
    return 1;
}

static Dictionary<string, object?> Check()
{
    return new Dictionary<string, object?>
    {
        ["ok"] = WindowsMicrophoneCapture.IsSupported,
        ["platform"] = OperatingSystem.IsWindows() ? "windows" : "unsupported",
        ["stream_chunk_ms"] = AudioCaptureSettings.StreamChunkInterval.TotalMilliseconds,
        ["error"] = WindowsMicrophoneCapture.IsSupported ? null : "The current audio capture backend supports Windows only.",
    };
}

static async Task<Dictionary<string, object?>> Probe()
{
    if (!WindowsMicrophoneCapture.HasInputDevice())
        return new Dictionary<string, object?> { ["ok"] = false, ["error"] = "No microphone input device was found." };

    using var capture = new WindowsMicrophoneCapture();
    await capture.RecordAsync(TimeSpan.FromMilliseconds(200));
    return new Dictionary<string, object?> { ["ok"] = true };
}

static async Task<Dictionary<string, object?>> Record(Dictionary<string, string> options)
{
    var output = Required(options, "output");
    var duration = GetDouble(options, "duration", 5.0);
    var startDelay = GetDouble(options, "start-delay", 0.0);
    var readyStatus = options.GetValueOrDefault("ready-status", "");

    using var capture = new WindowsMicrophoneCapture();
    if (startDelay > 0.0)
        await Task.Delay(TimeSpan.FromSeconds(startDelay));

    var samples = await capture.RecordAsync(
        TimeSpan.FromSeconds(duration),
        () => WriteReadyIfNeeded(readyStatus));
    WavFileWriter.Write16BitMono(output, samples, AudioCaptureSettings.SampleRate);

    return new Dictionary<string, object?>
    {
        ["ok"] = true,
        ["output"] = output,
        ["duration"] = duration,
        ["sample_rate"] = AudioCaptureSettings.SampleRate,
    };
}

static async Task<Dictionary<string, object?>> Calibrate(Dictionary<string, string> options)
{
    var output = options.GetValueOrDefault("output", "");
    var expectedSeconds = GetDouble(options, "expected", 1.0);
    var duration = GetDouble(options, "duration", 3.0);
    var readyStatus = options.GetValueOrDefault("ready-status", "");

    using var capture = new WindowsMicrophoneCapture();
    var samples = await capture.RecordAsync(
        TimeSpan.FromSeconds(duration),
        () => WriteReadyIfNeeded(readyStatus));
    if (!string.IsNullOrWhiteSpace(output))
        WavFileWriter.Write16BitMono(output, samples, AudioCaptureSettings.SampleRate);

    var onset = CalibrationAnalyzer.DetectOnsetSeconds(samples, AudioCaptureSettings.SampleRate);
    if (onset == null)
        return new Dictionary<string, object?> { ["ok"] = false, ["error"] = "No clear piano onset was detected." };

    var difference = onset.Value - expectedSeconds;
    return new Dictionary<string, object?>
    {
        ["ok"] = true,
        ["onset_seconds"] = onset.Value,
        ["expected_seconds"] = expectedSeconds,
        ["difference_seconds"] = difference,
        ["delay_seconds"] = Math.Max(0.0, difference),
    };
}

static Dictionary<string, object?> StreamInfo()
{
    return new Dictionary<string, object?>
    {
        ["ok"] = true,
        ["stream_chunk_ms"] = AudioCaptureSettings.StreamChunkInterval.TotalMilliseconds,
        ["sample_rate"] = AudioCaptureSettings.SampleRate,
        ["channels"] = AudioCaptureSettings.Channels,
    };
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            continue;

        var key = args[i][2..];
        var value = "true";
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            value = args[++i];
        options[key] = value;
    }

    return options;
}

static string Required(Dictionary<string, string> options, string key)
{
    if (options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        return value;

    throw new ArgumentException($"Missing --{key}.");
}

static double GetDouble(Dictionary<string, string> options, string key, double fallback)
{
    if (!options.TryGetValue(key, out var value))
        return fallback;

    return double.Parse(value, CultureInfo.InvariantCulture);
}

static void WriteStatus(Dictionary<string, string> options, Dictionary<string, object?> result)
{
    if (options.TryGetValue("status", out var statusPath) && !string.IsNullOrWhiteSpace(statusPath))
        WriteJsonFile(statusPath, result);
    else
        Console.WriteLine(JsonSerializer.Serialize(result));
}

static void WriteJsonFile(string path, Dictionary<string, object?> result)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
    File.WriteAllText(path, JsonSerializer.Serialize(result));
}

static void WriteReadyIfNeeded(string readyStatus)
{
    if (!string.IsNullOrWhiteSpace(readyStatus))
        WriteJsonFile(readyStatus, new Dictionary<string, object?> { ["ok"] = true, ["ready"] = true });
}
