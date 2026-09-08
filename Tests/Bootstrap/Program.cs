using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OblivionVoice.Client;
using OblivionMp.Sdk;
using ReadyM.Modloader.Mods;

var rpc = new VoiceServerRpc();
var runtime = new VoiceRuntime();
var system = new VoiceBootstrapSystem(rpc, runtime, NullLogger.Instance);
var update = typeof(VoiceBootstrapSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
void Tick(float seconds) => update.Invoke(system, new object[] { new UpdateTick { deltaTime = seconds } });
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
Tick(100);
Check(rpc.Requests == 0, "no requests outside game");
SDK.Sync.LocalPlayer = new Player("one");
Tick(0.5f);
Check(rpc.Requests == 0, "initial join delay");
Tick(0.5f);
Check(rpc.Requests == 1, "initial session request");
Tick(9);
Check(rpc.Requests == 1, "handshake allowed to finish");
Tick(1);
Check(rpc.Requests == 2, "retry missing bootstrap or handshake");
runtime.IsBootstrapping = true;
Tick(100);
Check(rpc.Requests == 2, "no overlapping initialization");
runtime.IsBootstrapping = false;
runtime.IsVoiceConnected = true;
Tick(100);
Check(rpc.Requests == 2, "healthy session does not rebootstrap");
runtime.IsVoiceConnected = false;
Tick(1);
Check(rpc.Requests == 3, "automatic request after connection loss");
for (var i = 0; i < 20; i++) Tick(30);
Check(rpc.Requests == 23, "retries continue beyond original ten-attempt limit");
rpc.DisabledByServer = true;
Tick(100);
Check(rpc.Requests == 23, "server-disabled voice stops retries");
SDK.Sync.LocalPlayer = null;
Tick(1);
Check(!rpc.DisabledByServer && runtime.Disposals == 2, "leaving clears voice state");
SDK.Sync.LocalPlayer = new Player("two");
Tick(1);
Check(rpc.Requests == 24, "next join starts a new session");
Console.WriteLine("All bootstrap checks passed.");

namespace OblivionMp.Sdk
{
    public readonly record struct Player(string PlayerId);
    public static class SDK
    {
        public static class Sync { public static Player? LocalPlayer { get; set; } }
    }
}
namespace ReadyM.Modloader.Mods
{
    public struct UpdateTick { public float deltaTime; }
    public abstract class ModSystemBase { protected abstract void OnUpdate(UpdateTick tick); }
}
namespace OblivionVoice.Client
{
    public sealed class VoiceServerRpc
    {
        public int Requests;
        public bool DisabledByServer;
        public void ResetBootstrap() => DisabledByServer = false;
        public void RequestBootstrap(string reason) => Requests++;
    }
    public sealed class VoiceRuntime
    {
        public bool IsVoiceConnected;
        public bool IsBootstrapping;
        public int Disposals;
        public void Dispose() { Disposals++; IsVoiceConnected = false; }
        public void RefreshConnectionState() { }
    }
}
