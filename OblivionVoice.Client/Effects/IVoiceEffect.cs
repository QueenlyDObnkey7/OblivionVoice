namespace OblivionVoice.Client.Audio.Effects;

public interface IVoiceEffect
{

    bool Enabled { get; set; }

    void Process(Span<float> buffer);

    void Prepare(int sampleRate);

    void Reset();
}

public sealed class VoiceEffectChain
{
    private readonly List<IVoiceEffect> _effects = new();
    private int _sampleRate;

    public bool Enabled { get; set; } = true;

    public IReadOnlyList<IVoiceEffect> Effects => _effects;

    public void Add(IVoiceEffect effect)
    {
        if (_sampleRate > 0) effect.Prepare(_sampleRate);
        _effects.Add(effect);
    }

    public void Clear()
    {
        foreach (var effect in _effects) effect.Reset();
        _effects.Clear();
    }

    public void Prepare(int sampleRate)
    {
        _sampleRate = sampleRate;
        foreach (var effect in _effects) effect.Prepare(sampleRate);
    }

    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;

        for (var i = 0; i < _effects.Count; i++)
        {
            var effect = _effects[i];
            if (effect.Enabled) effect.Process(buffer);
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _effects.Count; i++) _effects[i].Reset();
    }
}
