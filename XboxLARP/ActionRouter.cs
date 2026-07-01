using MonitorTopology;

namespace XboxLARP;

/// <summary>Evaluates chord bindings against each poll tick. A binding fires when all of its
/// Modifiers are currently held AND its Trigger button transitions to pressed this tick -
/// edge-triggered on the trigger, so holding a chord doesn't repeat-fire, but re-tapping the
/// trigger while the modifier stays held (e.g. Guide+X for Alt-Tab cycling) fires again.</summary>
public sealed class ActionRouter
{
    private readonly ControllerConfig _config;
    private readonly ILogSink _log;
    private readonly Action<ActionType, string> _onAction;
    private readonly List<(Binding binding, XButton modifierMask, XButton triggerMask, ActionType action)> _compiled;
    private readonly Dictionary<string, DateTime> _lastFired = new();

    public ActionRouter(ControllerConfig config, ILogSink log, Action<ActionType, string> onAction)
    {
        _config = config;
        _log = log;
        _onAction = onAction;
        _compiled = [];

        foreach (var b in config.Bindings)
        {
            if (!TryParseButtons(b.Modifiers, out var modMask))
            {
                _log.Error($"Binding '{b.Name}': invalid modifier button name(s) in [{string.Join(",", b.Modifiers)}] - skipped");
                continue;
            }
            if (!Enum.TryParse<XButton>(b.Trigger, out var trigMask) || trigMask == XButton.None)
            {
                _log.Error($"Binding '{b.Name}': invalid trigger button '{b.Trigger}' - skipped");
                continue;
            }
            if (!Enum.TryParse<ActionType>(b.Action, out var action))
            {
                _log.Error($"Binding '{b.Name}': invalid action '{b.Action}' - skipped");
                continue;
            }
            _compiled.Add((b, modMask, trigMask, action));
        }
    }

    private static bool TryParseButtons(IEnumerable<string> names, out XButton mask)
    {
        mask = XButton.None;
        foreach (var name in names)
        {
            if (!Enum.TryParse<XButton>(name, out var b)) return false;
            mask |= b;
        }
        return true;
    }

    public void Process(XButton currentHeld, XButton pressedThisTick)
    {
        var now = DateTime.UtcNow;
        foreach (var (binding, modMask, trigMask, action) in _compiled)
        {
            bool modifiersHeld = (currentHeld & modMask) == modMask;
            bool triggerPressed = (pressedThisTick & trigMask) == trigMask;
            if (!modifiersHeld || !triggerPressed)
                continue;

            if (_lastFired.TryGetValue(binding.Name, out var last) && (now - last).TotalMilliseconds < _config.CooldownMs)
                continue;

            _lastFired[binding.Name] = now;
            _log.Info($"Chord fired: {binding.Name} -> {action}");
            _onAction(action, binding.Name);
        }
    }
}
