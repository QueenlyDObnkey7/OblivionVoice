using System.Reflection;

namespace OblivionVoice.Common;

public static class VoiceRpcOffsets
{

    public static readonly byte? PinnedOffset = 0;

    public static byte HostAssignedOffset { get; private set; }

    public static bool Pinned { get; private set; }

    public static byte TotalEventCount => ServerRpcManifest.TotalEventCount;

    public static byte CurrentOffset => ServerRpcManifest.Offset;

    public static bool ApplyPin()
    {
        HostAssignedOffset = ServerRpcManifest.Offset;

        if (PinnedOffset is not { } pinned || pinned == HostAssignedOffset)
            return false;

        ServerRpcManifest.Offset = pinned;
        Pinned = true;
        return true;
    }

    public static string Describe()
    {
        const int minServerRpcEvent = 150;

        var first = minServerRpcEvent + ServerRpcManifest.Offset;
        var last = first + Math.Max(ServerRpcManifest.TotalEventCount - 1, 0);

        return $"OblivionVoice.Common offset={ServerRpcManifest.Offset} " +
               $"hostAssigned={HostAssignedOffset} pinned={Pinned} " +
               $"events={ServerRpcManifest.TotalEventCount} wireCodes={first}..{last}";
    }

    public static IReadOnlyList<string> SurveyManifestAssemblies()
    {
        var results = new List<string>();
        var runningOffset = 0;

        var assemblies = AppDomain.CurrentDomain
            .GetAssemblies()
            .OrderBy(a => a.FullName)
            .ToList();

        foreach (var assembly in assemblies)
        {
            Type? manifest;
            try
            {

                manifest = assembly
                    .GetTypes()
                    .FirstOrDefault(t => t.Name == "ServerRpcManifest" && t is { IsAbstract: true, IsSealed: true });
            }
            catch (ReflectionTypeLoadException ex)
            {
                manifest = ex.Types.FirstOrDefault(t =>
                    t is { Name: "ServerRpcManifest", IsAbstract: true, IsSealed: true });
            }
            catch
            {
                continue;
            }

            if (manifest is null)
                continue;

            var countField = manifest.GetField("TotalEventCount", BindingFlags.Public | BindingFlags.Static);
            var offsetProp = manifest.GetProperty("Offset", BindingFlags.Public | BindingFlags.Static);

            if (countField is null || offsetProp is null)
            {
                results.Add($"{assembly.GetName().Name}: ServerRpcManifest present but members missing " +
                            "(generator version mismatch)");
                continue;
            }

            var count = Convert.ToInt32(countField.GetValue(null));
            var offset = Convert.ToInt32(offsetProp.GetValue(null));

            results.Add($"{assembly.GetName().Name}: events={count} offset={offset} " +
                        $"(expected {runningOffset} from scan order)" +
                        (offset == runningOffset ? "" : " <-- MISMATCH / pinned"));

            runningOffset += count;
        }

        if (results.Count == 0)
            results.Add("No ServerRpcManifest assemblies found in this AppDomain.");

        return results;
    }
}
