namespace OblivionVoice.Client.Animation;

// Only native /Script classes and default objects have process-long lifetimes.
// Never retain dynamic pawns, meshes, montages or game assets here.
internal sealed class NativeScriptObjectCache
{
    readonly Dictionary<string, nint> objects = new(StringComparer.Ordinal);
    public nint Find(string path, Func<string, nint> find)
    {
        if (!path.StartsWith("/Script/", StringComparison.Ordinal)) return find(path);
        if (objects.TryGetValue(path, out var cached)) return cached;
        var value = find(path);
        if (value != 0) objects.Add(path, value);
        return value;
    }
}
