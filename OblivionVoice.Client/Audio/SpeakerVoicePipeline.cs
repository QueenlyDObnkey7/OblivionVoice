using System.Diagnostics;
using Concentus;
using NAudio.Wave;
using OblivionVoice.Client.Audio.Effects;

namespace OblivionVoice.Client.Audio;

public sealed class SpeakerVoicePipeline : ISampleProvider, IDisposable
{

    public int TargetFrames { get; set; } = 3;

    public int MaxFrames { get; set; } = 50;

    public int MaxConsecutiveConcealed { get; set; } = 5;

    public VoiceEffectChain Effects { get; } = new();
    public WaveFormat WaveFormat { get; }

    public bool IsActive { get; private set; }
    private readonly SpeechLevel _speechLevel = new();
    public float CurrentSpeechLevel => _speechLevel.Value;
    public void ClearSpeechLevel() => _speechLevel.Clear();

    public long ConcealedFrames { get; private set; }
    public long DroppedLateFrames { get; private set; }
    public long DroppedOverflowFrames { get; private set; }

    public long EnqueuedFrames { get; private set; }

    public long ConsumedFrames { get; private set; }

    public int QueueDepth
    {
        get { lock (_gate) return _queue.Count; }
    }

    private readonly IOpusDecoder _decoder;
    private readonly int _frameSamples;
    private readonly object _gate = new();
    private readonly SortedDictionary<ushort, byte[]> _queue = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();

    private readonly float[] _frameBuffer;
    private readonly short[] _decodeBuffer;

    private int _frameFill;
    private int _framePosition;
    private bool _priming = true;
    private bool _hasNextSequence;
    private ushort _nextSequence;
    private int _consecutiveConcealed;
    private int _silentReads;

    public SpeakerVoicePipeline(int sampleRate, int frameMilliseconds)
    {
        _frameSamples = sampleRate * frameMilliseconds / 1000;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, 1);

        _frameBuffer = new float[_frameSamples];
        _decodeBuffer = new short[_frameSamples];

        Effects.Prepare(sampleRate);
    }

    public void Enqueue(ushort sequence, ReadOnlyMemory<byte> opus)
    {
        if (opus.Length == 0) return;

        lock (_gate)
        {

            if (_hasNextSequence && IsOlderThan(sequence, _nextSequence))
            {
                DroppedLateFrames++;
                return;
            }

            if (_queue.ContainsKey(sequence)) return;

            _queue[sequence] = opus.ToArray();
            EnqueuedFrames++;

            while (_queue.Count > MaxFrames)
            {
                var oldest = _queue.Keys.First();
                _queue.Remove(oldest);
                DroppedOverflowFrames++;

                if (IsOlderThan(_nextSequence, _queue.Keys.First()))
                    _nextSequence = _queue.Keys.First();
            }
        }
    }

    public int Read(Span<float> buffer)
    {
        var written = 0;

        while (written < buffer.Length)
        {
            if (_framePosition >= _frameFill)
            {
                if (!FillNextFrame())
                {
                    _speechLevel.Clear();
                    buffer[written..].Clear();
                    written = buffer.Length;
                    break;
                }

                _framePosition = 0;
            }

            var take = Math.Min(buffer.Length - written, _frameFill - _framePosition);
            _frameBuffer.AsSpan(_framePosition, take).CopyTo(buffer.Slice(written, take));
            _framePosition += take;
            written += take;
        }

        return written;
    }

    private bool FillNextFrame()
    {
        byte[]? encoded = null;
        var conceal = false;

        lock (_gate)
        {

            if (_priming)
            {
                if (_queue.Count < TargetFrames) return false;

                _priming = false;
                _nextSequence = _queue.Keys.First();
                _hasNextSequence = true;
            }

            if (_queue.Count == 0)
            {

                if (_consecutiveConcealed < MaxConsecutiveConcealed)
                {
                    conceal = true;
                }
                else
                {
                    if (++_silentReads > 75)
                    {
                        IsActive = false;
                        _priming = true;
                        _hasNextSequence = false;
                        Effects.Reset();
                    }

                    return false;
                }
            }
            else if (_queue.TryGetValue(_nextSequence, out var frame))
            {
                encoded = frame;
                _queue.Remove(_nextSequence);
            }
            else
            {

                conceal = true;
            }

            if (!conceal || _queue.Count > 0)
                _nextSequence = unchecked((ushort)(_nextSequence + 1));
        }

        int samples;

        if (conceal)
        {

            samples = _decoder.Decode(null, _decodeBuffer.AsSpan(), _frameSamples, false);
            _consecutiveConcealed++;
            ConcealedFrames++;
        }
        else
        {
            samples = _decoder.Decode(encoded, _decodeBuffer.AsSpan(), _frameSamples, false);
            _consecutiveConcealed = 0;
            _silentReads = 0;
            IsActive = true;
        }

        if (samples <= 0) { _speechLevel.Clear(); return false; }

        // Meter the real decoded frame when playback consumes it, before gain,
        // panning or reverb. Packet arrival and packet-loss concealment are not speech.
        if (conceal) _speechLevel.Clear();
        else _speechLevel.Publish(_decodeBuffer.AsSpan(0, samples));

        ConsumedFrames++;

        for (var i = 0; i < samples; i++)
            _frameBuffer[i] = _decodeBuffer[i] / 32768f;

        _frameFill = samples;

        Effects.Process(_frameBuffer.AsSpan(0, samples));

        return true;
    }

    public string DescribeThroughput()
    {
        var seconds = Math.Max(_uptime.Elapsed.TotalSeconds, 0.001);

        return $"enq={EnqueuedFrames} ({EnqueuedFrames / seconds:0.0}/s) " +
               $"cons={ConsumedFrames} ({ConsumedFrames / seconds:0.0}/s) " +
               $"depth={QueueDepth}";
    }

    private static bool IsOlderThan(ushort a, ushort b) =>
        unchecked((short)(a - b)) < 0;

    public void Dispose()
    {
        _speechLevel.Clear();
        lock (_gate) _queue.Clear();
        Effects.Clear();
        _decoder.Dispose();
    }
}
