using ReadyM.Api.Multiplayer;

namespace OblivionVoice.Common;

[ServerRpcContracts]
public static partial class VoiceRpcContracts
{

    [ClientToServer]
    public static partial void VoiceSession();

    [ServerToClient]
    public static partial void VoiceSession(
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
        bool invertPan
    );

    [ClientToServer]
    public static partial void DiagnosticPing(int nonce);

    [ServerToClient]
    public static partial void DiagnosticPing(int nonce, int echoedNonce);
}
