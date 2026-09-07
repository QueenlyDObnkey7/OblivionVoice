using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

namespace OblivionVoice.Client.Audio;

public sealed class WasapiMicrophoneCapture(ILogger logger) : IDisposable
{
    public event Action<ReadOnlyMemory<short>>? FrameReady;

    public WaveFormat? DeviceFormat { get; private set; }

    public bool ConversionActive { get; private set; }

    private WasapiCapture? _capture;
    private BufferedWaveProvider? _deviceBuffer;
    private ISampleProvider? _converted;

    private readonly object _gate = new();

    private int _frameSamples;
    private float[] _readBuffer = [];
    private float[] _accumulator = [];
    private int _accumulated;
    private short[] _frame = [];

    public void Start(int sampleRate, int channels, int frameMilliseconds, int bufferMilliseconds)
    {
        Stop();

        var device = WasapiCapture.GetDefaultCaptureDevice();
        var capture = new WasapiCapture(device, true, Math.Max(20, bufferMilliseconds));

        try { capture.WaveFormat = new WaveFormat(sampleRate, 16, channels); }
        catch (Exception ex) { logger.LogDebug(ex, "Capture device rejected the requested format; using its own."); }

        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;
        _capture = capture;

        capture.StartRecording();

        var actual = capture.WaveFormat;
        DeviceFormat = actual;

        _frameSamples = sampleRate * frameMilliseconds / 1000;
        _frame = new short[_frameSamples];
        _readBuffer = new float[_frameSamples];
        _accumulator = new float[_frameSamples * 4];
        _accumulated = 0;

        _deviceBuffer = new BufferedWaveProvider(actual)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };

        ISampleProvider source = _deviceBuffer.ToSampleProvider();

        if (actual.Channels > 1)
            source = source.ToMono();

        if (actual.SampleRate != sampleRate)
            source = new WdlResamplingSampleProvider(source, sampleRate);

        _converted = source;

        ConversionActive = actual.SampleRate != sampleRate
                           || actual.Channels != channels
                           || actual.BitsPerSample != 16;

        logger.LogInformation(
            "OblivionVoice microphone capture started. Requested {Rate}Hz/{Channels}ch/16bit, device gives {Device}. Conversion={Conversion}.",
            sampleRate,
            channels,
            actual,
            ConversionActive);

        Console.WriteLine(
            $"[OblivionVoice] Capture device={actual} (requested {sampleRate}Hz {channels}ch 16bit), conversion={ConversionActive}");

        if (ConversionActive)
        {
            Console.WriteLine(
                "[OblivionVoice] Capture format differs from requested; downmixing/resampling to match. " +
                "This is normal - WASAPI shared mode uses the device's own mix format.");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        lock (_gate)
        {
            if (_deviceBuffer == null || _converted == null) return;

            _deviceBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

            while (true)
            {
                var read = _converted.Read(_readBuffer.AsSpan());
                if (read <= 0) break;

                if (_accumulated + read > _accumulator.Length)
                    Array.Resize(ref _accumulator, Math.Max(_accumulator.Length * 2, _accumulated + read));

                Array.Copy(_readBuffer, 0, _accumulator, _accumulated, read);
                _accumulated += read;

                while (_accumulated >= _frameSamples)
                {
                    for (var i = 0; i < _frameSamples; i++)
                    {
                        var sample = _accumulator[i];

                        if (sample > 1f) sample = 1f;
                        else if (sample < -1f) sample = -1f;

                        _frame[i] = (short)(sample * 32767f);
                    }

                    _accumulated -= _frameSamples;
                    if (_accumulated > 0)
                        Array.Copy(_accumulator, _frameSamples, _accumulator, 0, _accumulated);

                    FrameReady?.Invoke(_frame);
                }
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            logger.LogError(e.Exception, "OblivionVoice microphone capture stopped with an error.");
    }

    public void Stop()
    {
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.StopRecording(); } catch { }
            _capture.Dispose();
            _capture = null;
        }

        lock (_gate)
        {
            _deviceBuffer = null;
            _converted = null;
            _accumulated = 0;
        }
    }

    public void Dispose() => Stop();
}
