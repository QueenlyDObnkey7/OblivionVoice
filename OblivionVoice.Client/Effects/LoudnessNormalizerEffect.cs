namespace OblivionVoice.Client.Audio.Effects;

public sealed class LoudnessNormalizerEffect : IVoiceEffect
{
    public bool Enabled { get; set; } = true;

    public float TargetDb { get; set; } = -24f;

    public float MaxGainDb { get; set; } = 10f;

    public float MaxCutDb { get; set; } = 18f;

    public float SilenceFloorDb { get; set; } = -55f;

    public float AttackSeconds { get; set; } = 0.15f;

    public float ReleaseSeconds { get; set; } = 0.6f;

    public float CurrentGainDb { get; private set; }

    private int _sampleRate = 48000;
    private float _currentGain = 1f;

    public void Prepare(int sampleRate) => _sampleRate = sampleRate;

    public void Process(Span<float> buffer)
    {
        if (buffer.Length == 0) return;

        var frameSeconds = (float)buffer.Length / _sampleRate;

        double sum = 0;
        for (var i = 0; i < buffer.Length; i++)
            sum += buffer[i] * (double)buffer[i];

        var rms = (float)Math.Sqrt(sum / buffer.Length);
        var levelDb = rms <= 1e-7f ? -120f : 20f * MathF.Log10(rms);

        if (levelDb > SilenceFloorDb)
        {
            var desiredGainDb = Math.Clamp(TargetDb - levelDb, -MaxCutDb, MaxGainDb);
            var desiredGain = MathF.Pow(10f, desiredGainDb / 20f);

            var timeConstant = desiredGain > _currentGain ? AttackSeconds : ReleaseSeconds;
            var alpha = 1f - MathF.Exp(-frameSeconds / MathF.Max(timeConstant, 0.001f));

            _currentGain += (desiredGain - _currentGain) * alpha;
            CurrentGainDb = 20f * MathF.Log10(MathF.Max(_currentGain, 1e-6f));
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i] * _currentGain;

            buffer[i] = sample switch
            {
                > 0.95f or < -0.95f => MathF.Tanh(sample),
                _ => sample
            };
        }
    }

    public void Reset()
    {
        _currentGain = 1f;
        CurrentGainDb = 0f;
    }
}
