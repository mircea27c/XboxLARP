using SDL2;

namespace XboxLARP;

/// <summary>
/// Polls a controller via SDL2's GameController API instead of raw XInput. SDL2 is the
/// open-source, decade-hardened input library most games/emulators (RetroArch, Dolphin) and
/// even Steam's own controller backend are built on - critically, it exposes the Guide button
/// as a first-class, documented value (SDL_CONTROLLER_BUTTON_GUIDE), unlike XInput, which only
/// reports it through an undocumented, unexported-by-name entry point (XInputGetStateEx,
/// ordinal 100). That undocumented call is the prime suspect for the "works when launched
/// interactively, silently fails when launched from Run-key at boot" behavior this replaces -
/// SDL2 doesn't rely on it at all.
///
/// Public surface is identical to the old XInput-based poller so nothing above this layer
/// (ActionRouter, Listener, orchestration) needed to change.
/// </summary>
public sealed class ControllerPoller : IDisposable
{
    private static readonly SDL.SDL_GameControllerButton[] AllButtons =
    [
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT,
    ];

    private bool _sdlInitialized;
    private IntPtr _controller = IntPtr.Zero;
    private int _controllerInstanceId = -1;
    private XButton _previousHeld = XButton.None;

    public XButton CurrentHeld { get; private set; }
    public XButton PressedThisTick { get; private set; }
    public XButton ReleasedThisTick { get; private set; }
    public bool IsConnected => _controller != IntPtr.Zero;

    public bool Poll()
    {
        EnsureInitialized();

        // Pump SDL's event queue so device add/remove and internal state stay current -
        // required even though we read button state via the polling API below, not events.
        while (SDL.SDL_PollEvent(out var ev) != 0)
        {
            if (ev.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED && ev.cdevice.which == _controllerInstanceId)
                CloseController();
        }

        // SDL's recommended pattern for polling (non-event-loop) usage: explicitly refresh
        // joystick/controller state before querying it, rather than relying solely on the
        // event pump above to have populated the device list.
        SDL.SDL_JoystickUpdate();
        SDL.SDL_GameControllerUpdate();

        if (_controller == IntPtr.Zero && !TryOpenFirstController())
        {
            Reset();
            return false;
        }

        var held = XButton.None;
        foreach (var button in AllButtons)
        {
            if (SDL.SDL_GameControllerGetButton(_controller, button) != 0)
                held |= ToXButton(button);
        }

        PressedThisTick = held & ~_previousHeld;
        ReleasedThisTick = _previousHeld & ~held;
        CurrentHeld = held;
        _previousHeld = held;
        return true;
    }

    private void EnsureInitialized()
    {
        if (_sdlInitialized) return;

        // SDL2's newer RawInput-based Windows joystick backend has a real, reproducible bug
        // on this hardware: it correctly reads high-index buttons (Guide) but silently drops
        // low-index ones (A/B/X/Y, indices 0-3) - confirmed by comparing raw button presses
        // with RawInput on vs off. The legacy DirectInput/XInput backend doesn't have this
        // problem, so force it off rather than relying on SDL2's default backend selection.
        SDL.SDL_SetHint(SDL.SDL_HINT_JOYSTICK_RAWINPUT, "0");
        SDL.SDL_Init(SDL.SDL_INIT_JOYSTICK | SDL.SDL_INIT_GAMECONTROLLER | SDL.SDL_INIT_EVENTS);
        _sdlInitialized = true;
    }

    private bool TryOpenFirstController()
    {
        int count = SDL.SDL_NumJoysticks();
        for (int i = 0; i < count; i++)
        {
            if (SDL.SDL_IsGameController(i) != SDL.SDL_bool.SDL_TRUE) continue;

            var handle = SDL.SDL_GameControllerOpen(i);
            if (handle == IntPtr.Zero) continue;

            _controller = handle;
            var joystick = SDL.SDL_GameControllerGetJoystick(handle);
            _controllerInstanceId = SDL.SDL_JoystickInstanceID(joystick);
            return true;
        }
        return false;
    }

    private void CloseController()
    {
        if (_controller != IntPtr.Zero)
            SDL.SDL_GameControllerClose(_controller);
        _controller = IntPtr.Zero;
        _controllerInstanceId = -1;
    }

    private void Reset()
    {
        _previousHeld = XButton.None;
        CurrentHeld = XButton.None;
        PressedThisTick = XButton.None;
        ReleasedThisTick = XButton.None;
    }

    private static XButton ToXButton(SDL.SDL_GameControllerButton button) => button switch
    {
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A => XButton.A,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B => XButton.B,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X => XButton.X,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y => XButton.Y,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK => XButton.Back,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE => XButton.Guide,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START => XButton.Start,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK => XButton.LeftThumb,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK => XButton.RightThumb,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER => XButton.LeftShoulder,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER => XButton.RightShoulder,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP => XButton.DPadUp,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN => XButton.DPadDown,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT => XButton.DPadLeft,
        SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT => XButton.DPadRight,
        _ => XButton.None,
    };

    public void Dispose()
    {
        CloseController();
        if (_sdlInitialized)
        {
            SDL.SDL_QuitSubSystem(SDL.SDL_INIT_GAMECONTROLLER);
            _sdlInitialized = false;
        }
    }
}
