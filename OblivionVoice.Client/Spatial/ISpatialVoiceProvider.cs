namespace OblivionVoice.Client.Spatial;

public readonly record struct SpatialVoiceResult(
    bool Audible,
    float Gain,
    float Pan,
    float DistanceMeters)
{

    public static SpatialVoiceResult FullVolume => new(true, 1f, 0f, 0f);

    public static SpatialVoiceResult Silent => new(false, 0f, 0f, float.PositiveInfinity);
}

public interface ISpatialVoiceProvider
{

    SpatialVoiceResult Resolve(string speakerPlayerId, float voiceRangeMeters);
}
