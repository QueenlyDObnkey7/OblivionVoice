using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using OblivionVoice.Client;
using OblivionVoice.Client.Net;
using OblivionVoice.Common;
using OblivionVoice.Server;
using ReadyM.Relay.Common.Oblivion.ECS.Values;
using Placement = OblivionVoice.Server.VoicePositionCache.PlayerPlacement;

var log = NullLogger.Instance;
var loader = new VoiceConfigLoader(log);
var config = loader.Current;
config.Network.BindAddress = "127.0.0.1";
using (var reserve = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
    config.Network.Port = ((IPEndPoint)reserve.Client.LocalEndPoint!).Port;
config.Proximity.WorldUnitsPerMeter = 1;
config.Proximity.ServerSideRouting = true;
config.Proximity.UseParentCell = true;
var cache = new VoicePositionCache(log);
var tokens = new VoiceSessionTokenService(loader);
using var relay = new UdpVoiceRelay(loader, tokens, cache, log);
relay.Start();
using var speaker = new UdpVoiceClient(log);
using var listener = new UdpVoiceClient(log);
int received = 0;
VoiceEnvironment lastEnvironment = VoiceEnvironment.Outdoor;
listener.OpusFrameReceived += (_, _, _, environment, _) => { lastEnvironment = environment; Interlocked.Increment(ref received); };
VoiceClientSettings Settings(string id) => new()
{
    Enabled = true, Host = "127.0.0.1", Port = config.Network.Port,
    SessionToken = tokens.Issue(id).Token
};
await speaker.ConnectAsync(Settings("speaker"));
await listener.ConnectAsync(Settings("listener"));
await Until(() => speaker.IsConnected && listener.IsConnected);
Console.WriteLine("PASS: UDP authentication");

Placement Cell(float distance, ParentCellKind kind = ParentCellKind.Interior, int id = 1) =>
    new(new Vector3(distance, 0, 0), kind, id, 0, kind == ParentCellKind.Interior ? VoiceEnvironment.Room : VoiceEnvironment.Outdoor);
void Snapshot(Placement? a, Placement? b, long age = 0)
{
    var placements = new Dictionary<string, Placement>();
    if (a.HasValue) placements["speaker"] = a.Value;
    if (b.HasValue) placements["listener"] = b.Value;
    var type = typeof(VoicePositionCache).GetNestedType("Snapshot", BindingFlags.NonPublic)!;
    var snapshot = Activator.CreateInstance(type, placements, Environment.TickCount64 - age)!;
    typeof(VoicePositionCache).GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(cache, snapshot);
}
async Task Check(string name, Placement? a, Placement? b, bool expected, long age = 0, VoiceMode mode = VoiceMode.Normal)
{
    Snapshot(a, b, age);
    var before = Volatile.Read(ref received);
    await speaker.SendOpusAsync(mode, new byte[] { 1, 2, 3 });
    if (expected) await Until(() => Volatile.Read(ref received) > before);
    else await Task.Delay(100);
    if ((Volatile.Read(ref received) > before) != expected) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
}
await Check("same interior inside range", Cell(0), Cell(5), true);
await Check("exact range boundary", Cell(0), Cell(8), true);
await Check("outside range", Cell(0), Cell(8.1f), false);
await Check("whisper range", Cell(0), Cell(3), false, mode: VoiceMode.Whisper);
await Check("shout range", Cell(0), Cell(24), true, mode: VoiceMode.Shout);
await Check("separate interiors", Cell(0), Cell(1, id: 2), false);
await Check("interior versus exterior", Cell(0), Cell(1, ParentCellKind.Exterior), false);
await Check("unknown cell", Cell(0), Cell(1, ParentCellKind.Unknown), false);
await Check("missing speaker", null, Cell(1), false);
await Check("missing listener", Cell(0), null, false);
await Check("stale snapshot", Cell(0), Cell(1), false, age: 1001);
await Check("fresh snapshot recovery", Cell(0), Cell(1), true);
config.Proximity.ServerSideRouting = false;
await Check("cell filtering without server distance filtering", Cell(0), Cell(1, id: 2), false);
await Check("distance filtering disabled on relay", Cell(0), Cell(50), true);
if (lastEnvironment != VoiceEnvironment.Room) throw new Exception("Environment lost when distance filtering disabled");
config.Proximity.UseParentCell = false;
await Check("both filters disabled permits missing positions", null, null, true);
config.Proximity.ServerSideRouting = true;
await Check("cell filtering disabled permits cross-cell nearby voice", Cell(0), Cell(1, id: 2), true);
await Check("distance still enforced without cell filtering", Cell(0), Cell(50, id: 2), false);
await Task.Delay(2500);
if (!speaker.IsConnected || !listener.IsConnected) throw new Exception("Heartbeat connectivity");
Console.WriteLine("PASS: quiet-session heartbeat replies");
relay.Dispose();
await Until(() => !speaker.IsConnected && !listener.IsConnected, 20000);
Console.WriteLine("PASS: relay loss detection");
using var restarted = new UdpVoiceRelay(loader, tokens, cache, log);
restarted.Start();
await speaker.ConnectAsync(Settings("speaker"));
await listener.ConnectAsync(Settings("listener"));
await Until(() => speaker.IsConnected && listener.IsConnected);
await Check("fresh-token reconnection restores delivery", Cell(0), Cell(1), true);
var oldToken = Settings("consumed");
using var first = new UdpVoiceClient(log);
await first.ConnectAsync(oldToken);
await Until(() => first.IsConnected);
using var replay = new UdpVoiceClient(log);
await replay.ConnectAsync(oldToken);
await Task.Delay(300);
if (replay.IsConnected) throw new Exception("Token reused");
Console.WriteLine("PASS: consumed token rejected");
config.Proximity.WorldUnitsPerMeter = 0;
try { config.Validate(); throw new Exception("Invalid units accepted"); }
catch (InvalidOperationException) { Console.WriteLine("PASS: invalid units rejected"); }
Console.WriteLine("All voice checks passed.");

static async Task Until(Func<bool> condition, int timeout = 3000)
{
    var start = Environment.TickCount64;
    while (!condition())
    {
        if (Environment.TickCount64 - start > timeout) throw new TimeoutException();
        await Task.Delay(20);
    }
}

namespace OblivionVoice.Server
{
    internal static class ServerStartupTrace
    {
        public static void Write(string message, params string[] details) { }
    }
}
