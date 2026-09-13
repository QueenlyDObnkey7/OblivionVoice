using System.Diagnostics;
using OblivionVoice.Client.Audio;
using OblivionVoice.Client.Audio.Effects;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
long stamp = Stopwatch.GetTimestamp();
long Later(double seconds) => stamp + (long)Math.Ceiling(seconds * Stopwatch.Frequency);
float Rms(float db) => MathF.Pow(10, db / 20);
var meter = new SpeechLevel();

Check(meter.Value == 0, "Meter starts closed");
meter.PublishRms(Rms(-48), stamp);
Check(meter.Read(stamp) == 0, "Background noise below opening threshold does not animate");
meter.PublishRms(Rms(-30), stamp);
float talking = meter.Read(stamp);
Check(talking > 0 && talking < 1, "Ordinary speech yields a bounded partial mouth opening");
meter.PublishRms(Rms(-48), stamp);
Check(meter.Read(stamp) > 0, "Hysteresis preserves softer syllables after speech opens the gate");
meter.PublishRms(Rms(-52), stamp);
Check(meter.Read(stamp) == 0, "Low noise closes the gate");
meter.PublishRms(Rms(-48), stamp);
Check(meter.Read(stamp) == 0, "Noise cannot reopen a closed gate");
float previous = 0;
foreach (float db in new[] { -44f, -35f, -25f, -18f })
{
    meter.Clear(); meter.PublishRms(Rms(db), stamp);
    float current = meter.Read(stamp);
    Check(current > previous && current <= 1, "Increasing speech loudness increases bounded mouth opening");
    previous = current;
}
meter.PublishRms(1, stamp);
Check(meter.Read(stamp) == 1, "Clipped full-scale audio remains bounded");
Check(meter.Read(Later(.03)) == 1, "The short envelope hold spans normal audio callbacks");
Check(meter.Read(Later(.12)) is > 0 and < 1, "Missing callbacks decay the envelope");
Check(meter.Read(Later(.181)) == 0, "Stale speech expires");
Check(meter.Read(stamp - 1) == 0, "A backwards clock read cannot reveal stale speech");
meter.PublishRms(Rms(-48), Later(1));
Check(meter.Read(Later(1)) == 0, "Expired speech cannot hold the noise gate open");
foreach (float rms in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1, 0 })
{
    meter.PublishRms(1, stamp); meter.PublishRms(rms, stamp);
    Check(meter.Read(stamp) == 0, "Invalid/nonpositive RMS clears the meter");
}
meter.Publish([short.MinValue, short.MaxValue], stamp);
Check(meter.Read(stamp) == 1, "Signed 16-bit extremes cannot overflow the RMS sum");
meter.Publish(new short[960], stamp);
Check(meter.Read(stamp) == 0, "Real digital silence closes the meter");
meter.Publish([short.MaxValue], stamp); meter.Publish(ReadOnlySpan<short>.Empty, stamp);
Check(meter.Read(stamp) == 0, "Empty PCM clears speech");
meter.Publish([short.MaxValue], stamp); meter.Clear();
Check(meter.Read(stamp) == 0, "Explicit lifecycle reset immediately clears speech");

const int samples = 960;
var tone = Enumerable.Range(0, samples).Select(i => (short)(8000 * Math.Sin(i * 2 * Math.PI * 220 / 48000))).ToArray();
using (var encoder = new ConcentusOpusCodec())
using (var pipeline = new SpeakerVoicePipeline(48000, 20) { TargetFrames = 3 })
{
    encoder.Configure(48000, 1, 20, 32000, false, false);
    var frame = new float[samples];
    pipeline.Enqueue(100, encoder.Encode(tone));
    Check(pipeline.CurrentSpeechLevel == 0, "Received packets do not animate before playout");
    pipeline.Read(frame);
    Check(pipeline.CurrentSpeechLevel == 0 && frame.All(v => v == 0) && pipeline.ConsumedFrames == 0, "Jitter priming remains silent and does not animate");
    pipeline.Enqueue(101, encoder.Encode(tone)); pipeline.Enqueue(102, encoder.Encode(tone));
    pipeline.Read(frame);
    Check(frame.Any(v => Math.Abs(v) > .001f), "Real Opus audio decoded for playout");
    Check(pipeline.CurrentSpeechLevel is > 0 and <= 1, "Mouth signal starts when the jitter buffer actually plays speech");
    Check(pipeline.ConsumedFrames == 1, "Metering did not add another decode");
    pipeline.Read(frame); pipeline.Read(frame);
    pipeline.Read(frame);
    Check(pipeline.ConcealedFrames == 1, "Existing packet-loss concealment still runs");
    Check(pipeline.CurrentSpeechLevel == 0, "Packet-loss concealment does not invent talking motion");
    pipeline.Dispose();
    Check(pipeline.CurrentSpeechLevel == 0, "Disposed pipeline cannot leave the mouth open");
}
using (var encoder = new ConcentusOpusCodec())
using (var pipeline = new SpeakerVoicePipeline(48000, 20) { TargetFrames = 1 })
{
    encoder.Configure(48000, 1, 20, 32000, false, false);
    pipeline.Effects.Add(new ReplaceSamples(0));
    pipeline.Enqueue(1, encoder.Encode(tone));
    var output = new float[samples]; pipeline.Read(output);
    Check(output.All(v => v == 0) && pipeline.CurrentSpeechLevel > 0, "Effects that quiet playback do not alter the underlying speech envelope");
}
using (var encoder = new ConcentusOpusCodec())
using (var pipeline = new SpeakerVoicePipeline(48000, 20) { TargetFrames = 1 })
{
    encoder.Configure(48000, 1, 20, 32000, false, false);
    pipeline.Effects.Add(new ReplaceSamples(.8f));
    pipeline.Enqueue(1, encoder.Encode(new short[samples]));
    var output = new float[samples]; pipeline.Read(output);
    Check(output.All(v => v == .8f) && pipeline.CurrentSpeechLevel == 0, "An effect/reverb tail cannot turn silent decoded speech into mouth motion");
}

var concurrent = new SpeechLevel();
Parallel.For(0, 1000, i =>
{
    if (i % 7 == 0) concurrent.Clear();
    else concurrent.PublishRms((i % 100) / 100f, Stopwatch.GetTimestamp());
    float value = concurrent.Value;
    if (!float.IsFinite(value) || value is < 0 or > 1) throw new Exception("Concurrent meter read was not finite and bounded");
});
Check(true, "Concurrent audio writes and game-thread reads remain finite and bounded");
Console.WriteLine($"Mouth audio: {checks} checks passed, including real Opus and jitter-buffer playout.");

sealed class ReplaceSamples(float value) : IVoiceEffect
{
    public bool Enabled { get; set; } = true;
    public void Process(Span<float> buffer) => buffer.Fill(value);
    public void Prepare(int sampleRate) { }
    public void Reset() { }
}
