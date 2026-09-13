using System.Diagnostics;

namespace OblivionVoice.Client.Audio;

/// <summary>A small, thread-safe speech meter. It stores an envelope only, never audio.</summary>
public sealed class SpeechLevel
{
    public const double HoldSeconds = .06;
    public const double ExpirySeconds = .18;
    public const float OpenDb = -45;
    public const float CloseDb = -51;
    public const float FullDb = -18;
    private readonly object _gate = new();
    private float _level;
    private long _timestamp;
    private bool _hasSample, _open;

    public float Value => Read(Stopwatch.GetTimestamp());

    public void Publish(ReadOnlySpan<short> pcm) => Publish(pcm, Stopwatch.GetTimestamp());
    public void Publish(ReadOnlySpan<short> pcm, long timestamp)
    {
        if (pcm.Length == 0) { Clear(); return; }
        double squares = 0;
        foreach (var sample in pcm)
        {
            double normalized = sample / 32768d;
            squares += normalized * normalized;
        }
        PublishRms((float)Math.Sqrt(squares / pcm.Length), timestamp);
    }

    public void PublishRms(float rms, long timestamp)
    {
        lock (_gate)
        {
            if (!float.IsFinite(rms) || rms <= 0)
            {
                _level = 0; _open = false; _hasSample = false;
                return;
            }
            if (_hasSample && (timestamp < _timestamp || Stopwatch.GetElapsedTime(_timestamp, timestamp).TotalSeconds >= ExpirySeconds)) _open = false;
            var db = 20 * MathF.Log10(Math.Clamp(rms, 1e-9f, 1));
            if (db >= OpenDb) _open = true;
            else if (db <= CloseDb) _open = false;
            // A logarithmic range accommodates quiet and loud microphones. The curve
            // keeps small syllables subtle; rendering supplies attack/release smoothing.
            _level = _open ? MathF.Pow(Math.Clamp((db - CloseDb) / (FullDb - CloseDb), 0, 1), 1.25f) : 0;
            _timestamp = timestamp;
            _hasSample = true;
        }
    }

    public float Read(long timestamp)
    {
        lock (_gate)
        {
            if (!_hasSample || timestamp < _timestamp) return 0;
            var age = Stopwatch.GetElapsedTime(_timestamp, timestamp).TotalSeconds;
            if (age >= ExpirySeconds) return 0;
            if (age <= HoldSeconds) return _level;
            return _level * (float)((ExpirySeconds - age) / (ExpirySeconds - HoldSeconds));
        }
    }

    public void Clear()
    {
        lock (_gate) { _level = 0; _timestamp = 0; _hasSample = false; _open = false; }
    }
}
