using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using ReadyM.Modloader.Mods;

namespace OblivionVoice.Client;

public sealed class VoiceBootstrapSystem(VoiceServerRpc serverRpc, VoiceRuntime runtime, ILogger logger) : ModSystemBase
{
    private string? _playerId;
    private float _sinceAttempt;
    private float _retrySeconds = 1f;
    private bool _wasConnected;

    protected override void OnUpdate(UpdateTick tick)
    {
        var localPlayer = SDK.Sync.LocalPlayer;
        var playerId = localPlayer is { } player ? player.PlayerId.ToString() : null;
        if (playerId != _playerId)
        {
            runtime.Dispose();
            serverRpc.ResetBootstrap();
            _playerId = playerId;
            _sinceAttempt = 0f;
            _retrySeconds = 1f;
            _wasConnected = false;
        }
        if (playerId == null) return;
        runtime.RefreshConnectionState();
        if (runtime.IsVoiceConnected)
        {
            _wasConnected = true;
            _sinceAttempt = 0f;
            _retrySeconds = 3f;
            return;
        }
        if (_wasConnected)
        {
            _wasConnected = false;
            _sinceAttempt = 0f;
            _retrySeconds = 1f;
            logger.LogWarning("Voice disconnected. Automatic recovery is active.");
        }
        if (serverRpc.DisabledByServer) return;
        if (runtime.IsBootstrapping) return;
        _sinceAttempt += tick.deltaTime;
        if (_sinceAttempt < _retrySeconds) return;
        _sinceAttempt = 0f;
        try { serverRpc.RequestBootstrap("automatic connection recovery"); }
        catch (Exception ex) { logger.LogDebug(ex, "Voice session request failed; retrying."); }
        _retrySeconds = MathF.Min(30f, MathF.Max(10f, _retrySeconds * 2f));
    }
}
