using System.Reflection;
using Microsoft.Extensions.Logging;

namespace OblivionVoice.Client;

public sealed class NativeHudTextBridge(ILogger logger)
{
    private const string ModAssemblyName = "OblivionMpCSharpMod";
    private const string DiTypeName = "OblivionMpCSharpMod.DI";
    private const string WidgetPropertyName = "PingIndicatorWidget";

    private object? _widget;
    private MethodInfo? _setInfoText;
    private MethodInfo? _showInfoText;
    private MethodInfo? _hideInfoText;

    private bool _resolved;
    private bool _unavailable;
    private string _lastText = string.Empty;

    public bool Available => _resolved && !_unavailable;

    public void SetText(string text)
    {
        if (!EnsureResolved()) return;
        if (text == _lastText) return;

        try
        {
            _setInfoText!.Invoke(_widget, [text]);
            _showInfoText!.Invoke(_widget, null);
            _lastText = text;
        }
        catch (Exception ex)
        {
            Disable("writing HUD text failed", ex);
        }
    }

    public void Hide()
    {
        if (!EnsureResolved()) return;

        try
        {
            _hideInfoText!.Invoke(_widget, null);
            _lastText = string.Empty;
        }
        catch (Exception ex)
        {
            Disable("hiding HUD text failed", ex);
        }
    }

    private bool EnsureResolved()
    {
        if (_unavailable) return false;
        if (_resolved) return true;

        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, ModAssemblyName, StringComparison.Ordinal));

            if (assembly is null)
            {
                Disable($"{ModAssemblyName} is not loaded", null);
                return false;
            }

            var diType = assembly.GetType(DiTypeName, throwOnError: false);
            if (diType is null)
            {
                Disable($"{DiTypeName} not found", null);
                return false;
            }

            if (!TryGetWidget(diType, out _widget) || _widget is null)
            {
                Disable($"could not read {WidgetPropertyName} from {DiTypeName}", null);
                return false;
            }

            var widgetType = _widget.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            _setInfoText = widgetType.GetMethod("SetInfoText", flags, null, [typeof(string)], null);
            _showInfoText = widgetType.GetMethod("ShowInfoText", flags, null, Type.EmptyTypes, null);
            _hideInfoText = widgetType.GetMethod("HideInfoText", flags, null, Type.EmptyTypes, null);

            if (_setInfoText is null || _showInfoText is null || _hideInfoText is null)
            {
                Disable($"{widgetType.Name} does not expose the expected info-text methods", null);
                return false;
            }

            _resolved = true;
            logger.LogInformation("OblivionVoice HUD bridge bound to {Widget}.", widgetType.FullName);
            Console.WriteLine($"[OblivionVoice] HUD text bridge bound to {widgetType.Name}.");
            return true;
        }
        catch (Exception ex)
        {
            Disable("resolving the HUD widget threw", ex);
            return false;
        }
    }

    private static bool TryGetWidget(Type diType, out object? widget)
    {
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var staticProperty = diType.GetProperty(WidgetPropertyName, staticFlags);
        if (staticProperty?.GetValue(null) is { } fromStaticProperty)
        {
            widget = fromStaticProperty;
            return true;
        }

        var staticField = diType.GetField(WidgetPropertyName, staticFlags);
        if (staticField?.GetValue(null) is { } fromStaticField)
        {
            widget = fromStaticField;
            return true;
        }

        var instanceHolder = diType
            .GetFields(staticFlags)
            .FirstOrDefault(f => f.FieldType == diType)?
            .GetValue(null);

        if (instanceHolder is not null)
        {
            var instanceProperty = diType.GetProperty(WidgetPropertyName, instanceFlags);
            if (instanceProperty?.GetValue(instanceHolder) is { } fromInstance)
            {
                widget = fromInstance;
                return true;
            }
        }

        widget = null;
        return false;
    }

    private void Disable(string reason, Exception? ex)
    {
        _unavailable = true;
        _widget = null;

        if (ex is null)
            logger.LogWarning("OblivionVoice HUD text unavailable: {Reason}. Voice is unaffected.", reason);
        else
            logger.LogWarning(ex, "OblivionVoice HUD text unavailable: {Reason}. Voice is unaffected.", reason);

        Console.WriteLine($"[OblivionVoice] HUD text unavailable: {reason}. Voice is unaffected.");
    }
}
