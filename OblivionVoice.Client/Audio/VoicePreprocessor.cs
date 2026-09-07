using Microsoft.Extensions.Logging;

namespace OblivionVoice.Client.Audio;

public sealed class VoicePreprocessor(ILogger logger)
{

    public bool Enabled { get; set; } = true;

    public bool UseVoiceActivityDetection { get; set; } = true;

    public float OpenThresholdDb { get; set; } = 9f;

    public float CloseThresholdDb { get; set; } = 5f;

    public int HangoverMilliseconds { get; set; } = 300;

    public float MaxAttenuationDb { get; set; } = 18f;

    public float HighPassHz { get; set; } = 90f;

    public float NoiseFloorDb { get; private set; } = -60f;

    public float LevelDb { get; private set; } = -120f;

    public bool SpeechDetected { get; private set; }

    public long FramesProcessed { get; private set; }
    public long FramesGated { get; private set; }

    private int _sampleRate;
    private int _frameMilliseconds;

    private float _hpPrevIn;
    private float _hpPrevOut;
    private float _hpCoefficient;

    private float _currentGain = 1f;

    private int _hangoverFramesRemaining;
    private bool _gateOpen;
    private bool _primed;
    private int _primeFrames;

    public void Configure(int sampleRate, int frameMilliseconds)
    {
        _sampleRate = sampleRate;
        _frameMilliseconds = frameMilliseconds;

        var rc = 1f / (2f * MathF.PI * HighPassHz);
        var dt = 1f / sampleRate;
        _hpCoefficient = rc / (rc + dt);

        _hpPrevIn = 0f;
        _hpPrevOut = 0f;
        _currentGain = 1f;
        NoiseFloorDb = -60f;
        _primed = false;
        _primeFrames = 0;

        logger.LogInformation(
            "Voice preprocessor configured. HPF={HighPass}Hz open={Open}dB close={Close}dB hangover={Hangover}ms VAD={Vad}",
            HighPassHz,
            OpenThresholdDb,
            CloseThresholdDb,
            HangoverMilliseconds,
            UseVoiceActivityDetection);
    }

    public bool Process(Span<short> pcm)
    {
        FramesProcessed++;

        if (!Enabled || pcm.Length == 0)
        {
            SpeechDetected = true;
            return true;
        }

        ApplyHighPass(pcm);

        var rms = ComputeRms(pcm);
        LevelDb = rms <= 1e-7f ? -120f : 20f * MathF.Log10(rms);

        UpdateNoiseFloor();

        var snr = LevelDb - NoiseFloorDb;
        UpdateGate(snr);

        ApplyExpander(pcm, snr);

        SpeechDetected = _gateOpen;

        if (!UseVoiceActivityDetection) return true;

        if (!_primed) return true;

        if (!_gateOpen)
        {
            FramesGated++;
            return false;
        }

        return true;
    }

    private void ApplyHighPass(Span<short> pcm)
    {
        for (var i = 0; i < pcm.Length; i++)
        {
            var input = pcm[i] / 32768f;
            var output = _hpCoefficient * (_hpPrevOut + input - _hpPrevIn);

            _hpPrevIn = input;
            _hpPrevOut = output;

            pcm[i] = ClampToShort(output);
        }
    }

    private static float ComputeRms(ReadOnlySpan<short> pcm)
    {
        double sum = 0;
        for (var i = 0; i < pcm.Length; i++)
        {
            var normalized = pcm[i] / 32768.0;
            sum += normalized * normalized;
        }

        return (float)Math.Sqrt(sum / pcm.Length);
    }

    private void UpdateNoiseFloor()
    {
        var frameSeconds = _frameMilliseconds / 1000f;

        if (LevelDb < NoiseFloorDb)
        {

            NoiseFloorDb += (LevelDb - NoiseFloorDb) * 0.35f;
        }
        else
        {

            NoiseFloorDb += 1.5f * frameSeconds;
        }

        NoiseFloorDb = Math.Clamp(NoiseFloorDb, -75f, -20f);

        if (_primed) return;

        if (++_primeFrames >= 25) _primed = true;
    }

    private void UpdateGate(float snr)
    {
        var hangoverFrames = Math.Max(1, HangoverMilliseconds / Math.Max(1, _frameMilliseconds));

        if (snr >= OpenThresholdDb)
        {
            _gateOpen = true;
            _hangoverFramesRemaining = hangoverFrames;
            return;
        }

        if (snr < CloseThresholdDb)
        {
            if (_hangoverFramesRemaining > 0) _hangoverFramesRemaining--;
            if (_hangoverFramesRemaining == 0) _gateOpen = false;
            return;
        }

    }

    private void ApplyExpander(Span<short> pcm, float snr)
    {
        var targetGain = 1f;

        if (snr < OpenThresholdDb)
        {
            var span = MathF.Max(OpenThresholdDb - CloseThresholdDb, 0.5f);
            var below = Math.Clamp((OpenThresholdDb - snr) / span, 0f, 1f);
            var attenuationDb = -MaxAttenuationDb * below;
            targetGain = MathF.Pow(10f, attenuationDb / 20f);
        }

        var rate = targetGain > _currentGain ? 0.5f : 0.15f;
        _currentGain += (targetGain - _currentGain) * rate;

        if (_currentGain > 0.999f) return;

        for (var i = 0; i < pcm.Length; i++)
            pcm[i] = ClampToShort((pcm[i] / 32768f) * _currentGain);
    }

    private static short ClampToShort(float value)
    {
        var scaled = value * 32768f;
        if (scaled > short.MaxValue) return short.MaxValue;
        if (scaled < short.MinValue) return short.MinValue;
        return (short)scaled;
    }

    public string Describe() =>
        $"level={LevelDb:0.0}dBFS floor={NoiseFloorDb:0.0}dBFS snr={LevelDb - NoiseFloorDb:0.0}dB " +
        $"speech={SpeechDetected} gain={_currentGain:0.00} gated={FramesGated}/{FramesProcessed}";
}
