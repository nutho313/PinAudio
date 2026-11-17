# Nutho313 PinAudio

Multi-channel audio output controller for virtual pinball cabinets (Windows).
Map the speakers of your cabinet (backglass, rear, playfield L/R, sub, center, front) to the channels of your sound card, control each zone's volume with global shortcuts or Pinscape / OpenPinDev buttons, and see the result in a live on-screen overlay.

## Features

- **Audio Mapping** — assign BackGlass, SSR, L/R, SUB, CENTER and SSF to any channel of any detected audio device.
- **Linked / Unlinked** — control a stereo zone with one bar, or split it into independent L and R bars.
- **Channel Selector + Volume Up / Down** — select a zone, then raise or lower its volume.
- **Night Mode** — attenuate every channel by a chosen percentage (0–100 %), then restore the original levels exactly when turned off.
- **Live overlay** — cabinet-shaped overlay on the display of your choice (screen, position, orientation, size, opacity, duration), with the shortcut legend at the bottom.
- **Shortcuts from keyboard or controller** — four global shortcuts (Channel Selector, Night Mode, Volume Down, Volume Up), each assignable to a key / key combination or to a **Pinscape / OpenPinDev button**.
- Start with Windows, tray icon, minimize to tray.

## Requirements

- Windows 10 / 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- A multi-channel audio device (for example a 7.1 sound card or USB audio interface)

## Install

Download the latest build from the [Releases](../../releases) page, unzip it anywhere and run `Nutho313PinAudio.exe`.

## Usage

1. **Audio Mapping** — choose which device channel feeds each cabinet zone.
2. **Audio Setting** — set Linked / Unlinked per zone, the Night Mode percentage, and the four global shortcuts.
3. **Display** — pick the screen, position and orientation of the overlay.

### Pinscape / OpenPinDev buttons

In Audio Setting, click a shortcut card and press a button on your controller instead of a key. The button is stored together with the controller's VID:PID, so other gamepads don't trigger it.

The controller must appear in Windows' *Set up USB game controllers* (`joy.cpl`). Buttons 1–32 are supported. If a button isn't detected, you can also configure it as a keyboard key in the Pinscape configuration and assign that key instead.

### Settings location

Settings are stored in `%APPDATA%\AudioOutputController\` (`settings.json`, `app-settings.json`, `display-settings.json`, `audio-mappings.json`).

## Build from source

```
git clone https://github.com/nutho313/PinAudio.git
cd PinAudio/AudioOutputController
dotnet build -c Release
```

Publish a ready-to-run build:

```
dotnet publish -c Release -r win-x64 --self-contained false -o ../publish
```

## Support

If this tool is useful to you: [Ko-fi — nutho313](https://ko-fi.com/nutho313)
