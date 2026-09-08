using Microsoft.Extensions.Logging;
using ReadyM.Relay.Server.Sdk.Ecs;
using ReadyM.Relay.Server.Sdk.Ecs.Systems;

namespace OblivionVoice.Server;

public sealed class VoiceRoutingSystem(
    EcsApi ecsApi,
    VoicePositionCache cache,
    VoiceConfigLoader configLoader,
    ILogger logger) : ModSystemBase
{
    private const float RefreshIntervalSeconds = 0.1f;

    private float _sinceRefresh;
    private bool _loggedCullingOff;
    private bool _loggedFirstTick;

    protected override void OnUpdate(UpdateTick tick)
    {

        if (!_loggedFirstTick)
        {
            _loggedFirstTick = true;

            ServerStartupTrace.Write("VOICE ROUTING SYSTEM FIRST TICK");
        }

        var config = configLoader.Current;
        if (!config.Enabled) return;

        if (!config.Proximity.ServerSideRouting && !_loggedCullingOff)
        {
            _loggedCullingOff = true;
            logger.LogInformation(
                "Server-side distance culling is off; distance is checked by receiving clients. " +
                "Server cell filtering follows UseParentCell independently; environment lookup remains active.");
        }

        _sinceRefresh += tick.DeltaTime;
        if (_sinceRefresh < RefreshIntervalSeconds) return;
        _sinceRefresh = 0f;

        cache.Refresh(ecsApi);

        if (cache.RefreshCount <= 3 || cache.RefreshCount % 100 == 0)
        {
            ServerStartupTrace.Write(
                "POSITION REFRESH",
                $"refreshes={cache.RefreshCount}",
                $"players={cache.Count}",
                $"cellMatches={cache.CellMatchCount}",
                $"interiors={cache.InteriorCount}");
        }
    }
}
