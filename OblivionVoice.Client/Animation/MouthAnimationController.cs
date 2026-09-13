namespace OblivionVoice.Client.Animation;

internal readonly record struct MouthActor<T>(string Id,T Actor,float Level,bool CanAnimate);

// This controller owns only voice poses. Game objects are supplied fresh by the
// caller every update; vanished actors are forgotten rather than dereferenced.
internal sealed class MouthAnimationController<T>(Action<T,float> apply,Action<T> release,Action<string> forget)
{
    readonly Dictionary<string,MouthMotion> motions=new(StringComparer.Ordinal);
    readonly HashSet<string> seen = new(StringComparer.Ordinal);
    readonly List<string> expired = new();
    public int Count=>motions.Count;

    public void Update(IEnumerable<MouthActor<T>> actors,bool enabled,float strength,float elapsed)
    {
        enabled &= float.IsFinite(strength)&&strength>0;
        strength=float.IsFinite(strength)?Math.Clamp(strength,0,1):0;
        seen.Clear(); expired.Clear();
        foreach(var actor in actors)
        {
            if(!seen.Add(actor.Id))continue;
            motions.TryGetValue(actor.Id,out var motion);
            if(!enabled||!actor.CanAnimate)
            {
                if(motion is not null){release(actor.Actor);motions.Remove(actor.Id);}
                continue;
            }
            float target=float.IsFinite(actor.Level)?Math.Clamp(actor.Level,0,1)*strength:0;
            if(motion is null)
            {
                if(target==0)continue;
                motions.Add(actor.Id,motion=new());
            }
            float amount=motion.Update(target,elapsed);
            if(amount==0){release(actor.Actor);motions.Remove(actor.Id);}
            else apply(actor.Actor,amount);
        }
        foreach(var id in motions.Keys) if(!seen.Contains(id)) expired.Add(id);
        foreach(var id in expired)
        {
            forget(id);motions.Remove(id);
        }
    }
    public void ForgetAll(){foreach(var id in motions.Keys)forget(id);motions.Clear();}
}
