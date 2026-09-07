using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OblivionVoice.Client.Audio.Effects;

namespace OblivionVoice.Client.Audio;

public sealed class WasapiPlaybackMixer(ILogger logger) : IDisposable
{
    private sealed record SpeakerChannel(
        SpeakerVoicePipeline Pipeline,
        PanningSampleProvider Panner,
        VolumeSampleProvider Volume);

    private readonly ConcurrentDictionary<string, SpeakerChannel> _speakers = new(StringComparer.Ordinal);

    private readonly object _speakerGate = new();

    private WasapiPlayer? _output;
    private MixingSampleProvider? _mixer;
    private int _sampleRate;
    private int _frameMilliseconds;
    private bool _muted;

    public int TargetJitterFrames { get; set; } = 3;

    public IReadOnlyCollection<string> ActiveSpeakers =>
        _speakers.Where(kvp => kvp.Value.Pipeline.IsActive).Select(kvp => kvp.Key).ToArray();

    public void Start(int sampleRate, int channels, int bufferMilliseconds, int frameMilliseconds = 20)
    {
        DisposeOutput();

        _sampleRate = sampleRate;
        _frameMilliseconds = frameMilliseconds;

        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2))
        {
            ReadFully = true
        };

        _output = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithMmcssThreadPriority("Games")
            .Build();

        _output.Init(_mixer);
        _output.Play();

        logger.LogInformation(
            "OblivionVoice playback started. Device={Device} Mixer={Mixer} State={State} Latency={Latency}ms {Frame}ms frames, {Jitter} frame cushion ({Cushion}ms).",
            _output.OutputWaveFormat,
            _mixer.WaveFormat,
            _output.PlaybackState,
            _output.LatencyMilliseconds,
            frameMilliseconds,
            TargetJitterFrames,
            TargetJitterFrames * frameMilliseconds);

        _output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception != null)
            {

                logger.LogError(
                    "OblivionVoice playback stopped with an error: {Error}",
                    e.Exception.ToString());
            }
            else
            {
                logger.LogWarning("OblivionVoice playback stopped unexpectedly.");
            }
        };
    }

    public void Submit(
        string speakerId,
        ushort sequence,
        ReadOnlyMemory<byte> opus,
        float volume = 1f,
        float pan = 0f)
    {
        if (_mixer == null || _muted || opus.Length == 0) return;

        if (!_speakers.TryGetValue(speakerId, out var channel))
        {
            lock (_speakerGate)
            {

                if (!_speakers.TryGetValue(speakerId, out channel))
                {
                    var mixer = _mixer;
                    if (mixer == null) return;

                    var pipeline = new SpeakerVoicePipeline(_sampleRate, _frameMilliseconds)
                    {
                        TargetFrames = TargetJitterFrames
                    };

                    pipeline.Effects.Add(new LoudnessNormalizerEffect());

                    var panner = new PanningSampleProvider(pipeline)
                    {

                        PanStrategy = new SinPanStrategy(),
                        Pan = 0f
                    };

                    var volumeProvider = new VolumeSampleProvider(panner) { Volume = 1f };

                    mixer.AddMixerInput(volumeProvider);
                    channel = new SpeakerChannel(pipeline, panner, volumeProvider);
                    _speakers[speakerId] = channel;

                    logger.LogInformation(
                        "Added voice mixer input for {Speaker}. Mixer inputs now {Inputs}, output state {State}.",
                        speakerId,
                        mixer.MixerInputs.Count(),
                        _output?.PlaybackState);
                }
            }
        }

        channel.Volume.Volume = Math.Clamp(volume, 0f, 1f);
        channel.Panner.Pan = Math.Clamp(pan, -1f, 1f);
        channel.Pipeline.Enqueue(sequence, opus);
    }

    public void ConfigureEffects(string speakerId, Action<VoiceEffectChain> configure)
    {
        if (_speakers.TryGetValue(speakerId, out var channel))
            configure(channel.Pipeline.Effects);
    }

    public void ConfigureAllEffects(Action<VoiceEffectChain> configure)
    {
        foreach (var channel in _speakers.Values)
            configure(channel.Pipeline.Effects);
    }

    public void SetMuted(bool muted) => _muted = muted;

    public void RemoveSpeaker(string speakerId)
    {
        lock (_speakerGate)
        {
            if (!_speakers.TryRemove(speakerId, out var channel)) return;

            try
            {
                _mixer?.RemoveMixerInput(channel.Volume);
                channel.Pipeline.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to remove pipeline for {Speaker}.", speakerId);
            }
        }
    }

    public string DescribeJitter()
    {
        if (_speakers.IsEmpty) return "no speakers";

        var inputs = _mixer?.MixerInputs.Count() ?? 0;
        var state = _output?.PlaybackState;

        return $"[inputs={inputs} state={state}] " + string.Join(", ", _speakers.Select(kvp =>
            $"{kvp.Key}: concealed={kvp.Value.Pipeline.ConcealedFrames} " +
            $"late={kvp.Value.Pipeline.DroppedLateFrames} " +
            $"overflow={kvp.Value.Pipeline.DroppedOverflowFrames} " +
            $"{kvp.Value.Pipeline.DescribeThroughput()}"));
    }

    private void DisposeOutput()
    {
        try { _output?.Stop(); } catch { }
        _output?.Dispose();
        _output = null;
        _mixer = null;

        lock (_speakerGate)
        {
            foreach (var channel in _speakers.Values)
            {
                try { channel.Pipeline.Dispose(); } catch { }
            }

            _speakers.Clear();
        }
    }

    public void Dispose() => DisposeOutput();
}
