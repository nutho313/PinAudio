# Changelog

## 1.0.0 — First public release

### Audio
- Per-zone channel mapping for a virtual pinball cabinet: BackGlass, SSR (rear), L/R, SUB, CENTER, SSF (front), across any detected audio device.
- Linked / Unlinked stereo per zone. Unlinked zones control L and R independently.
- Channel Selector: cycles through the zones (L/R, SUB, CENTER order matches the overlay); Volume Up / Volume Down act on the selected target.
- Night Mode: global attenuation (0–100 %) computed from the original channel levels, restored when turned off, on exit or when devices are refreshed.

### Overlay
- Live cabinet overlay with per-channel bars, linked or split L/R.
- Configurable screen, position, orientation, size, opacity and duration.
- Title centered, wide bars of equal length, Night Mode toggle, and the four shortcuts shown at the bottom in the order Channel Selector, Night Mode, Volume Down, Volume Up.

### Shortcuts
- Four global shortcuts: Channel Selector, Night Mode, Volume Down, Volume Up.
- Assign a keyboard key / combination **or a Pinscape / OpenPinDev controller button** (HID gamepad buttons 1–32).

### App
- Start with Windows, tray icon, minimize to tray on close.
