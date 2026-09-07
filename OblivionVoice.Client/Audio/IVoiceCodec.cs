namespace OblivionVoice.Client.Audio;

public interface IVoiceCodec : IDisposable
{
    void Configure(int sampleRate, int channels, int frameMilliseconds, int bitrate, bool dtx, bool fec);
    byte[] Encode(ReadOnlySpan<short> pcm);
    short[] Decode(ReadOnlySpan<byte> encoded);
}
