namespace OblivionVoice.Client.Spatial;

public sealed class PassThroughSpatialVoiceProvider : ISpatialVoiceProvider
{
    public SpatialVoiceResult Resolve(string speakerPlayerId, float voiceRangeMeters)
        => SpatialVoiceResult.FullVolume;
}
