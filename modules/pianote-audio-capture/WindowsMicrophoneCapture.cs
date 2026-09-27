using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Pianote.AudioCapture;

public sealed class WindowsMicrophoneCapture : IDisposable
{
    private const int CALLBACK_FUNCTION = 0x00030000;
    private const int WIM_DATA = 0x3C0;
    private const int MMSYSERR_NOERROR = 0;
    private const int WAVE_FORMAT_PCM = 1;
    private const int WAVE_MAPPER = -1;

    private readonly int _bufferMilliseconds;
    private readonly List<GCHandle> _pins = [];
    private readonly List<IntPtr> _allocatedData = [];
    private readonly ConcurrentQueue<short[]> _buffers = new();
    private WaveInProc? _callback;
    private IntPtr _handle;
    private bool _recording;

    public WindowsMicrophoneCapture(int bufferMilliseconds = 50)
    {
        _bufferMilliseconds = Math.Max(10, bufferMilliseconds);
    }

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool HasInputDevice()
    {
        return IsSupported && waveInGetNumDevs() > 0;
    }

    public async Task<List<short>> RecordAsync(TimeSpan duration, Action? onReady = null, CancellationToken cancellationToken = default)
    {
        if (!HasInputDevice())
            throw new InvalidOperationException("No Windows microphone input device is available.");

        Start();
        onReady?.Invoke();
        try
        {
            var targetSamples = Math.Max(1, (int)(AudioCaptureSettings.SampleRate * duration.TotalSeconds));
            var result = new List<short>(targetSamples);

            while (result.Count < targetSamples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (_buffers.TryDequeue(out var buffer))
                {
                    var remaining = targetSamples - result.Count;
                    if (buffer.Length <= remaining)
                    {
                        result.AddRange(buffer);
                    }
                    else
                    {
                        result.AddRange(buffer.AsSpan(0, remaining).ToArray());
                    }

                    if (result.Count >= targetSamples)
                        break;
                }

                if (result.Count < targetSamples)
                    await Task.Delay(5, cancellationToken);
            }

            return result;
        }
        finally
        {
            Stop();
        }
    }

    private void Start()
    {
        if (_recording)
            return;

        _callback = OnWaveIn;
        var format = new WaveFormat
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = AudioCaptureSettings.Channels,
            nSamplesPerSec = AudioCaptureSettings.SampleRate,
            nAvgBytesPerSec = AudioCaptureSettings.SampleRate * AudioCaptureSettings.Channels * AudioCaptureSettings.BitsPerSample / 8,
            nBlockAlign = (short)(AudioCaptureSettings.Channels * AudioCaptureSettings.BitsPerSample / 8),
            wBitsPerSample = AudioCaptureSettings.BitsPerSample,
            cbSize = 0,
        };

        ThrowOnError(waveInOpen(out _handle, WAVE_MAPPER, ref format, _callback, IntPtr.Zero, CALLBACK_FUNCTION), "waveInOpen");

        var bytesPerBuffer = AudioCaptureSettings.SampleRate * AudioCaptureSettings.Channels * AudioCaptureSettings.BitsPerSample / 8 * _bufferMilliseconds / 1000;
        bytesPerBuffer = Math.Max(bytesPerBuffer, 1024);
        bytesPerBuffer -= bytesPerBuffer % 2;

        for (var i = 0; i < 4; i++)
            AddBuffer(bytesPerBuffer);

        ThrowOnError(waveInStart(_handle), "waveInStart");
        _recording = true;
    }

    private void Stop()
    {
        if (_handle == IntPtr.Zero)
            return;

        waveInStop(_handle);
        waveInReset(_handle);
        waveInClose(_handle);
        _handle = IntPtr.Zero;
        _recording = false;

        foreach (var pin in _pins)
        {
            if (pin.IsAllocated)
                pin.Free();
        }
        _pins.Clear();
        foreach (var data in _allocatedData)
        {
            if (data != IntPtr.Zero)
                Marshal.FreeHGlobal(data);
        }
        _allocatedData.Clear();
        while (_buffers.TryDequeue(out _))
        {
        }
    }

    private void AddBuffer(int byteLength)
    {
        var data = new byte[byteLength];
        var header = new WaveHeader
        {
            lpData = Marshal.AllocHGlobal(byteLength),
            dwBufferLength = byteLength,
        };
        _allocatedData.Add(header.lpData);
        Marshal.Copy(data, 0, header.lpData, data.Length);

        var headerHandle = GCHandle.Alloc(header, GCHandleType.Pinned);
        _pins.Add(headerHandle);
        var headerPtr = headerHandle.AddrOfPinnedObject();
        ThrowOnError(waveInPrepareHeader(_handle, headerPtr, Marshal.SizeOf<WaveHeader>()), "waveInPrepareHeader");
        ThrowOnError(waveInAddBuffer(_handle, headerPtr, Marshal.SizeOf<WaveHeader>()), "waveInAddBuffer");
    }

    private void OnWaveIn(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (uMsg != WIM_DATA || dwParam1 == IntPtr.Zero)
            return;

        var header = Marshal.PtrToStructure<WaveHeader>(dwParam1);
        if (header.dwBytesRecorded > 0)
        {
            var bytes = new byte[header.dwBytesRecorded];
            Marshal.Copy(header.lpData, bytes, 0, bytes.Length);
            var samples = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
            _buffers.Enqueue(samples);
        }

        if (_handle != IntPtr.Zero)
            waveInAddBuffer(_handle, dwParam1, Marshal.SizeOf<WaveHeader>());
    }

    private static void ThrowOnError(int code, string operation)
    {
        if (code != MMSYSERR_NOERROR)
            throw new InvalidOperationException($"{operation} failed with code {code}.");
    }

    public void Dispose()
    {
        Stop();
    }

    private delegate void WaveInProc(IntPtr hwi, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short wFormatTag;
        public short nChannels;
        public int nSamplesPerSec;
        public int nAvgBytesPerSec;
        public short nBlockAlign;
        public short wBitsPerSample;
        public short cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr lpData;
        public int dwBufferLength;
        public int dwBytesRecorded;
        public IntPtr dwUser;
        public int dwFlags;
        public int dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr hWaveIn, int uDeviceID, ref WaveFormat lpFormat, WaveInProc dwCallback, IntPtr dwInstance, int dwFlags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr hWaveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInStop(IntPtr hWaveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr hWaveIn);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr hWaveIn);
}
