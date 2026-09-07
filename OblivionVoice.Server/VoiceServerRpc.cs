using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using OblivionVoice.Common;
using ReadyM.Api.Multiplayer;
using ReadyM.Relay.Server.Sdk.Rpc;

namespace OblivionVoice.Server;

[ServerRpcFor(typeof(VoiceRpcContracts))]
public partial class VoiceServerRpc : ServerRpcHandlersBase
{

    private static readonly TimeSpan RequestDebounce = TimeSpan.FromMilliseconds(750);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequestByPlayer = new(StringComparer.Ordinal);

    private readonly VoiceConfigLoader configLoader;
    private readonly VoiceSessionTokenService tokenService;
    private readonly ILogger logger;

    public VoiceServerRpc(
        VoiceConfigLoader configLoader,
        VoiceSessionTokenService tokenService,
        ILogger logger)
    {
        this.configLoader = configLoader;
        this.tokenService = tokenService;
        this.logger = logger;

        ServerStartupTrace.Write(
            "VoiceServerRpc CONSTRUCTOR",
            $"HandlerType={GetType().FullName}",
            $"BaseType={GetType().BaseType?.FullName}");
    }

    partial void OnVoiceSession(RpcContext context)
    {
        var playerKey = context.Sender.ToString();
        var now = DateTimeOffset.UtcNow;

        if (_lastRequestByPlayer.TryGetValue(playerKey, out var previous) && now - previous < RequestDebounce)
        {
            logger.LogDebug(
                "[VoiceDebug] Ignoring duplicate VoiceSession request from {PlayerId} ({Gap}ms since the last one).",
                context.Sender,
                (now - previous).TotalMilliseconds);

            ServerStartupTrace.Write(
                "RPC VoiceSession REQUEST DEBOUNCED",
                $"Sender={context.Sender}",
                $"GapMs={(now - previous).TotalMilliseconds:0}");

            return;
        }

        _lastRequestByPlayer[playerKey] = now;

        ServerStartupTrace.Write(
            "RPC VoiceSession REQUEST RECEIVED",
            $"Sender={context.Sender}");

        logger.LogInformation("[VoiceDebug] VoiceSession request RPC received from player {PlayerId}", context.Sender);
        Console.WriteLine($"[OblivionVoice] VoiceSession request received from {context.Sender}");

        var c = configLoader.Current;
        var sessionToken = c.Enabled ? tokenService.Issue(context.Sender).Token : string.Empty;

        SendVoiceSession(
            context.Sender,
            c.Enabled,
            c.Network.AdvertisedHost,
            c.Network.Port,
            sessionToken,
            c.Audio.SampleRate,
            c.Audio.Channels,
            c.Audio.FrameMilliseconds,
            c.Audio.Bitrate,
            c.Audio.EnableDtx,
            c.Audio.EnableFec,
            c.Audio.MicrophoneBufferMilliseconds,
            c.Audio.PlaybackBufferMilliseconds,
            c.Proximity.WhisperMeters,
            c.Proximity.NormalMeters,
            c.Proximity.ShoutMeters,
            c.Proximity.MaximumReceiveMeters,
            (int)c.Proximity.DefaultMode,
            c.Proximity.UseParentCell,
            c.Proximity.ServerSideRouting,
            c.Controls.TransmitKey,
            (int)c.Controls.TransmitMode,
            c.Controls.CycleRangeKey,
            c.Controls.ToggleMuteKey,
            c.Radio.Enabled,
            c.Controls.RadioTransmitKey,
            c.Ui.BrandName,
            c.Debug.Enabled,
            c.Debug.LoopbackMicrophone,
            c.Debug.LogPackets,
            c.Debug.LogClientStats,
            c.Debug.LogAudioLevels,
            c.Debug.LogKeyEvents,
            c.Debug.ForceTransmit,
            c.Debug.StatsIntervalSeconds,
            c.Proximity.WorldUnitsPerMeter,
            c.Proximity.InvertPan);

        logger.LogInformation("Issued OblivionVoice bootstrap for player {PlayerId}", context.Sender);
        ServerStartupTrace.Write(
            "RPC VoiceSession RESPONSE SENT",
            $"Recipient={context.Sender}",
            $"Enabled={c.Enabled}",
            $"Host={c.Network.AdvertisedHost}:{c.Network.Port}");

        if (c.Debug.Enabled)
        {
            logger.LogInformation(
                "[VoiceDebug] Bootstrap Player={PlayerId} Host={Host}:{Port} Loopback={Loopback} PacketLog={PacketLog} ForceTransmit={ForceTransmit}",
                context.Sender,
                c.Network.AdvertisedHost,
                c.Network.Port,
                c.Debug.LoopbackMicrophone,
                c.Debug.LogPackets,
                c.Debug.ForceTransmit);
        }
    }

    partial void OnDiagnosticPing(RpcContext context, int nonce)
    {
        ServerStartupTrace.Write(
            "RPC DiagnosticPing RECEIVED",
            $"Sender={context.Sender}",
            $"Nonce={nonce}");

        Console.WriteLine(
            $"[OblivionVoice] DiagnosticPing received from {context.Sender}, nonce={nonce}");

        SendDiagnosticPing(context.Sender, nonce, nonce);

        ServerStartupTrace.Write(
            "RPC DiagnosticPing RESPONSE SENT",
            $"Recipient={context.Sender}",
            $"Nonce={nonce}");
    }
}
