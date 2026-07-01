namespace XboxLARP;

/// <summary>Polls one XInput slot, auto-detecting whichever is connected and recovering
/// gracefully on disconnect/reconnect - the poll loop must never crash or get stuck on a
/// missing controller.</summary>
public sealed class ControllerPoller
{
    private int _connectedSlot = -1;
    private XButton _previousHeld = XButton.None;

    public XButton CurrentHeld { get; private set; }
    public XButton PressedThisTick { get; private set; }
    public XButton ReleasedThisTick { get; private set; }
    public bool IsConnected => _connectedSlot >= 0;

    public bool Poll()
    {
        if (_connectedSlot < 0 && !TryFindController())
        {
            Reset();
            return false;
        }

        int rc = XInputNative.XInputGetStateEx(_connectedSlot, out var state);
        if (rc == XInputNative.ERROR_DEVICE_NOT_CONNECTED)
        {
            _connectedSlot = -1;
            Reset();
            return false;
        }
        if (rc != XInputNative.ERROR_SUCCESS)
            return false; // transient read error - keep previous state, retry next tick

        var held = (XButton)state.Gamepad.wButtons;
        PressedThisTick = held & ~_previousHeld;
        ReleasedThisTick = _previousHeld & ~held;
        CurrentHeld = held;
        _previousHeld = held;
        return true;
    }

    private bool TryFindController()
    {
        for (int i = 0; i < 4; i++)
        {
            if (XInputNative.XInputGetStateEx(i, out _) == XInputNative.ERROR_SUCCESS)
            {
                _connectedSlot = i;
                return true;
            }
        }
        return false;
    }

    private void Reset()
    {
        _previousHeld = XButton.None;
        CurrentHeld = XButton.None;
        PressedThisTick = XButton.None;
        ReleasedThisTick = XButton.None;
    }
}
