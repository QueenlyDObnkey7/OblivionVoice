using Microsoft.Extensions.Logging;
using OblivionVoice.Client.Audio;
using OblivionVoice.Client.Audio.Effects;
using OblivionVoice.Common;

namespace OblivionVoice.Client;

public sealed class VoiceEnvironmentDirector(WasapiPlaybackMixer playback, ILogger logger)
{
    private readonly Dictionary<string, VoiceEnvironment> _current = new(StringComparer.Ordinal);

    public bool Enabled { get; set; } = true;

    public void Apply(string speakerId, VoiceEnvironment environment)
    {
        if (!Enabled) return;

        if (_current.TryGetValue(speakerId, out var existing) && existing == environment)
            return;

        _current[speakerId] = environment;

        playback.ConfigureEffects(speakerId, chain =>
        {
            chain.Clear();

            chain.Add(new LoudnessNormalizerEffect());

            var reverb = CreateReverb(environment);
            if (reverb != null) chain.Add(reverb);
        });

        logger.LogInformation(
            "[VoiceDebug] Speaker {Speaker} environment -> {Environment}",
            speakerId,
            environment);
    }

    public void Forget(string speakerId) => _current.Remove(speakerId);

    private static ReverbEffect? CreateReverb(VoiceEnvironment environment) => environment switch
    {
        VoiceEnvironment.Outdoor => null,

        VoiceEnvironment.Room => ReverbEffect.Room(),

        VoiceEnvironment.Cave => ReverbEffect.Cave(),

        VoiceEnvironment.Mine => new ReverbEffect
        {
            RoomSize = 0.82f,
            Damping = 0.45f,
            Wet = 0.32f,
            Dry = 0.9f
        },

        VoiceEnvironment.Ruin => ReverbEffect.Ruin(),

        VoiceEnvironment.Hall => new ReverbEffect
        {
            RoomSize = 0.94f,
            Damping = 0.2f,
            Wet = 0.42f,
            Dry = 0.88f
        },

        _ => ReverbEffect.Room()
    };
}
