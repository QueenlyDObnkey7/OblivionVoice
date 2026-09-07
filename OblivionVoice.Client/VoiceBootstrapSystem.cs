using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using ReadyM.Modloader.Mods;

namespace OblivionVoice.Client;

public sealed class VoiceBootstrapSystem(VoiceServerRpc serverRpc, ILogger logger) : ModSystemBase
{

    private const float RetrySeconds = 3f;
    private static int _instanceCount;
    private readonly int _instanceId = Interlocked.Increment(ref _instanceCount);

    private const float InitialDelaySeconds = 1f;

    private const int MaxAttempts = 10;

    private float _sinceLocalPlayer;
    private float _sinceLastAttempt;
    private int _attempts;
    private bool _wasInGame;
    private bool _gaveUp;

    protected override void OnUpdate(UpdateTick tick)
    {
        var inGame = SDK.Sync.LocalPlayer is not null;

        if (!inGame)
        {
            if (_wasInGame)
            {
                logger.LogInformation("[VoiceDebug] Local player gone; voice bootstrap armed for next join.");
                _wasInGame = false;
                _sinceLocalPlayer = 0f;
                _sinceLastAttempt = 0f;
                _attempts = 0;
                _gaveUp = false;
            }

            return;
        }

        if (!_wasInGame)
        {
            _wasInGame = true;
            _sinceLocalPlayer = 0f;
            _sinceLastAttempt = float.MaxValue;
            logger.LogInformation("[VoiceDebug] Local player ready; voice bootstrap will begin shortly.");
        }

        if (serverRpc.HasReceivedBootstrap || _gaveUp)
            return;

        _sinceLocalPlayer += tick.deltaTime;
        if (_sinceLocalPlayer < InitialDelaySeconds)
            return;

        _sinceLastAttempt += tick.deltaTime;
        if (_sinceLastAttempt < RetrySeconds)
            return;

        _sinceLastAttempt = 0f;
        _attempts++;

        if (_attempts > MaxAttempts)
        {
            _gaveUp = true;
            logger.LogWarning(
                "OblivionVoice gave up requesting a voice session after {Attempts} attempts. " +
                "The server may not have OblivionVoice installed. Press F9 to retry manually.",
                MaxAttempts);
            Console.WriteLine(
                $"[OblivionVoice] Gave up after {MaxAttempts} bootstrap attempts. Press F9 to retry.");
            return;
        }

        try
        {
            serverRpc.RequestBootstrap($"auto bootstrap attempt {_attempts} (system #{_instanceId} of {_instanceCount})");
        }
        catch (Exception ex)
        {

            logger.LogError(ex, "OblivionVoice automatic voice session request failed.");
            Console.WriteLine($"[OblivionVoice] Auto bootstrap request failed: {ex.Message}");
        }
    }
}
