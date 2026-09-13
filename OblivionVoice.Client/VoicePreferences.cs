using System.Text.Json;
using OblivionVoice.Common;
namespace OblivionVoice.Client;

// User preferences only: never persist network/session credentials here.
public sealed record VoicePreferences
{
    public string InputDeviceId { get; init; } = "";
    public string? TransmitKey {get;init;}
    public float InputGainDb { get; init; }
    public TransmitMode? TransmitMode { get; init; }
    public bool MouthAnimationEnabled { get; init; } = true;
    public float MouthAnimationStrength { get; init; } = .65f;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OblivionVoice","settings.json");
    public void Validate()
    {
        if(TransmitKey is not null && (string.IsNullOrWhiteSpace(TransmitKey)||TransmitKey.Length>32))throw new ArgumentException("Invalid voice key.");
        if(InputDeviceId is null || InputDeviceId.Length>2048 || !float.IsFinite(InputGainDb) || InputGainDb is < -24 or > 24 || (TransmitMode.HasValue && !Enum.IsDefined(TransmitMode.Value))) throw new ArgumentException("Invalid microphone preferences.");
        if(!float.IsFinite(MouthAnimationStrength)||MouthAnimationStrength is <0 or >1)throw new ArgumentException("Mouth animation strength must be between 0 and 100 percent.");
    }
    public static VoicePreferences Load()
    {
        if(!File.Exists(FilePath)) return new();
        var value=JsonSerializer.Deserialize<VoicePreferences>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("Empty voice settings.");
        value.Validate();return value;
    }
    public void Save()
    {
        Validate();Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp=FilePath+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,FilePath,true);
    }
    public void ApplyGain(Span<short> samples)
    {
        if(InputGainDb==0)return;
        var gain=MathF.Pow(10,InputGainDb/20);
        for(int i=0;i<samples.Length;i++)samples[i]=(short)Math.Clamp(MathF.Round(samples[i]*gain),short.MinValue,short.MaxValue);
    }
}
