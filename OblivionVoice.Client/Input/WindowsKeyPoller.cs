using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace OblivionVoice.Client.Input;

public sealed class WindowsKeyPoller(ILogger logger) : IDisposable
{
    private CancellationTokenSource? _releaseCts;
    private Task? _releaseTask;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public void WatchForRelease(
        string keyName,
        Action released,
        bool debugLogKeyEvents = false)
    {
        Stop();

        if (!TryResolveVirtualKey(keyName, out var vk))
        {
            logger.LogError(
                "Unknown OblivionVoice TransmitKey '{Key}'. Cannot detect release.",
                keyName);
            return;
        }

        _releaseCts = new CancellationTokenSource();
        var token = _releaseCts.Token;

        _releaseTask = Task.Run(async () =>
        {
            try
            {

                await Task.Delay(5, token);

                var sawDown = false;
                var startedAt = DateTimeOffset.UtcNow;

                while (!token.IsCancellationRequested)
                {
                    var down = (GetAsyncKeyState(vk) & 0x8000) != 0;

                    if (down)
                        sawDown = true;

                    if (sawDown && !down)
                    {
                        if (debugLogKeyEvents)
                            logger.LogInformation(
                                "[VoiceDebug] Windows key release detected key={Key}",
                                keyName);

                        released();
                        return;
                    }

                    if (!sawDown &&
                        DateTimeOffset.UtcNow - startedAt > TimeSpan.FromMilliseconds(500))
                    {
                        logger.LogWarning(
                            "[VoiceDebug] Could not observe Windows down-state for PTT key {Key}; ending transmission as a safety fallback.",
                            keyName);

                        released();
                        return;
                    }

                    await Task.Delay(8, token);
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PTT release watcher failed for key {Key}.", keyName);

                released();
            }
        }, token);
    }

    public void Stop()
    {
        try { _releaseCts?.Cancel(); } catch { }
        _releaseCts?.Dispose();
        _releaseCts = null;
        _releaseTask = null;
    }

    public static bool TryResolveVirtualKey(string text, out int key)
    {
        key = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();

        if (value.Length == 1)
        {
            var ch = char.ToUpperInvariant(value[0]);
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
            {
                key = ch;
                return true;
            }
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["CapsLock"] = 0x14,
            ["LShift"] = 0xA0,
            ["RShift"] = 0xA1,
            ["Shift"] = 0x10,
            ["LControl"] = 0xA2,
            ["RControl"] = 0xA3,
            ["Control"] = 0x11,
            ["LCtrl"] = 0xA2,
            ["RCtrl"] = 0xA3,
            ["LAlt"] = 0xA4,
            ["RAlt"] = 0xA5,
            ["Space"] = 0x20,
            ["Mouse4"] = 0x05,
            ["Mouse5"] = 0x06
        };

        if (map.TryGetValue(value, out key))
            return true;

        if (value.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value[1..], out var f) &&
            f is >= 1 and <= 24)
        {
            key = 0x70 + f - 1;
            return true;
        }

        return false;
    }

    public void Dispose() => Stop();
}
