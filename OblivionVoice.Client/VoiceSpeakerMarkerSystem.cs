using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Values;
using ReadyM.Modloader.Mods;

namespace OblivionVoice.Client;

public sealed class VoiceSpeakerMarkerSystem(
    VoiceRuntime runtime,
    ILogger logger) : ModSystemBase
{

    private const float RefreshIntervalSeconds = 0.2f;

    private const float LingerSeconds = 0.6f;

    private static readonly MarkerColor SpeakingColor = new(0.4f, 1f, 0.4f);

    private readonly Dictionary<string, MarkerHandle> _markers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _lastHeard = new(StringComparer.Ordinal);

    private float _sinceRefresh;
    private float _elapsed;

    protected override void OnUpdate(UpdateTick tick)
    {

        _elapsed += tick.deltaTime;
        _sinceRefresh += tick.deltaTime;

        if (_sinceRefresh < RefreshIntervalSeconds) return;
        _sinceRefresh = 0f;

        try
        {
            Refresh();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Voice speaker marker refresh failed.");
        }
    }

    private void Refresh()
    {
        foreach (var speakerId in runtime.ActiveSpeakers)
            _lastHeard[speakerId] = _elapsed;

        var present = new Dictionary<string, OblivionMp.Sdk.Entities.Player.ReadyMainCharacter>(StringComparer.Ordinal);
        foreach (var player in SDK.Sync.AllPlayers)
            present[player.PlayerId.ToString()] = player;

        foreach (var playerId in _markers.Keys.ToArray())
        {
            var stillSpeaking = _lastHeard.TryGetValue(playerId, out var heardAt)
                                && _elapsed - heardAt < LingerSeconds;

            if (stillSpeaking && present.ContainsKey(playerId)) continue;

            SDK.Markers.DestroyMarker(_markers[playerId]);
            _markers.Remove(playerId);

            if (!present.ContainsKey(playerId)) _lastHeard.Remove(playerId);
        }

        foreach (var (playerId, heardAt) in _lastHeard)
        {
            if (_elapsed - heardAt >= LingerSeconds) continue;
            if (_markers.ContainsKey(playerId)) continue;
            if (!present.TryGetValue(playerId, out var character)) continue;

            var handle = SDK.Markers.CreateMarker(character, "speaking", SpeakingColor);

            if (handle.Equals(MarkerHandle.None)) continue;

            _markers[playerId] = handle;
        }
    }
}
