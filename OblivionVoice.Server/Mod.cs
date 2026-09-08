using Microsoft.Extensions.Logging;
using OblivionMp.Sdk.Serverside;
using OblivionVoice.Common;
using ReadyM.Relay.Server.Sdk;
using ReadyM.Relay.Server.Sdk.Ecs.Components;
using ReadyM.Relay.Server.Sdk.Ecs.Systems;

namespace OblivionVoice.Server;

public sealed class Mod : ServerModBase
{
    public Mod()
    {
        ServerStartupTrace.Write("MOD CONSTRUCTOR");
    }

    protected override void RegisterComponents(IComponentRegistry registry)
    {
        ServerStartupTrace.Write(
            "RegisterComponents BEGIN",
            $"RegistryType={registry.GetType().FullName}");

        try
        {

            ServerStartupTrace.Write("RegisterComponents COMPLETE");
        }
        catch (Exception ex)
        {
            ServerStartupTrace.Exception("RegisterComponents FAILED", ex);
            throw;
        }
    }

    protected override void Init()
    {
        ServerStartupTrace.Write("Init BEGIN");

        try
        {

            var changed = VoiceRpcOffsets.ApplyPin();

            ServerStartupTrace.Write(
                changed ? "RPC OFFSET PINNED" : "RPC OFFSET UNCHANGED",
                VoiceRpcOffsets.Describe());

            ServerStartupTrace.Write(
                "RPC MANIFEST SURVEY",
                VoiceRpcOffsets.SurveyManifestAssemblies().ToArray());

            ServerStartupTrace.Write("Registering VoiceConfigLoader");
            Services.RegisterSingleton<VoiceConfigLoader>();

            ServerStartupTrace.Write("Registering VoiceSessionTokenService");
            Services.RegisterSingleton<VoiceSessionTokenService>();

            ServerStartupTrace.Write("Registering UdpVoiceRelay");
            Services.RegisterSingleton<UdpVoiceRelay>();

            ServerStartupTrace.Write("Registering VoicePositionCache");
            Services.RegisterSingleton<VoicePositionCache>();
            ServerStartupTrace.Write("Registering VoiceRoutingSystem");
            Services.RegisterSingleton<ModSystemBase, VoiceRoutingSystem>();

            ServerStartupTrace.Write("Registering VoiceServerRpc");
            Services.RegisterSingleton<VoiceServerRpc>();
            ServerStartupTrace.Write("Registering VoiceRpcDiagnosticsSystem");
            Services.RegisterSingleton<ModSystemBase, VoiceRpcDiagnosticsSystem>();
            ServerStartupTrace.Write("Resolving VoiceConfigLoader");
            var configLoader = Services.Resolve<VoiceConfigLoader>();

            ServerStartupTrace.Write("Loading voiceconfig.json");
            var config = configLoader.Load();

            ServerStartupTrace.Write(
                "Config loaded",
                $"Enabled={config.Enabled}",
                $"Bind={config.Network.BindAddress}:{config.Network.Port}",
                $"AdvertisedHost={config.Network.AdvertisedHost}",
                $"Debug={config.Debug.Enabled}");

            if (config.Network.AdvertisedHost is "127.0.0.1" or "localhost" or "::1" or "CHANGE-ME.example.com")
            {
                var warning =
                    $"AdvertisedHost is '{config.Network.AdvertisedHost}'. Remote clients will try to " +
                    "reach the voice relay on their own machine and voice will fail silently. " +
                    "Set it to this server's public address.";

                ServerStartupTrace.Write("ADVERTISEDHOST WARNING", warning);
                Console.WriteLine($"[OblivionVoice] WARNING: {warning}");

                try { Services.Resolve<ILogger>().LogWarning(warning); } catch { }
            }

            ServerStartupTrace.Write("Resolving UdpVoiceRelay");
            var relay = Services.Resolve<UdpVoiceRelay>();

            ServerStartupTrace.Write("Starting UDP relay");
            relay.Start();

            ServerStartupTrace.Write("UDP relay Start() returned");

            try
            {
                var logger = Services.Resolve<ILogger>();
                logger.LogInformation("OblivionVoice 0.4.4 server initialized.");
                logger.LogInformation("[VoiceDebug] {OffsetState}", VoiceRpcOffsets.Describe());
            }
            catch (Exception loggerEx)
            {
                ServerStartupTrace.Exception("ILogger write FAILED", loggerEx);
            }

            Console.WriteLine("[OblivionVoice] 0.4.4 server initialized.");
            Console.WriteLine($"[OblivionVoice] {VoiceRpcOffsets.Describe()}");
            ServerStartupTrace.Write("Init COMPLETE");

        }
        catch (Exception ex)
        {
            ServerStartupTrace.Exception("Init FAILED", ex);

            try
            {
                Console.Error.WriteLine(
                    $"[OblivionVoice] SERVER INIT FAILED: {ex}");
            }
            catch
            {
            }

            throw;
        }
    }
}
