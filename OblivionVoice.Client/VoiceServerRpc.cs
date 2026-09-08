using Microsoft.Extensions.Logging;
using OblivionVoice.Common;
using ReadyM.Api.Multiplayer;
using ReadyM.Api.Multiplayer.RPC;

namespace OblivionVoice.Client;

[ServerRpcFor(typeof(VoiceRpcContracts))]
public partial class VoiceServerRpc(VoiceRuntime runtime, ILogger logger) : ServerRpcClient
{
    private bool _acceptReplies;
    public bool DisabledByServer { get; private set; }
    public void ResetBootstrap()
    {
        _acceptReplies = false;
        HasReceivedBootstrap = false;
        DisabledByServer = false;
    }

    public bool HasReceivedBootstrap { get; private set; }
    public int BootstrapRequestsSent { get; private set; }

    public void RequestBootstrap(string reason = "unspecified")
    {
        _acceptReplies = true;
        BootstrapRequestsSent++;

        logger.LogInformation(
            "[VoiceDebug] Sending RequestVoiceSession RPC to server. Attempt={Attempt} Reason={Reason}",
            BootstrapRequestsSent,
            reason);

        Console.WriteLine(
            $"[OblivionVoice] Sending RequestVoiceSession RPC. Attempt={BootstrapRequestsSent} Reason={reason}");

        SendVoiceSession();
    }

    partial void OnVoiceSession(
        bool enabled,
        string advertisedHost,
        int port,
        string sessionToken,
        int sampleRate,
        int channels,
        int frameMilliseconds,
        int bitrate,
        bool enableDtx,
        bool enableFec,
        int microphoneBufferMilliseconds,
        int playbackBufferMilliseconds,
        float whisperMeters,
        float normalMeters,
        float shoutMeters,
        float maximumReceiveMeters,
        int defaultMode,
        bool useParentCell,
        bool serverSideRouting,
        string transmitKey,
        int transmitMode,
        string cycleRangeKey,
        string toggleMuteKey,
        bool radioEnabled,
        string radioTransmitKey,
        string brandName,
        bool debugEnabled,
        bool debugLoopbackMicrophone,
        bool debugLogPackets,
        bool debugLogClientStats,
        bool debugLogAudioLevels,
        bool debugLogKeyEvents,
        bool debugForceTransmit,
        int debugStatsIntervalSeconds,
        float worldUnitsPerMeter,
        bool invertPan)
    {
        if (!_acceptReplies || OblivionMp.Sdk.SDK.Sync.LocalPlayer is null) return;
        HasReceivedBootstrap = true;
        DisabledByServer = !enabled;

        logger.LogInformation("[VoiceDebug] VoiceSessionResponse RPC received from server. Enabled={Enabled} Host={Host}:{Port}", enabled, advertisedHost, port);
        Console.WriteLine($"[OblivionVoice] VoiceSessionResponse received. Enabled={enabled} Host={advertisedHost}:{port}");

        var settings = new VoiceClientSettings
        {
            Enabled = enabled,
            Host = advertisedHost,
            Port = port,
            SessionToken = sessionToken,
            SampleRate = sampleRate,
            Channels = channels,
            FrameMilliseconds = frameMilliseconds,
            Bitrate = bitrate,
            EnableDtx = enableDtx,
            EnableFec = enableFec,
            MicrophoneBufferMilliseconds = microphoneBufferMilliseconds,
            PlaybackBufferMilliseconds = playbackBufferMilliseconds,
            WhisperMeters = whisperMeters,
            NormalMeters = normalMeters,
            ShoutMeters = shoutMeters,
            MaximumReceiveMeters = maximumReceiveMeters,

            DefaultMode = Enum.IsDefined((VoiceMode)defaultMode) ? (VoiceMode)defaultMode : VoiceMode.Normal,

            UseParentCell = useParentCell,
            ServerSideRouting = serverSideRouting,
            TransmitKey = transmitKey,
            TransmitMode = Enum.IsDefined((TransmitMode)transmitMode) ? (TransmitMode)transmitMode : TransmitMode.Hold,
            CycleRangeKey = cycleRangeKey,
            ToggleMuteKey = toggleMuteKey,
            RadioEnabled = radioEnabled,
            RadioTransmitKey = radioTransmitKey,
            BrandName = brandName,
            DebugEnabled = debugEnabled,
            DebugLoopbackMicrophone = debugLoopbackMicrophone,
            DebugLogPackets = debugLogPackets,
            DebugLogClientStats = debugLogClientStats,
            DebugLogAudioLevels = debugLogAudioLevels,
            DebugLogKeyEvents = debugLogKeyEvents,
            DebugForceTransmit = debugForceTransmit,
            DebugStatsIntervalSeconds = debugStatsIntervalSeconds,
            WorldUnitsPerMeter = worldUnitsPerMeter,
            InvertPan = invertPan
        };

        logger.LogInformation("Received OblivionVoice configuration from server.");
        _ = runtime.BootstrapAsync(settings);
    }

    public void RequestDiagnosticPing(int nonce)
    {
        logger.LogInformation("[VoiceDebug] Sending DiagnosticPing nonce={Nonce}", nonce);
        Console.WriteLine($"[OblivionVoice] Sending DiagnosticPing nonce={nonce}");
        SendDiagnosticPing(nonce);
    }

    partial void OnDiagnosticPing(int nonce, int echoedNonce)
    {
        logger.LogInformation(
            "[VoiceDebug] DiagnosticPing response nonce={Nonce} echoed={Echoed}",
            nonce,
            echoedNonce);

        Console.WriteLine(
            $"[OblivionVoice] DiagnosticPing RESPONSE nonce={nonce} echoed={echoedNonce}");
    }
}
