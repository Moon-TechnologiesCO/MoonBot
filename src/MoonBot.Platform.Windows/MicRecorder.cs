using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoonBot.Platform.Windows;

// Graba el micrófono predeterminado con la API clásica de Windows (winmm, "waveIn"):
// 16 kHz, mono, 16 bits, que es justo el formato que pide Whisper.
// Los búferes viven en memoria no administrada porque el driver escribe en ellos mientras graba.
[SupportedOSPlatform("windows")]
public sealed class MicRecorder : IDisposable
{
    public const int SampleRate = 16_000;
    private const int BufferCount = 4;
    private const int BufferBytes = SampleRate / 10 * 2; // 100 ms por búfer
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WhdrDone = 0x00000001;
    private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();

    private readonly object _gate = new();
    private readonly List<short> _samples = [];
    private IntPtr _device;
    private IntPtr[] _headers = [];
    private Thread? _pump;
    private volatile bool _running;

    public bool IsRecording => _running;

    // Volumen reciente (0..1), para animar a la luna mientras escucha.
    public float Level { get; private set; }

    public void Start()
    {
        if (_running)
            return;

        var format = new WaveFormat
        {
            FormatTag = 1, Channels = 1, SamplesPerSec = SampleRate, AvgBytesPerSec = SampleRate * 2,
            BlockAlign = 2, BitsPerSample = 16, Size = 0
        };
        var result = waveInOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
            throw new InvalidOperationException($"The microphone could not be opened (code {result}).");

        lock (_gate)
            _samples.Clear();

        _headers = new IntPtr[BufferCount];
        for (var i = 0; i < BufferCount; i++)
        {
            var header = new WaveHeader { Data = Marshal.AllocHGlobal(BufferBytes), BufferLength = BufferBytes };
            _headers[i] = Marshal.AllocHGlobal(HeaderSize);
            Marshal.StructureToPtr(header, _headers[i], false);
            waveInPrepareHeader(_device, _headers[i], (uint)HeaderSize);
            waveInAddBuffer(_device, _headers[i], (uint)HeaderSize);
        }

        _running = true;
        waveInStart(_device);
        _pump = new Thread(Pump) { IsBackground = true, Name = "MoonBot microphone" };
        _pump.Start();
    }

    // Detiene la grabación y devuelve el audio como floats entre -1 y 1.
    public float[] Stop()
    {
        if (!_running)
            return [];

        _running = false;
        _pump?.Join();
        waveInReset(_device); // devuelve los búferes pendientes marcados como "hechos"
        foreach (var header in _headers)
            Collect(header, requeue: false);
        Release();

        lock (_gate)
        {
            var result = new float[_samples.Count];
            for (var i = 0; i < result.Length; i++)
                result[i] = _samples[i] / 32768f;
            _samples.Clear();
            Level = 0;
            return result;
        }
    }

    private void Pump()
    {
        while (_running)
        {
            foreach (var header in _headers)
                Collect(header, requeue: true);
            Thread.Sleep(20);
        }
    }

    private void Collect(IntPtr headerPtr, bool requeue)
    {
        var header = Marshal.PtrToStructure<WaveHeader>(headerPtr);
        if ((header.Flags & WhdrDone) == 0)
            return;

        var count = (int)header.BytesRecorded / 2;
        if (count > 0)
        {
            var chunk = new short[count];
            Marshal.Copy(header.Data, chunk, 0, count);
            double sum = 0;
            foreach (var s in chunk)
                sum += (double)s * s;
            Level = (float)Math.Min(1, Math.Sqrt(sum / count) / 6000);
            lock (_gate)
                _samples.AddRange(chunk);
        }

        if (requeue && _running)
        {
            header.Flags &= ~WhdrDone;
            header.BytesRecorded = 0;
            Marshal.StructureToPtr(header, headerPtr, false);
            waveInAddBuffer(_device, headerPtr, (uint)HeaderSize);
        }
        else
        {
            // Marcar como recogido para no leerlo dos veces.
            header.Flags &= ~WhdrDone;
            header.BytesRecorded = 0;
            Marshal.StructureToPtr(header, headerPtr, false);
        }
    }

    private void Release()
    {
        foreach (var headerPtr in _headers)
        {
            waveInUnprepareHeader(_device, headerPtr, (uint)HeaderSize);
            var header = Marshal.PtrToStructure<WaveHeader>(headerPtr);
            Marshal.FreeHGlobal(header.Data);
            Marshal.FreeHGlobal(headerPtr);
        }
        _headers = [];
        waveInClose(_device);
        _device = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_running)
            Stop();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr device, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr device);
}
