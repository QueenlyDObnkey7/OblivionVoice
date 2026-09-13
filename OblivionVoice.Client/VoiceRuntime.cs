using Microsoft.Extensions.Logging;
using OblivionVoice.Client.Audio;
using OblivionVoice.Client.Input;
using OblivionVoice.Client.Net;
using OblivionVoice.Client.Spatial;
using OblivionVoice.Common;

namespace OblivionVoice.Client;

public sealed class VoiceRuntime(
    UdpVoiceClient network,
    WasapiMicrophoneCapture microphone,
    ConcentusOpusCodec codec,
    WasapiPlaybackMixer playback,
    WindowsKeyPoller keyPoller,
    ReadyMKeyBindings readyMKeys,
    ISpatialVoiceProvider spatial,
    VoiceEnvironmentDirector environmentDirector,
    VoicePreprocessor preprocessor,
    NativeHudTextBridge hud,
    ILogger logger) : IDisposable
{
    public VoiceClientSettings Settings { get; private set; } = new();
    private VoicePreferences _preferences=new();
    private bool _preferencesLoaded;
    private volatile bool _settingsOpen;
    public VoicePreferences Preferences => _preferences;
    public string EffectiveTransmitKey => _preferences.TransmitKey ?? Settings.TransmitKey;
    public TransmitMode EffectiveTransmitMode => _preferences.TransmitMode ?? Settings.TransmitMode;
    public void EnsurePreferencesLoaded()
    {
        if(_preferencesLoaded)return;_preferencesLoaded=true;
        try {_preferences=VoicePreferences.Load();}
        catch(Exception ex){logger.LogWarning(ex,"Could not load local voice preferences; using defaults.");}
    }
    public void SetSettingsOpen(bool open)
    {
        _settingsOpen=open;
        if(open){Transmitting=false;keyPoller.Stop();UpdateHud();}
    }
    public void ApplyPreferences(VoicePreferences next)
    {
        next.Validate();
        var key=next.TransmitKey??Settings.TransmitKey;
        if(!WindowsKeyPoller.TryResolveVirtualKey(key,out _)||!Enum.TryParse<ReadyM.Sdk.Common.Input.Key>(key,true,out var parsed)||!Enum.IsDefined(parsed)||key.Equals("F5",StringComparison.OrdinalIgnoreCase)||key.Equals("F10",StringComparison.OrdinalIgnoreCase)||key.Equals(Settings.CycleRangeKey,StringComparison.OrdinalIgnoreCase)||key.Equals(Settings.ToggleMuteKey,StringComparison.OrdinalIgnoreCase)||key.Equals("Escape",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Choose a supported key that is not used for voice settings, mute or range.");
        if(IsBootstrapping)throw new InvalidOperationException("Voice is connecting. Try Apply again shortly.");
        var previous=_preferences;
        Transmitting=false;keyPoller.Stop();UpdateHud();
        bool restart=_initialized && next.InputDeviceId!=previous.InputDeviceId;
        try {
            if(restart)microphone.Start(Settings.SampleRate,Settings.Channels,Settings.FrameMilliseconds,Settings.MicrophoneBufferMilliseconds,next.InputDeviceId);
            readyMKeys.RegisterTransmit(key,this,Settings);
            next.Save();_preferences=next;
        } catch {
            if(restart)try {microphone.Start(Settings.SampleRate,Settings.Channels,Settings.FrameMilliseconds,Settings.MicrophoneBufferMilliseconds,previous.InputDeviceId);}catch(Exception ex){logger.LogError(ex,"Could not restore previous microphone.");}
            throw;
        }
    }

    public VoiceMode CurrentMode { get; private set; } = VoiceMode.Normal;
    private volatile bool _muted;
    public bool Muted { get => _muted; private set => _muted = value; }
    private volatile bool _transmitting;
    private readonly SpeechLevel _localSpeechLevel = new();
    public bool Transmitting
    {
        get => _transmitting;
        private set { _transmitting = value; if (!value) _localSpeechLevel.Clear(); }
    }
    public float LocalSpeechLevel => Settings.Enabled && Transmitting && !_settingsOpen && network.IsConnected ? _localSpeechLevel.Value : 0;
    public float GetSpeakerSpeechLevel(string speakerId) => Settings.Enabled && network.IsConnected && !Muted ? playback.GetSpeakerSpeechLevel(speakerId) : 0;

    private long _lastSpeechFrame;
    public bool IsSpeaking => Transmitting && !_settingsOpen && network.IsConnected
        && System.Diagnostics.Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastSpeechFrame)).TotalSeconds < .25;

    private bool _initialized;
    private int _bootstrapping;
    private int _lifecycle;
    private bool _lastConnected;
    public bool IsVoiceConnected => network.IsConnected;
    public bool IsBootstrapping => Volatile.Read(ref _bootstrapping) != 0;

    public void RefreshConnectionState()
    {
        var connected = network.IsConnected;
        if (_lastConnected == connected) return;
        _lastConnected = connected;
        if (!connected)
        {
            Transmitting = false;
            keyPoller.Stop();
            playback.ClearSpeechLevels();
            foreach (var speaker in playback.ActiveSpeakers.ToArray())
            {
                playback.RemoveSpeaker(speaker);
                environmentDirector.Forget(speaker);
            }
        }
        UpdateHud();
    }

    private long _microphoneFrames;
    private long _encodedPackets;
    private long _encodedBytes;
    private long _receivedPackets;
    private long _spatiallyDropped;
    private long _vadGatedFrames;
    private DateTimeOffset _nextStatsLog = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLevelLog = DateTimeOffset.MinValue;

    private short[] _processBuffer = [];

    public IReadOnlyCollection<string> ActiveSpeakers => playback.ActiveSpeakers;

    public async Task BootstrapAsync(VoiceClientSettings settings)
    {

        if (Interlocked.CompareExchange(ref _bootstrapping, 1, 0) != 0) return;
        var lifecycle = Volatile.Read(ref _lifecycle);
        try
        {
            if (_initialized && settings.Enabled)
            {
                if (!network.IsConnected)
                {
                    Transmitting = false;
                    keyPoller.Stop();
                    await network.ConnectAsync(settings);
                    if (lifecycle != Volatile.Read(ref _lifecycle)) network.Dispose();
                }
                return;
            }
            if (!settings.Enabled && _initialized) Dispose();

            EnsurePreferencesLoaded();
            Settings = settings;
            CurrentMode = settings.DefaultMode;
            if (!settings.Enabled)
            {
                logger.LogInformation("OblivionVoice is disabled by this server.");
                return;
            }

            try
            {

                readyMKeys.Register(settings, this);

                ConfigureSpatial(settings);
                ConfigurePreprocessor(settings);

                codec.Configure(settings.SampleRate, settings.Channels, settings.FrameMilliseconds, settings.Bitrate, settings.EnableDtx, settings.EnableFec);
                playback.Start(settings.SampleRate, settings.Channels, settings.PlaybackBufferMilliseconds, settings.FrameMilliseconds);
                microphone.FrameReady += OnMicrophoneFrame;
                try { microphone.Start(settings.SampleRate, settings.Channels, settings.FrameMilliseconds, settings.MicrophoneBufferMilliseconds,_preferences.InputDeviceId); }
                catch when(!string.IsNullOrEmpty(_preferences.InputDeviceId)) {
                    logger.LogWarning("Saved microphone is unavailable; using Windows default for this session.");
                    microphone.Start(settings.SampleRate,settings.Channels,settings.FrameMilliseconds,settings.MicrophoneBufferMilliseconds);
                }
                network.OpusFrameReceived += OnOpusFrame;
                await network.ConnectAsync(settings);
                if (lifecycle != Volatile.Read(ref _lifecycle))
                {
                    network.Dispose();
                    return;
                }
                _initialized = true;

                if (settings.DebugEnabled && settings.DebugForceTransmit && !_settingsOpen)
                {
                    Transmitting = true;
                    logger.LogWarning("[VoiceDebug] ForceTransmit is ENABLED. Microphone audio will transmit continuously while voice is active.");
                }

                UpdateHud();

                logger.LogInformation("{Brand} initialized. Mode={Mode} Range={Range}m", settings.BrandName, CurrentMode, GetCurrentRange());

                if (settings.DebugEnabled)
                {
                    logger.LogInformation(
                        "[VoiceDebug] Enabled Host={Host}:{Port} PTT={PttKey}/{PttMode} Loopback={Loopback} PacketLog={PacketLog} Stats={Stats} Levels={Levels} Interval={Interval}s",
                        settings.Host,
                        settings.Port,
                        settings.TransmitKey,
                        settings.TransmitMode,
                        settings.DebugLoopbackMicrophone,
                        settings.DebugLogPackets,
                        settings.DebugLogClientStats,
                        settings.DebugLogAudioLevels,
                        settings.DebugStatsIntervalSeconds);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OblivionVoice client initialization failed.");

                Console.WriteLine($"[OblivionVoice] CLIENT INIT FAILED: {ex}");
                Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Voice reconnection failed; automatic retry remains active.");
        }
        finally { Volatile.Write(ref _bootstrapping, 0); }
    }

    private void UpdateHud()
    {
        if (!Settings.Enabled) return;

        var body = $"{CurrentMode} - {GetCurrentRange():0}m";
        if (!network.IsConnected)
        {
            hud.SetText($"[CONNECTING] {body}");
            return;
        }

        hud.Hide(); // Connected voice activity is now the shared bottom-right speaker icon.

    }

    private void ConfigureSpatial(VoiceClientSettings settings)
    {
        if (spatial is not OblivionSpatialVoiceProvider oblivion) return;

        oblivion.WorldUnitsPerMeter = settings.WorldUnitsPerMeter;
        oblivion.UseParentCell = settings.UseParentCell;
        oblivion.InvertPan = settings.InvertPan;

        logger.LogInformation(
            "Spatial voice active. worldUnitsPerMeter={Units} useParentCell={Cell} invertPan={Invert}",
            settings.WorldUnitsPerMeter,
            settings.UseParentCell,
            settings.InvertPan);

        Console.WriteLine(
            $"[OblivionVoice] Spatial: {settings.WorldUnitsPerMeter} units/m, cells={settings.UseParentCell}, invertPan={settings.InvertPan}");
    }

    public string DescribeVoiceState() =>
        $"connected={network.IsConnected} host={Settings.Host}:{Settings.Port} " +
        $"mode={CurrentMode} range={GetCurrentRange():0}m units/m={Settings.WorldUnitsPerMeter} " +
        $"routing={Settings.ServerSideRouting} rx={_receivedPackets} dropped={_spatiallyDropped}";

    private void ConfigurePreprocessor(VoiceClientSettings settings)
    {
        preprocessor.Enabled = settings.NoiseSuppressionEnabled;
        preprocessor.UseVoiceActivityDetection = settings.VoiceActivityDetectionEnabled;
        preprocessor.OpenThresholdDb = settings.VadOpenThresholdDb;
        preprocessor.CloseThresholdDb = settings.VadCloseThresholdDb;
        preprocessor.Configure(settings.SampleRate, settings.FrameMilliseconds);

        Console.WriteLine(
            $"[OblivionVoice] Preprocessor: noiseSuppression={settings.NoiseSuppressionEnabled}, " +
            $"vad={settings.VoiceActivityDetectionEnabled} ({settings.VadOpenThresholdDb:0.#}/{settings.VadCloseThresholdDb:0.#}dB)");
    }

    public void OnTransmitKeyPressed()
    {
        if (!_initialized || !network.IsConnected)
        {
            if (Settings.DebugEnabled && Settings.DebugLogKeyEvents)
                logger.LogWarning("[VoiceDebug] PTT key fired before voice initialization completed.");
            return;
        }

        if (Settings.DebugEnabled && Settings.DebugLogKeyEvents)
        {
            logger.LogInformation(
                "[VoiceDebug] PTT press accepted mode={Mode} currentlyTransmitting={Transmitting}",
                Settings.TransmitMode,
                Transmitting);
        }

        if (EffectiveTransmitMode == TransmitMode.Toggle)
        {
            SetTransmitting(!Transmitting);
            return;
        }

        SetTransmitting(true);

        keyPoller.WatchForRelease(
            EffectiveTransmitKey,
            () => SetTransmitting(false),
            Settings.DebugEnabled && Settings.DebugLogKeyEvents);
    }

    public void SetTransmitting(bool transmitting)
    {
        if (!_initialized || (transmitting && _settingsOpen)) return;
        if (Transmitting == transmitting) return;
        Transmitting = transmitting;
        if (Settings.DebugEnabled && Settings.DebugLogKeyEvents)
            logger.LogInformation("[VoiceDebug] PTT transmitting={Value}", Transmitting);

        UpdateHud();
    }

    public void CycleMode()
    {
        CurrentMode = CurrentMode switch
        {
            VoiceMode.Whisper => VoiceMode.Normal,
            VoiceMode.Normal => VoiceMode.Shout,
            _ => VoiceMode.Whisper
        };
        logger.LogInformation("Voice mode: {Mode} ({Range}m)", CurrentMode, GetCurrentRange());
        if (Settings.DebugEnabled && Settings.DebugLogKeyEvents)
            logger.LogInformation("[VoiceDebug] Range changed Mode={Mode} Range={Range}m", CurrentMode, GetCurrentRange());

        UpdateHud();
    }

    public void ToggleMute()
    {
        Muted = !Muted;
        playback.SetMuted(Muted);
        logger.LogInformation("Voice mute: {Muted}", Muted);
        if (Settings.DebugEnabled && Settings.DebugLogKeyEvents)
            logger.LogInformation("[VoiceDebug] Incoming mute={Muted}", Muted);

        UpdateHud();
    }

    public float GetCurrentRange() => CurrentMode switch
    {
        VoiceMode.Whisper => Settings.WhisperMeters,
        VoiceMode.Shout => Settings.ShoutMeters,
        _ => Settings.NormalMeters
    };

    private void OnMicrophoneFrame(ReadOnlyMemory<short> pcm)
    {
        _microphoneFrames++;

        if (pcm.Length == 0) { _localSpeechLevel.Clear(); return; }

        if (_processBuffer.Length < pcm.Length)
            _processBuffer = new short[pcm.Length];

        var frame = _processBuffer.AsSpan(0, pcm.Length);
        pcm.Span.CopyTo(frame);

        var hasSpeech = preprocessor.Process(frame);
        _preferences.ApplyGain(frame);

        if (Settings.DebugEnabled && Settings.DebugLogAudioLevels)
            MaybeLogAudioLevel(frame);

        MaybeLogStats();

        if (_settingsOpen || !Transmitting || !network.IsConnected) { _localSpeechLevel.Clear(); return; }

        if (!hasSpeech)
        {
            _localSpeechLevel.Clear();
            _vadGatedFrames++;
            return;
        }

        try
        {
            var encoded = codec.Encode(frame);
            if (encoded.Length > 2)
            {
                _localSpeechLevel.Publish(frame);
                Interlocked.Exchange(ref _lastSpeechFrame,System.Diagnostics.Stopwatch.GetTimestamp());
                _encodedPackets++;
                _encodedBytes += encoded.Length;
                _ = network.SendOpusAsync(CurrentMode, encoded);
            }
            else _localSpeechLevel.Clear();
        }
        catch (Exception ex)
        {
            _localSpeechLevel.Clear();
            logger.LogDebug(ex, "Voice encode/send failed.");
        }
    }

private void OnOpusFrame(
        string speakerId,
        ushort sequence,
        VoiceMode mode,
        VoiceEnvironment environment,
        ReadOnlyMemory<byte> encoded)
    {
        _receivedPackets++;
        MaybeLogStats();

        if (Muted) return;

        var range = mode switch
        {
            VoiceMode.Whisper => Settings.WhisperMeters,
            VoiceMode.Shout => Settings.ShoutMeters,
            _ => Settings.NormalMeters
        };

        range = MathF.Min(range, Settings.MaximumReceiveMeters);
        var placement = spatial.Resolve(speakerId, range);

        if (!placement.Audible)
        {
            _spatiallyDropped++;
            return;
        }

        var gain = placement.Gain;

        environmentDirector.Apply(speakerId, environment);

        playback.Submit(speakerId, sequence, encoded, gain, placement.Pan);

        if (Settings.DebugEnabled && Settings.DebugLogPackets)
        {
            logger.LogInformation(
                "[VoiceDebug] Spatial speaker={Speaker} env={Environment} distance={Distance:0.0}m gain={Gain:0.00} pan={Pan:+0.00;-0.00; 0.00}",
                speakerId,
                environment,
                placement.DistanceMeters,
                gain,
                placement.Pan);
        }
    }

    private void MaybeLogStats()
    {
        if (!Settings.DebugEnabled || !Settings.DebugLogClientStats) return;

        var now = DateTimeOffset.UtcNow;
        if (now < _nextStatsLog) return;

        _nextStatsLog = now.AddSeconds(Math.Max(1, Settings.DebugStatsIntervalSeconds));

        logger.LogInformation(
            "[VoiceDebug] Stats connected={Connected} transmitting={Transmitting} micFrames={MicFrames} txPackets={TxPackets} txOpusBytes={TxBytes} vadGated={VadGated} rxPackets={RxPackets} spatialDropped={Dropped}",
            network.IsConnected,
            Transmitting,
            _microphoneFrames,
            _encodedPackets,
            _encodedBytes,
            _vadGatedFrames,
            _receivedPackets,
            _spatiallyDropped);

        logger.LogInformation("[VoiceDebug] Mic {Preprocessor}", preprocessor.Describe());
        logger.LogInformation("[VoiceDebug] Jitter {Jitter}", playback.DescribeJitter());
    }

    private void MaybeLogAudioLevel(ReadOnlySpan<short> pcm)
    {
        if (pcm.Length == 0) return;

        var now = DateTimeOffset.UtcNow;
        if (now < _nextLevelLog) return;

        _nextLevelLog = now.AddSeconds(Math.Max(1, Settings.DebugStatsIntervalSeconds));

        double sumSquares = 0;
        for (var i = 0; i < pcm.Length; i++)
        {
            var normalized = pcm[i] / 32768.0;
            sumSquares += normalized * normalized;
        }

        var rms = Math.Sqrt(sumSquares / pcm.Length);
        var dbfs = rms <= 0.0000001 ? -120.0 : 20.0 * Math.Log10(rms);

        logger.LogInformation(
            "[VoiceDebug] Microphone level RMS={Rms:0.0000} dBFS={Dbfs:0.0} speech={Speech} transmitting={Transmitting}",
            rms,
            dbfs,
            preprocessor.SpeechDetected,
            Transmitting);
    }

    public void Dispose()
    {
        VoiceSettingsBook.Close();
        Interlocked.Increment(ref _lifecycle);
        Transmitting = false;
        _lastConnected = false;
        Settings = new();
        _initialized = false;

        OblivionUI.SpeakerIndicators.Clear();
        try { hud.Hide(); } catch { }

        keyPoller.Stop();
        microphone.FrameReady -= OnMicrophoneFrame;
        network.OpusFrameReceived -= OnOpusFrame;
        microphone.Dispose();
        network.Dispose();
        playback.Dispose();
        codec.Dispose();
    }
}
