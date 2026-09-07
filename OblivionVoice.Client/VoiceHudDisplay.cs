using Microsoft.Extensions.Logging;
using OblivionMp.Sdk;
using OblivionMpCSharpMod;

namespace OblivionVoice.Client;

public sealed class VoiceHudDisplay(ILogger logger)
{
    private string _lastText = string.Empty;
    private bool _visible;
    private bool _failed;

    public void SetText(string text)
    {
        if (_failed || text == _lastText) return;

        try
        {
            SDK.GameMessage.ShowInfoMessage(text);
            _lastText = text;
            _visible = true;
        }
        catch (Exception ex)
        {

            _failed = true;
            logger.LogWarning(ex, "OblivionVoice HUD unavailable. Voice is unaffected.");
            Console.WriteLine("[OblivionVoice] HUD unavailable; voice is unaffected.");
        }
    }

    public void Hide()
    {
        if (_failed || !_visible) return;

        try
        {
            SDK.GameMessage.HideInfoMessage();
            _visible = false;
            _lastText = string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to hide the OblivionVoice HUD message.");
        }
    }

    public void Notify(string text, float seconds = 3f)
    {
        if (_failed) return;

        try
        {
            SDK.GameMessage.ShowMessage(text, MessagePosition.Center, seconds);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to show an OblivionVoice notification.");
        }
    }

    public void Chat(string text)
    {
        if (_failed) return;

        try
        {

            SDK.Chat.ShowLocalMessage(text, new OblivionMpCSharpMod.Values.Color(1f, 1f, 1f, 1f));        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to write an OblivionVoice chat line.");
        }
    }
}
