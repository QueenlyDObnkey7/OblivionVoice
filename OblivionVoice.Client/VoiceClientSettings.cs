using OblivionVoice.Common;

namespace OblivionVoice.Client;

public sealed class VoiceClientSettings
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string SessionToken { get; init; } = string.Empty;
    public int SampleRate { get; init; } = 48000;
    public int Channels { get; init; } = 1;
    public int FrameMilliseconds { get; init; } = 20;
    public int Bitrate { get; init; } = 32000;
    public bool EnableDtx { get; init; } = true;
    public bool EnableFec { get; init; } = true;
    public int MicrophoneBufferMilliseconds { get; init; } = 40;
    public int PlaybackBufferMilliseconds { get; init; } = 250;
    public float WhisperMeters { get; init; } = 2f;
    public float NormalMeters { get; init; } = 8f;
    public float ShoutMeters { get; init; } = 25f;
    public float MaximumReceiveMeters { get; init; } = 35f;
    public VoiceMode DefaultMode { get; init; } = VoiceMode.Normal;
    public bool UseParentCell { get; init; } = true;
    public bool ServerSideRouting { get; init; }

    public float WorldUnitsPerMeter { get; init; } = 70f;

    public bool InvertPan { get; init; }

    public bool NoiseSuppressionEnabled { get; init; } = false;

    public bool VoiceActivityDetectionEnabled { get; init; } = false;

    public float VadOpenThresholdDb { get; init; } = 9f;

    public float VadCloseThresholdDb { get; init; } = 5f;

    public string TransmitKey { get; init; } = "N";
    public TransmitMode TransmitMode { get; init; } = TransmitMode.Hold;
    public string CycleRangeKey { get; init; } = "H";
    public string ToggleMuteKey { get; init; } = "M";
    public bool RadioEnabled { get; init; }
    public string RadioTransmitKey { get; init; } = "CapsLock";
    public string BrandName { get; init; } = "OblivionVoice";
    public bool DebugEnabled { get; init; }
    public bool DebugLoopbackMicrophone { get; init; }
    public bool DebugLogPackets { get; init; }
    public bool DebugLogClientStats { get; init; }
    public bool DebugLogAudioLevels { get; init; }
    public bool DebugLogKeyEvents { get; init; }
    public bool DebugForceTransmit { get; init; }
    public int DebugStatsIntervalSeconds { get; init; } = 5;
}
