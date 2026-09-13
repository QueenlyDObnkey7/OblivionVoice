namespace OblivionVoice.Client.Animation;

// Per actor: unsupported heads/assets must not trigger a blocking native load every audio frame.
internal sealed class NativeFailureCooldown
{
    private readonly Dictionary<string, (double Until, string Reason)> failures = new(StringComparer.Ordinal);
    internal const double Seconds = 2;
    internal void Record(string id, string reason, double now) => failures[id] = (now + Seconds, reason);
    internal bool TryGet(string id, double now, out string? reason)
    {
        if (failures.TryGetValue(id, out var failure))
        {
            if (now < failure.Until) { reason = failure.Reason; return true; }
            failures.Remove(id);
        }
        reason = null;
        return false;
    }
    internal void Forget(string id) => failures.Remove(id);
    internal void Clear() => failures.Clear();
}
