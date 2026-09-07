using Concentus;
using Concentus.Enums;

namespace OblivionVoice.Client.Audio;

public sealed class ConcentusOpusCodec : IVoiceCodec
{
    private IOpusEncoder? _encoder;
    private IOpusDecoder? _decoder;
    private int _frameSize;
    private int _channels;

    public void Configure(int sampleRate, int channels, int frameMilliseconds, int bitrate, bool dtx, bool fec)
    {
        Dispose();
        _channels = channels;
        _frameSize = sampleRate * frameMilliseconds / 1000;
        _encoder = OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);
        _encoder.Bitrate = bitrate;
        _encoder.UseDTX = dtx;
        _encoder.UseInbandFEC = fec;
    }

    public byte[] Encode(ReadOnlySpan<short> pcm)
    {
        if (_encoder == null) throw new InvalidOperationException("Codec is not configured.");
        if (pcm.Length < _frameSize * _channels)
            throw new ArgumentException("PCM frame is shorter than the configured Opus frame.", nameof(pcm));

        var input = pcm[..(_frameSize * _channels)].ToArray();
        var output = new byte[4000];
        var count = _encoder.Encode(input, _frameSize, output, output.Length);
        if (count <= 0) return Array.Empty<byte>();
        Array.Resize(ref output, count);
        return output;
    }

    public short[] Decode(ReadOnlySpan<byte> encoded)
    {
        if (_decoder == null) throw new InvalidOperationException("Codec is not configured.");
        var output = new short[_frameSize * _channels * 6];
        var samplesPerChannel = _decoder.Decode(encoded, output.AsSpan(), _frameSize * 6);
        if (samplesPerChannel <= 0) return Array.Empty<short>();
        Array.Resize(ref output, samplesPerChannel * _channels);
        return output;
    }

    public void Dispose()
    {
        _encoder?.Dispose();
        _decoder?.Dispose();
        _encoder = null;
        _decoder = null;
    }
}
