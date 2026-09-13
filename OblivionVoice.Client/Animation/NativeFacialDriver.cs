using OblivionMp.Sdk.Entities.Player;
using System.Reflection;

namespace OblivionVoice.Client.Animation;

/// <summary>
/// Samples our jaw-only curve animation on the dedicated head mesh. This does not change
/// the player's body AnimInstance, mesh, animation mode, or character-creation morphs.
/// Call only from the game update thread, after reading decoded audio levels elsewhere.
/// </summary>
internal sealed class NativeFacialDriver
{
    internal const string AnimationPath = "/Game/OblivionVoice/Animations/A_VoiceJaw.A_VoiceJaw";
    internal const string SkeletonPath = "/Game/Art/Character/Humanoid/SKEL_HumanoidHeadRig.SKEL_HumanoidHeadRig";
    internal const string JawCurve = "CTRL_expressions_jawOpen";
    internal const string Slot = "DefaultSlot";
    internal const float MaximumJawOpening = .45f;
    private const string HeadClass = "/Script/Altar.VHumanoidHeadComponent";
    private sealed record Lease(nint Pawn, nint Head, nint AnimInstance, nint Montage, float Length);
    private readonly Dictionary<string, Lease> owned = new(StringComparer.Ordinal);
    private readonly NativeFailureCooldown failures = new();
    private NativeFacialBridge? bridge;
    private int? thread;
    private static PropertyInfo? entityProperty, pawnProperty;
    public string? LastError { get; private set; }

    public bool Apply(ReadyMainCharacter player, float openness)
    {
        LastError = null;
        string? playerId = null;
        try
        {
            CheckThread();
            if (!player.IsValid) return false;
            string id = player.PlayerId.ToString();
            playerId = id;
            if (!float.IsFinite(openness) || openness <= .001f || player.IsDead || player.Hp <= 0 || player.InDialogue)
            {
                Release(player);
                return false;
            }
            if (failures.TryGet(id, Now, out string? failure)) { LastError = failure; return false; }
            bridge ??= new();
            var u = bridge;
            var current = Resolve(player);
            if (current is not { } face)
            {
                owned.Remove(id);
                if (LastError is { } reason) failures.Record(id, reason, Now);
                return false;
            }
            nint active = u.Call(face.Anim, "GetCurrentActiveMontage").Pointer();
            if (owned.TryGetValue(id, out var lease))
            {
                // Never follow old UObject pointers after a head replacement, respawn or world
                // transition. The active-montage pointer is obtained afresh from the current head.
                if (lease.Pawn != face.Pawn || lease.Head != face.Head || lease.AnimInstance != face.Anim || active != lease.Montage)
                {
                    owned.Remove(id);
                    lease = null;
                }
            }
            if (lease is null)
            {
                // NPC/game dialogue may be paused or blending out. Both checks are deliberate.
                // Starting a montage in the same slot would otherwise interrupt game dialogue.
                if (active != 0 || u.Call(face.Anim, "IsAnyMontagePlaying").Boolean()) return false;
                foreach (string function in new[] { "PlaySlotAnimationAsDynamicMontage", "Montage_Pause", "Montage_SetPosition", "Montage_Stop" })
                    u.Prepare(face.Anim, function);
                nint asset = FindAnimation(u);
                if (!Valid(u, asset)) return Fail(id, "Voice jaw animation asset is unavailable.");
                float length = (float)u.Call(asset, "GetPlayLength").Float("ReturnValue");
                if (length < .98f || length > 1.02f) throw new InvalidDataException("Voice jaw animation must be the one-second normalized curve asset.");
                byte[] slotName = u.Call(u.DefaultObject("/Script/Engine.Default__KismetStringLibrary"), "Conv_StringToName", ("InString", Slot)).Bytes();
                nint montage = u.Call(face.Anim, "PlaySlotAnimationAsDynamicMontage", ("Asset", asset), ("SlotNodeName", slotName),
                    ("BlendInTime", .10f), ("BlendOutTime", .10f), ("InPlayRate", 1f), ("LoopCount", 1),
                    ("BlendOutTriggerTime", -1f), ("InTimeToStartMontageAt", 0f)).Pointer();
                if (montage == 0) return Fail(id, "The current character head rejected the voice animation.");
                // A montage returned by Unreal must never be treated as a nullable wildcard.
                // All mutation calls below always pass this exact, nonzero owned montage.
                if (u.Call(face.Anim, "GetCurrentActiveMontage").Pointer() != montage)
                    throw new InvalidOperationException("The voice montage did not become the active head montage.");
                lease = new(face.Pawn, face.Head, face.Anim, montage, length);
                owned[id] = lease;
                u.Call(face.Anim, "Montage_Pause", ("Montage", montage));
            }
            float sample = SamplePosition(openness, lease.Length);
            u.Call(face.Anim, "Montage_SetPosition", ("Montage", lease.Montage), ("NewPosition", sample));
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            if (playerId is not null) failures.Record(playerId, error.Message, Now);
            // If creation succeeded but a later native contract failed, try to remove only the
            // montage still owned on the same freshly resolved head. Never stop a foreign one.
            if (thread == Environment.CurrentManagedThreadId) TryRelease(player);
            LastError = error.Message;
            return false;
        }
    }

