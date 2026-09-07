using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using ReadyM.Sdk.Common;
using ReadyM.Sdk.Common.Input;

namespace OblivionVoice.Client.Input;

public sealed class ReadyMKeyBindings(ILogger logger)
{
    private bool _registered;

    public void Register(VoiceClientSettings settings, VoiceRuntime runtime)
    {
        if (_registered) return;
        _registered = true;

        RegisterOne(
            settings.TransmitKey,
            runtime.OnTransmitKeyPressed,
            "push-to-talk",
            settings.DebugEnabled && settings.DebugLogKeyEvents);

        RegisterOne(
            settings.CycleRangeKey,
            runtime.CycleMode,
            "cycle voice range",
            settings.DebugEnabled && settings.DebugLogKeyEvents);

        RegisterOne(
            settings.ToggleMuteKey,
            runtime.ToggleMute,
            "toggle voice mute",
            settings.DebugEnabled && settings.DebugLogKeyEvents);
    }

    private void RegisterOne(
        string keyText,
        Action callback,
        string description,
        bool debugLogKeyEvents)
    {
        if (!Enum.TryParse<Key>(keyText, true, out var key))
        {
            logger.LogWarning(
                "Could not register {Description}: ReadyM Key enum has no value '{Key}'.",
                description,
                keyText);
            return;
        }

        SDK.Input.RegisterKeyBind(key, () =>
        {
            var canApply = SDK.Input.CanApplyInput();

            if (debugLogKeyEvents)
            {
                logger.LogInformation(
                    "[VoiceDebug] ReadyM key press key={Key} action={Action} CanApplyInput={CanApply}",
                    key,
                    description,
                    canApply);

                Console.WriteLine(
                    $"[VoiceDebug] ReadyM key press key={key} action={description} CanApplyInput={canApply}");
            }

            if (canApply)
                callback();
        });

        logger.LogInformation(
            "Registered OblivionVoice ReadyM key {Key} for {Description}.",
            key,
            description);

        Console.WriteLine(
            $"[OblivionVoice] Registered ReadyM key {key} for {description}.");
    }
}
