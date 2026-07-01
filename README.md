# XboxLARP

Xbox-controller-driven "game mode" switcher for multi-monitor Windows PCs running [Playnite](https://playnite.link/).

Hold the Guide (Xbox) button + Start to disable every monitor except a designated game
monitor and launch Playnite Fullscreen. Close Playnite (or hit Guide + Back) and it switches
your monitors back automatically. Also provides Guide-held controller shortcuts for basic
desktop navigation (arrows, Enter, Esc, Alt-Tab).

Built specifically to survive the display-topology instability that shows up on 3+ monitor
setups when naively enabling/disabling displays (see [How it works](#how-it-works) below).

## Requirements

- Windows 10/11
- [Playnite](https://playnite.link/) installed, with Fullscreen mode available
- An Xbox controller — or any controller Windows sees as one. This app reads the standard
  XInput API, so it can't tell a real Xbox controller apart from a **virtual** one. That means
  PlayStation controllers work fine through **DS4Windows** or **Steam Input's Xbox
  Configuration Support**, since both create a virtual XInput device — just make sure the PS
  button is mapped to pass through as the Guide button in whichever tool you use. There's no
  native (non-emulated) DirectInput/HID support for PlayStation controllers.

## Install

Download the latest release exe from the [Releases](../../releases) page and run it — no
installer, no .NET runtime required (self-contained single-file build).

Windows SmartScreen will likely flag it as an unrecognized publisher on first run since it
isn't code-signed — click "More info" → "Run anyway".

## One-time setup

**XboxLARP does not touch Xbox Game Bar or Steam settings itself.** Both are known to
intercept the Guide button system-wide before it ever reaches this (or any) app, so you have
to disable that interception yourself first, or Guide-held chords won't register at all. **If
Steam is installed, step 2 below is mandatory even if it isn't running in the foreground** —
Steam's controller hook runs in the background as long as Steam itself is running (including
at startup, if you have it set to launch on login), whether or not you're actively using it.

1. **Disable Xbox Game Bar's controller shortcut.** Try the simple toggle first: Settings →
   Gaming → Xbox Game Bar → turn off "Open Xbox Game Bar using this button on a controller".

   If the Guide button *still* opens Game Bar after that (the toggle alone wasn't enough on
   the machine this was built on), remove Game Bar's overlay components entirely. In an
   elevated PowerShell:
   ```powershell
   Get-AppxPackage "*XboxGamingOverlay*" | Remove-AppxPackage
   Get-AppxPackage "Microsoft.XboxGameOverlay" | Remove-AppxPackage
   New-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\GameBar" -Name "UseNexusForGameBarEnabled" -Value 0 -PropertyType DWord -Force
   New-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\GameBar" -Name "AppCaptureEnabled" -Value 0 -PropertyType DWord -Force
   New-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_Enabled" -Value 0 -PropertyType DWord -Force
   ```
   This removes the Xbox Game Bar app for your user account (not the Xbox app/sign-in
   components — those are untouched and unrelated to the Guide button).
2. **If Steam is installed, disable its Guide-button hook.** This is required, not optional —
   even with Game Bar fully removed, Steam grabs the Guide button on its own and nothing in
   step 1 affects it. Steam → Settings → Controller → General Controller Settings → Show
   Advanced Settings → turn off **"Guide button focuses Steam"** (also turn off "Enable Guide
   Button Chords for controllers" if you don't want Steam's own Guide+button shortcuts
   competing too).
3. Run `XboxLARP.exe`. It starts in the system tray (left-click the tray icon to open the
   window).
4. In the main window: **Manage Monitors...** → pick which monitor is the dedicated game
   monitor → **Save**.
5. Arrange your monitors normally in Windows Display Settings (game monitor off, everything
   else on as you'd normally use them), then click **Capture Current Layout as 'Normal'**.
6. Turn off everything except the game monitor (the **Activate '\<role>' Alone** button in the
   same dialog can do this for you without touching Display Settings), then click
   **Capture Current Layout as 'GameOnly'**.
7. Set your Playnite Fullscreen path (**Browse...** next to "Playnite Fullscreen path" in the
   main window) — usually `<Playnite install folder>\Playnite.FullscreenApp.exe`.
8. "Run at Login" is enabled by default the first time it runs; uncheck it in the tray menu or
   main window if you don't want that.

## Controls

Default bindings (all rebindable in `%LOCALAPPDATA%\XboxLARP\config\controller.config.json`,
or view them anytime via **View Controls...** in the app):

| Chord | Action |
|---|---|
| Guide + Start | Enter Game Mode (switch monitors, launch Playnite) |
| Guide + Back | Close Game Mode (close Playnite, restore monitors) |
| Guide + D-Pad | Arrow keys |
| Guide + A | Enter |
| Guide + B | Escape |
| Guide + X | Alt-Tab forward (tap repeatedly to cycle, release Guide to select) |
| Guide + Y | Alt-Tab backward |

## How it works

Windows' display API breaks in specific, well-documented ways when naively enabling/disabling
monitors on 3+ display systems (source IDs get reassigned, positions drift on re-enable,
looped `SetDisplayConfig` calls leave the topology transiently inconsistent). This app avoids
that by:

- Building **one full path+mode array** covering every known monitor (unwanted ones present
  but inactive) and applying it with a **single** `SetDisplayConfig` call — never a loop of
  per-monitor enable/disable calls.
- Identifying monitors by **adapter LUID + target ID + EDID**, never by source ID or
  `\\.\DISPLAYn` name (both are enumeration-order artifacts Windows reassigns on every
  topology rebuild).
- Verifying the result with a re-query + bounded retry after every apply, logged to
  `%LOCALAPPDATA%\XboxLARP\logs\controller.log`.

The Guide button is read via `XInputGetStateEx` (an undocumented ordinal-100 export) since
plain `XInputGetState` doesn't report it — this is the same technique most controller remap
tools use; it's unofficial but has been stable for years.

## CLI (optional)

Everything above is also available as CLI flags on the same exe (`--setup-monitors`,
`--capture-profile`, `--apply-profile`, `--query`, `--stress-test`, `--dry-run`, etc.) — run
`XboxLARP.exe --help` for the full list. Useful for scripting or diagnosing a flaky monitor
switch without the GUI.

## Building from source

```
dotnet build XboxLARP.sln
```

Requires .NET 9 SDK. To produce a self-contained single-file release build:

```
dotnet publish XboxLARP/XboxLARP.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## License

MIT — see [LICENSE](LICENSE).