    public void Release(ReadyMainCharacter player)
    {
        try { CheckThread(); ReleaseCore(player); }
        catch (Exception error) { LastError = error.Message; }
    }

    public void Forget(string playerId) { owned.Remove(playerId); failures.Forget(playerId); }
    public void ForgetAll() { owned.Clear(); failures.Clear(); }

    private static double Now => Environment.TickCount64 / 1000d;
    private bool Fail(string id, string reason)
    {
        LastError = reason;
        failures.Record(id, reason, Now);
        return false;
    }

    internal static float SamplePosition(float openness, float length)
    {
        if (!float.IsFinite(openness) || !float.IsFinite(length) || length < .98f || length > 1.02f)
            throw new ArgumentException("Voice jaw sampling requires a finite normalized value and a one-second asset.");
        return Math.Clamp(openness, 0, 1) * MaximumJawOpening * length;
    }

    private void TryRelease(ReadyMainCharacter player)
    {
        try { ReleaseCore(player); }
        catch { /* Keep the first useful error. Never retry against stale native objects. */ }
    }

    private void ReleaseCore(ReadyMainCharacter player)
    {
        if (!player.IsValid || bridge is not { } u) return;
        string id = player.PlayerId.ToString();
        if (!owned.TryGetValue(id, out var lease)) return;
        var current = Resolve(player);
        if (current is not { } face || face.Pawn != lease.Pawn || face.Head != lease.Head || face.Anim != lease.AnimInstance)
        { owned.Remove(id); return; }
        if (u.Call(face.Anim, "GetCurrentActiveMontage").Pointer() != lease.Montage)
        { owned.Remove(id); return; }
        // Blend the owned head slot back to the game's current pose. Do not zero/reset face
        // curves globally; the game may already be blending its own facial expression.
        u.Call(face.Anim, "Montage_Stop", ("InBlendOutTime", .10f), ("Montage", lease.Montage));
        owned.Remove(id);
    }

    private (nint Pawn, nint Head, nint Anim)? Resolve(ReadyMainCharacter player)
    {
        var u = bridge!;
        nint pawn = Pawn(player);
        if (!Valid(u, pawn)) { LastError = "Native player pawn is unavailable for mouth animation."; return null; }
        nint cls = u.DefaultObject(HeadClass);
        if (!Valid(u, cls)) { LastError = "Native humanoid head component class is unavailable."; return null; }
        nint head = u.Call(pawn, "GetComponentByClass", ("ComponentClass", cls)).Pointer();
        if (!Valid(u, head)) { LastError = "The player has no ready native humanoid head component."; return null; }
        nint anim = u.Call(head, "GetAnimInstance").Pointer();
        if (!Valid(u, anim)) { LastError = "The player's native head animation instance is not ready."; return null; }
        return (pawn, head, anim);
    }

    private static bool Valid(NativeFacialBridge u, nint value) => value != 0 &&
        u.Call(u.DefaultObject("/Script/Engine.Default__KismetSystemLibrary"), "IsValid", ("Object", value)).Boolean();

    private static nint FindAnimation(NativeFacialBridge u)
    {
        // Re-find on acquisition; do not retain an unrooted asset pointer across garbage collection.
        nint asset = u.DefaultObject(AnimationPath);
        if (asset != 0) return asset;
        nint system = u.DefaultObject("/Script/Engine.Default__KismetSystemLibrary");
        var path = u.Call(system, "MakeSoftObjectPath", ("PathString", AnimationPath)).Bytes();
        var reference = u.Call(system, "Conv_SoftObjPathToSoftObjRef", ("SoftObjectPath", path)).Bytes();
        return u.Call(system, "LoadAsset_Blocking", ("Asset", reference)).Pointer();
    }

    private void CheckThread()
    {
        int current = Environment.CurrentManagedThreadId;
        if (thread is { } existing && existing != current) throw new InvalidOperationException("Facial animation must run on the game update thread.");
        thread = current;
    }

    private static nint Pawn(ReadyMainCharacter player)
    {
        entityProperty ??= typeof(ReadyMainCharacter).GetProperty("Entity", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("SDK player entity contract is unavailable.");
        pawnProperty ??= entityProperty.PropertyType.GetProperty("Pawn", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new NotSupportedException("SDK pawn mapping contract is unavailable.");
        return (nint)(pawnProperty.GetValue(entityProperty.GetValue(player)) ?? nint.Zero);
    }
}
