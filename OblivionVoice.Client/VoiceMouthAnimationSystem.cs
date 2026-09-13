using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMp.Sdk.Entities.Player;
using OblivionVoice.Client.Animation;
using ReadyM.Modloader.Mods;

namespace OblivionVoice.Client;

public sealed class VoiceMouthAnimationSystem : ModSystemBase
{
    readonly VoiceRuntime runtime;
    readonly ILogger logger;
    readonly NativeFacialDriver driver=new();
    readonly MouthAnimationController<ReadyMainCharacter> controller;
    float elapsed;
    string? lastError;

    public VoiceMouthAnimationSystem(VoiceRuntime runtime,ILogger logger)
    {
        this.runtime=runtime;this.logger=logger;
        controller=new((player,amount)=>driver.Apply(player,amount),driver.Release,driver.Forget);
    }

    protected override void OnUpdate(UpdateTick tick)
    {
        if(!float.IsFinite(tick.deltaTime)||tick.deltaTime<=0)return;
        elapsed+=tick.deltaTime;if(elapsed<1f/30)return;
        var step=Math.Min(elapsed,.25f);elapsed=0;
        var actors=new List<MouthActor<ReadyMainCharacter>>();
        try
        {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            if(SDK.Sync.LocalPlayer is {IsValid:true} local)Add(local,runtime.LocalSpeechLevel);
            foreach(var player in SDK.Sync.AllPlayers)
            {
                if(!player.IsValid)continue;
                var id=player.PlayerId.ToString();
                // The local capture meter wins over a loopback playback channel.
                if(ids.Contains(id))continue;
                Add(player,runtime.GetSpeakerSpeechLevel(id));
            }
            var enabled=runtime.IsVoiceConnected&&runtime.Settings.Enabled&&runtime.Preferences.MouthAnimationEnabled;
            controller.Update(actors,enabled,runtime.Preferences.MouthAnimationStrength,step);
            if(driver.LastError is {Length:>0} error&&error!=lastError)
            {
                lastError=error;logger.LogWarning("[VoiceMouth] Facial animation unavailable: {Reason}",error);
            }
            void Add(ReadyMainCharacter player,float level)
            {
                var id=player.PlayerId.ToString();if(!ids.Add(id))return;
                actors.Add(new(id,player,level,!player.IsDead&&player.Hp>0&&!player.InDialogue));
            }
        }
        catch(Exception error)
        {
            foreach(var actor in actors)try{driver.Release(actor.Actor);}catch{ /* A disappearing pawn cannot be touched again. */ }
            controller.ForgetAll();driver.ForgetAll();
            if(lastError!=error.Message){lastError=error.Message;logger.LogWarning("[VoiceMouth] Facial update unavailable: {Reason}",error.Message);}
        }
    }
}
