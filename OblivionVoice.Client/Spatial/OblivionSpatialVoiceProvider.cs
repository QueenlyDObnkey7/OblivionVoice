using System.Numerics;
using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Entities.Extensions;
using OblivionMp.Sdk.Entities.Player;

namespace OblivionVoice.Client.Spatial;

public sealed class OblivionSpatialVoiceProvider(ILogger logger) : ISpatialVoiceProvider
{

    public float WorldUnitsPerMeter { get; set; } = 70f;

    public bool UseParentCell { get; set; }

    public float FullVolumeFraction { get; set; } = 0.15f;

    public float PanCollapseMeters { get; set; } = 1.5f;

    public float MaxPan { get; set; } = 0.85f;

    public bool InvertPan { get; set; }

    public float LastDistanceMeters { get; private set; }

    private int _missingSpeakerLogs;

    public SpatialVoiceResult Resolve(string speakerPlayerId, float voiceRangeMeters)
    {
        try
        {
            var local = SDK.Sync.LocalPlayer;
            if (local is not { } listener)
                return SpatialVoiceResult.Silent;

            if (!TryFindSpeaker(speakerPlayerId, out var speaker))
            {

                if (_missingSpeakerLogs++ < 5)
                    logger.LogDebug("No replicated entity for speaker {Speaker}; treating as out of range.", speakerPlayerId);

                return SpatialVoiceResult.Silent;
            }

            var delta = speaker.Location - listener.Location;
            var distanceUnits = delta.Length();
            var distanceMeters = distanceUnits / MathF.Max(WorldUnitsPerMeter, 0.0001f);
            LastDistanceMeters = distanceMeters;

            if (!float.IsFinite(distanceMeters) || !float.IsFinite(voiceRangeMeters) || distanceMeters > voiceRangeMeters)
                return new SpatialVoiceResult(false, 0f, 0f, distanceMeters);

            var gain = ComputeGain(distanceMeters, voiceRangeMeters);

            var pan = ComputePan(listener.Rotation, delta, distanceMeters);

            return new SpatialVoiceResult(true, gain, pan, distanceMeters);
        }
        catch (Exception ex)
        {

            logger.LogDebug(ex, "Spatial resolve failed for {Speaker}; withholding audio until position is available.", speakerPlayerId);
            return SpatialVoiceResult.Silent;
        }
    }

    private float ComputeGain(float distanceMeters, float rangeMeters)
    {
        if (rangeMeters <= 0f) return 0f;

        var flatZone = rangeMeters * Math.Clamp(FullVolumeFraction, 0f, 0.9f);
        if (distanceMeters <= flatZone) return 1f;

        var t = (distanceMeters - flatZone) / MathF.Max(rangeMeters - flatZone, 0.0001f);
        t = Math.Clamp(t, 0f, 1f);

        return Math.Clamp((1f - t) * (1f - t), 0f, 1f);
    }

    private float ComputePan(float listenerYawDegrees, Vector3 delta, float distanceMeters)
    {

        var flat = new Vector2(delta.X, delta.Y);
        var flatLength = flat.Length();
        if (flatLength < 0.0001f) return 0f;

        flat /= flatLength;

        var yawRadians = listenerYawDegrees * (MathF.PI / 180f);

        var right = new Vector2(
            MathF.Cos(yawRadians - (MathF.PI / 2f)),
            MathF.Sin(yawRadians - (MathF.PI / 2f)));

        var pan = Vector2.Dot(flat, right);

        if (distanceMeters < PanCollapseMeters && PanCollapseMeters > 0f)
            pan *= distanceMeters / PanCollapseMeters;

        pan *= MaxPan;
        if (InvertPan) pan = -pan;

        return Math.Clamp(pan, -1f, 1f);
    }

    private static bool TryFindSpeaker(string speakerPlayerId, out ReadyMainCharacter speaker)
    {
        foreach (var player in SDK.Sync.AllPlayers)
        {
            if (string.Equals(player.PlayerId.ToString(), speakerPlayerId, StringComparison.Ordinal))
            {
                speaker = player;
                return true;
            }
        }

        speaker = default;
        return false;
    }

    public string CalibrateNote(float rawUnits) =>
        $"raw={rawUnits:0.#} units -> {rawUnits / 70f:0.##}m if Gamebryo (70/m), " +
        $"{rawUnits / 100f:0.##}m if Unreal (100/m). Currently using {WorldUnitsPerMeter:0.#}/m " +
        $"= {rawUnits / WorldUnitsPerMeter:0.##}m.";
}
