using Microsoft.Extensions.Logging;
using OblivionUI;
using ReadyM.Modloader.Mods;
namespace OblivionVoice.Client;

// UI performs fresh camera occlusion and sneak checks each rendered frame, even while speech lingers.
public sealed class VoiceSpeakerMarkerSystem(VoiceRuntime runtime,ILogger logger) : ModSystemBase
{
    private float _elapsed;
    protected override void OnUpdate(UpdateTick tick)
    {
        _elapsed+=tick.deltaTime;
        if(_elapsed<.05f)return;
        _elapsed=0;
        try {
            if(!runtime.IsVoiceConnected || !runtime.Settings.Enabled) {SpeakerIndicators.Clear();return;}
            SpeakerIndicators.Publish(runtime.IsSpeaking,runtime.ActiveSpeakers);
        } catch(Exception ex) {SpeakerIndicators.Clear();logger.LogDebug(ex,"Voice speaking indicators unavailable.");}
    }
}
