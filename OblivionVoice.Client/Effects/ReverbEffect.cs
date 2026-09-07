namespace OblivionVoice.Client.Audio.Effects;

public sealed class ReverbEffect : IVoiceEffect
{

    private static readonly int[] CombTuning = [1116, 1188, 1277, 1356];
    private static readonly int[] AllPassTuning = [556, 441];
    private const int ReferenceSampleRate = 44100;

    private float[][] _combBuffers = [];
    private int[] _combIndices = [];
    private float[] _combFilterStore = [];

    private float[][] _allPassBuffers = [];
    private int[] _allPassIndices = [];

    private int _sampleRate;

    public bool Enabled { get; set; } = true;

    public float RoomSize { get; set; } = 0.85f;

    public float Damping { get; set; } = 0.25f;

    public float Wet { get; set; } = 0.35f;

    public float Dry { get; set; } = 0.9f;

    public void Prepare(int sampleRate)
    {
        _sampleRate = sampleRate;
        var scale = (float)sampleRate / ReferenceSampleRate;

        _combBuffers = new float[CombTuning.Length][];
        _combIndices = new int[CombTuning.Length];
        _combFilterStore = new float[CombTuning.Length];

        for (var i = 0; i < CombTuning.Length; i++)
            _combBuffers[i] = new float[Math.Max(1, (int)(CombTuning[i] * scale))];

        _allPassBuffers = new float[AllPassTuning.Length][];
        _allPassIndices = new int[AllPassTuning.Length];

        for (var i = 0; i < AllPassTuning.Length; i++)
            _allPassBuffers[i] = new float[Math.Max(1, (int)(AllPassTuning[i] * scale))];
    }

    public void Process(Span<float> buffer)
    {
        if (_sampleRate == 0 || _combBuffers.Length == 0) return;

        var roomSize = Math.Clamp(RoomSize, 0f, 0.98f);
        var damping = Math.Clamp(Damping, 0f, 1f);
        var wet = Math.Clamp(Wet, 0f, 1f);
        var dry = Math.Clamp(Dry, 0f, 1f);

        for (var s = 0; s < buffer.Length; s++)
        {
            var input = buffer[s];
            var accumulated = 0f;

            for (var c = 0; c < _combBuffers.Length; c++)
            {
                var line = _combBuffers[c];
                var index = _combIndices[c];
                var delayed = line[index];

                _combFilterStore[c] = (delayed * (1f - damping)) + (_combFilterStore[c] * damping);
                line[index] = input + (_combFilterStore[c] * roomSize);

                _combIndices[c] = index + 1 >= line.Length ? 0 : index + 1;
                accumulated += delayed;
            }

            accumulated /= _combBuffers.Length;

            for (var a = 0; a < _allPassBuffers.Length; a++)
            {
                var line = _allPassBuffers[a];
                var index = _allPassIndices[a];
                var delayed = line[index];

                var output = -accumulated + delayed;
                line[index] = accumulated + (delayed * 0.5f);

                _allPassIndices[a] = index + 1 >= line.Length ? 0 : index + 1;
                accumulated = output;
            }

            buffer[s] = (input * dry) + (accumulated * wet);
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _combBuffers.Length; i++)
        {
            Array.Clear(_combBuffers[i]);
            _combIndices[i] = 0;
            _combFilterStore[i] = 0f;
        }

        for (var i = 0; i < _allPassBuffers.Length; i++)
        {
            Array.Clear(_allPassBuffers[i]);
            _allPassIndices[i] = 0;
        }
    }

    public static ReverbEffect Cave() =>
        new() { RoomSize = 0.92f, Damping = 0.15f, Wet = 0.45f, Dry = 0.85f };

    public static ReverbEffect Ruin() =>
        new() { RoomSize = 0.88f, Damping = 0.3f, Wet = 0.35f, Dry = 0.9f };

    public static ReverbEffect Room() =>
        new() { RoomSize = 0.6f, Damping = 0.6f, Wet = 0.15f, Dry = 0.95f };
}
