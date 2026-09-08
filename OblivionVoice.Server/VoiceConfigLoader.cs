using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace OblivionVoice.Server;

public sealed class VoiceConfigLoader(ILogger logger)
{
    public VoiceConfig Current { get; private set; } = new();

    public VoiceConfig Load()
    {
        var modDirectory = Path.GetDirectoryName(typeof(VoiceConfigLoader).Assembly.Location)
                           ?? AppContext.BaseDirectory;
        var path = Path.Combine(modDirectory, "voiceconfig.json");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        if (!File.Exists(path))
        {
            Current = new VoiceConfig();
            File.WriteAllText(path, JsonSerializer.Serialize(Current, options));
            logger.LogWarning("OblivionVoice created a default config at {Path}. Set Network.AdvertisedHost before public use.", path);
        }
        else
        {
            Current = JsonSerializer.Deserialize<VoiceConfig>(File.ReadAllText(path), options)
                      ?? throw new InvalidOperationException("voiceconfig.json deserialized to null.");
        }

        Current.Validate();

        logger.LogInformation("OblivionVoice config loaded: Enabled={Enabled} Voice={Host}:{Port} Normal={Range}m",
            Current.Enabled, Current.Network.AdvertisedHost, Current.Network.Port, Current.Proximity.NormalMeters);

        return Current;
    }
}
