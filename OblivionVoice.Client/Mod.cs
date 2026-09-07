using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionVoice.Client.Audio;
using OblivionVoice.Client.Input;
using OblivionVoice.Client.Net;
using OblivionVoice.Client.Spatial;
using OblivionVoice.Common;
using ReadyM.Api.DI;
using ReadyM.Sdk.Common;
using ReadyM.Sdk.Common.Input;
using OblivionMp.Sdk.Entities.Extensions;

namespace OblivionVoice.Client;

public sealed class Mod : ModBase
{
    public override string Name => "OblivionVoice";

    private VoiceServerRpc _serverRpc = null!;

    protected override void RegisterServices(IDependencyContainer services)
    {
        services.RegisterSingleton<UdpVoiceClient>();
        services.RegisterSingleton<WasapiMicrophoneCapture>();
        services.RegisterSingleton<ConcentusOpusCodec>();
        services.RegisterSingleton<WasapiPlaybackMixer>();
        services.RegisterSingleton<WindowsKeyPoller>();
        services.RegisterSingleton<ReadyMKeyBindings>();
        services.RegisterSingleton<ISpatialVoiceProvider, OblivionSpatialVoiceProvider>();
        services.RegisterSingleton<VoiceEnvironmentDirector>();
        services.RegisterSingleton<VoiceRuntime>();
        services.RegisterSingleton<VoiceServerRpc>();
        services.RegisterSingleton<NativeHudTextBridge>();
        services.RegisterSingleton<VoicePreprocessor>();
        _serverRpc = services.Resolve<VoiceServerRpc>();
        _runtime = services.Resolve<VoiceRuntime>();
        _spatial = services.Resolve<ISpatialVoiceProvider>();

    }
        private ISpatialVoiceProvider _spatial = null!;
    private VoiceRuntime _runtime = null!;
    public override void Start()
    {
        Logger.LogInformation("OblivionVoice 0.4.3 client Start() called.");
        Console.WriteLine("[OblivionVoice] 0.4.3 client Start() called.");

        ApplyRpcOffsetPin();

        SDK.Input.RegisterKeyBind(Key.F10, () =>
        {
            var canApply = SDK.Input.CanApplyInput();
            Console.WriteLine($"[VoiceDebug] F10 input probe fired. CanApplyInput={canApply}");
            Logger.LogInformation("[VoiceDebug] F10 input probe fired. CanApplyInput={CanApply}", canApply);
        });

                SDK.Input.RegisterKeyBind(Key.F7, () =>
        {
            var local = SDK.Sync.LocalPlayer;
            if (local is not { } me) return;
            if (_spatial is not OblivionSpatialVoiceProvider oblivion) return;

            foreach (var other in SDK.Sync.AllPlayers)
            {
                if (other.PlayerId == me.PlayerId) continue;
                var raw = (other.Location - me.Location).Length();
                Logger.LogInformation("[VoiceDebug] {Name}: {Note}", other.Nickname, oblivion.CalibrateNote(raw));
            }
        });

        SDK.Input.RegisterKeyBind(Key.F6, () =>
        {
            Console.WriteLine($"[VoiceDebug] {_runtime.DescribeVoiceState()}");
        });

        Console.WriteLine("[OblivionVoice] Registered F10 bootstrap input probe.");

        SDK.Input.RegisterKeyBind(Key.F9, () =>
        {
            var canApply = SDK.Input.CanApplyInput();
            var localPlayerReady = SDK.Sync.LocalPlayer is not null;

            Console.WriteLine(
                $"[VoiceDebug] F9 RPC probe fired. CanApplyInput={canApply} LocalPlayerReady={localPlayerReady}");

            Logger.LogInformation(
                "[VoiceDebug] F9 RPC probe fired. CanApplyInput={CanApply} LocalPlayerReady={LocalPlayerReady}",
                canApply,
                localPlayerReady);

            if (canApply)
                _serverRpc.RequestBootstrap("manual F9 probe");
        });

        SDK.Input.RegisterKeyBind(Key.F8, () =>
        {
            Console.WriteLine($"[VoiceDebug] {VoiceRpcOffsets.Describe()}");
            foreach (var line in VoiceRpcOffsets.SurveyManifestAssemblies())
                Console.WriteLine($"[VoiceDebug]   {line}");
        });

        Console.WriteLine("[OblivionVoice] Registered F9 manual RPC bootstrap probe.");
        Console.WriteLine("[OblivionVoice] Registered F8 RPC offset dump.");
        Console.WriteLine("[OblivionVoice] After joining, press F9 to request the voice session.");
    }

    private void ApplyRpcOffsetPin()
    {
        try
        {
            var before = VoiceRpcOffsets.CurrentOffset;

            if (VoiceRpcOffsets.PinnedOffset is not { } pinned || pinned == before)
            {
                Logger.LogInformation("[VoiceDebug] {OffsetState}", VoiceRpcOffsets.Describe());
                Console.WriteLine($"[OblivionVoice] {VoiceRpcOffsets.Describe()}");
                return;
            }

            _serverRpc.Dispose();

            VoiceRpcOffsets.ApplyPin();

            _serverRpc.OnScopeStart();

            Logger.LogInformation(
                "[VoiceDebug] Re-registered server RPC handlers. {OffsetState}",
                VoiceRpcOffsets.Describe());

            Console.WriteLine(
                $"[OblivionVoice] Re-registered server RPC handlers. {VoiceRpcOffsets.Describe()}");
        }
        catch (Exception ex)
        {

            Logger.LogError(ex, "[VoiceDebug] Failed to pin server RPC offset.");
            Console.WriteLine($"[OblivionVoice] Failed to pin server RPC offset: {ex.Message}");
        }
    }
}
