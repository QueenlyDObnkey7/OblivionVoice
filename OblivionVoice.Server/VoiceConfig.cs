using OblivionVoice.Common;

namespace OblivionVoice.Server;

public sealed class VoiceConfig
{
    public bool Enabled { get; set; } = true;
    public NetworkSection Network { get; set; } = new();
    public AudioSection Audio { get; set; } = new();
    public ProximitySection Proximity { get; set; } = new();
    public ControlsSection Controls { get; set; } = new();
    public RadioSection Radio { get; set; } = new();
    public UiSection Ui { get; set; } = new();
    public DebugSection Debug { get; set; } = new();

    public sealed class NetworkSection
    {
        public string BindAddress { get; set; } = "0.0.0.0";
        public string AdvertisedHost { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 22100;
        public int SessionTokenTtlSeconds { get; set; } = 60;
        public int ClientTimeoutSeconds { get; set; } = 20;
        public int MaxPacketBytes { get; set; } = 1200;
        public int MaxAudioPacketsPerSecond { get; set; } = 80;
    }

    public sealed class AudioSection
    {
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 1;
        public int FrameMilliseconds { get; set; } = 20;
        public int Bitrate { get; set; } = 32000;
        public bool EnableDtx { get; set; } = true;
        public bool EnableFec { get; set; } = true;
        public int MicrophoneBufferMilliseconds { get; set; } = 40;
        public int PlaybackBufferMilliseconds { get; set; } = 250;
    }

    public sealed class ProximitySection
    {
        public VoiceMode DefaultMode { get; set; } = VoiceMode.Normal;
        public float WhisperMeters { get; set; } = 2f;
        public float NormalMeters { get; set; } = 8f;
        public float ShoutMeters { get; set; } = 25f;
        public float MaximumReceiveMeters { get; set; } = 35f;
        public bool UseParentCell { get; set; } = true;
        public float WorldUnitsPerMeter { get; set; } = 70f;
        public bool ServerSideRouting { get; set; } = false;
        public bool InvertPan { get; set; }
    }

    public sealed class ControlsSection
    {
        public string TransmitKey { get; set; } = "N";
        public TransmitMode TransmitMode { get; set; } = TransmitMode.Hold;
        public string CycleRangeKey { get; set; } = "H";
        public string ToggleMuteKey { get; set; } = "M";
        public string RadioTransmitKey { get; set; } = "CapsLock";
    }

    public sealed class RadioSection
    {
        public bool Enabled { get; set; }
        public int DefaultChannel { get; set; } = 1;
        public int MaximumChannels { get; set; } = 32;
    }

    public sealed class UiSection
    {
        public bool Enabled { get; set; } = true;
        public bool ShowSpeakingIndicator { get; set; } = true;
        public bool ShowCurrentRange { get; set; } = true;
        public string BrandName { get; set; } = "OblivionVoice";
    }

    public sealed class DebugSection
    {

        public bool Enabled { get; set; }

        public bool LogPackets { get; set; }

        public bool LoopbackMicrophone { get; set; }

        public bool LogClientStats { get; set; } = true;

        public bool LogAudioLevels { get; set; } = true;

        public bool LogKeyEvents { get; set; } = true;

        public bool ForceTransmit { get; set; }

        public int StatsIntervalSeconds { get; set; } = 5;
    }

    public void Validate()
    {
        if (Network.Port is < 1 or > 65535)
            throw new InvalidOperationException("Network.Port must be between 1 and 65535.");
        if (Network.SessionTokenTtlSeconds is < 10 or > 600)
            throw new InvalidOperationException("Network.SessionTokenTtlSeconds must be 10-600.");
        if (Network.ClientTimeoutSeconds is < 5 or > 300)
            throw new InvalidOperationException("Network.ClientTimeoutSeconds must be 5-300.");
        if (Network.MaxPacketBytes is < 256 or > 65000)
            throw new InvalidOperationException("Network.MaxPacketBytes must be 256-65000.");
        if (Network.MaxAudioPacketsPerSecond is < 10 or > 500)
            throw new InvalidOperationException("Network.MaxAudioPacketsPerSecond must be 10-500.");
        if (Audio.SampleRate != 48000)
            throw new InvalidOperationException("This release currently requires Audio.SampleRate = 48000.");
        if (Audio.Channels != 1)
            throw new InvalidOperationException("This release currently requires mono Audio.Channels = 1.");
        if (Audio.FrameMilliseconds is not (10 or 20 or 40 or 60))
            throw new InvalidOperationException("Audio.FrameMilliseconds must be 10, 20, 40 or 60.");
        if (Audio.Bitrate is < 6000 or > 128000)
            throw new InvalidOperationException("Audio.Bitrate must be 6000-128000.");
        if (!float.IsFinite(Proximity.WorldUnitsPerMeter) || Proximity.WorldUnitsPerMeter <= 0)
            throw new InvalidOperationException("WorldUnitsPerMeter must be finite and greater than zero.");
        if (!float.IsFinite(Proximity.WhisperMeters) || !float.IsFinite(Proximity.NormalMeters) || !float.IsFinite(Proximity.ShoutMeters) || !float.IsFinite(Proximity.MaximumReceiveMeters))
            throw new InvalidOperationException("All voice ranges must be finite.");
        if (Proximity.WhisperMeters <= 0 || Proximity.NormalMeters <= Proximity.WhisperMeters || Proximity.ShoutMeters <= Proximity.NormalMeters)
            throw new InvalidOperationException("Ranges must satisfy Whisper < Normal < Shout.");
        if (Proximity.MaximumReceiveMeters < Proximity.ShoutMeters)
            throw new InvalidOperationException("MaximumReceiveMeters must be >= ShoutMeters.");
        if (string.IsNullOrWhiteSpace(Network.AdvertisedHost))
            throw new InvalidOperationException("Network.AdvertisedHost cannot be blank.");
        if (string.IsNullOrWhiteSpace(Controls.TransmitKey))
            throw new InvalidOperationException("Controls.TransmitKey cannot be blank.");
        if (Debug.StatsIntervalSeconds is < 1 or > 60)
            throw new InvalidOperationException("Debug.StatsIntervalSeconds must be 1-60.");
    }
}
