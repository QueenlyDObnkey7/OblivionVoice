using OblivionVoice.Common;
using ReadyM.Relay.Server.Sdk.Ecs.Systems;
using ReadyM.Relay.Server.Sdk.Rpc;
using System.Collections;
using System.Reflection;

namespace OblivionVoice.Server;

public sealed class VoiceRpcDiagnosticsSystem(RpcApi rpc, VoiceServerRpc voiceRpc) : ModSystemBase
{
    private bool _reported;

    protected override void OnUpdate(UpdateTick tick)
    {
        if (_reported)
            return;

        _reported = true;

        try
        {
            var details = new List<string>
            {
                VoiceRpcOffsets.Describe(),
                $"VoiceServerRpc instance={voiceRpc.GetHashCode()}",
                $"RpcApi instance={rpc.GetHashCode()}"
            };

            details.AddRange(DescribeRegisteredCodes(rpc));

            ServerStartupTrace.Write("RPC HANDLER TABLE", details.ToArray());

            Console.WriteLine("[OblivionVoice] RPC handler table:");
            foreach (var line in details)
                Console.WriteLine($"[OblivionVoice]   {line}");
        }
        catch (Exception ex)
        {
            ServerStartupTrace.Exception("RPC HANDLER TABLE FAILED", ex);
        }
    }

    private static IEnumerable<string> DescribeRegisteredCodes(RpcApi rpc)
    {
        var field = typeof(RpcApi).GetField("_toCode", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field?.GetValue(rpc) is not IDictionary map)
        {
            yield return "_toCode not readable - SDK internals changed.";
            yield break;
        }

        if (map.Count == 0)
        {
            yield return "NO server RPC handlers registered at all.";
            yield break;
        }

        foreach (DictionaryEntry entry in map)
        {
            var target = (entry.Key as Delegate)?.Target;
            var owner = target?.GetType().FullName ?? entry.Key?.GetType().FullName ?? "<unknown>";

            var codes = entry.Value is IEnumerable list and not string
                ? string.Join(", ", list.Cast<object>().Select(c => $"{Convert.ToInt32(c)}"))
                : "<none>";

            var mine = target is not null && ReferenceEquals(target.GetType(), typeof(VoiceServerRpc));

            yield return $"handler={owner} codes=[{codes}]{(mine ? "  <-- OblivionVoice" : "")}";
        }
    }
}
