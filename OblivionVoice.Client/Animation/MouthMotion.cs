namespace OblivionVoice.Client.Animation;

/// <summary>A speech envelope with a quick opening and a softer close, evaluated on the game thread.</summary>
internal sealed class MouthMotion
{
    public float Amount { get; private set; }
    public float Update(float target, float elapsedSeconds)
    {
        target=float.IsFinite(target)?Math.Clamp(target,0,1):0;
        if(!float.IsFinite(elapsedSeconds)||elapsedSeconds<=0)return Amount;
        var seconds=Math.Min(elapsedSeconds,.25f);
        var response=target>Amount?.045f:.095f;
        Amount += (target-Amount)*(1-MathF.Exp(-seconds/response));
        if(target==0&&Amount<.001f)Amount=0;
        return Amount;
    }
    public void Reset()=>Amount=0;
}
