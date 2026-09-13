# Voice settings book — 0.5.1

Press F5 while in control of your character to open the book. Requires OblivionUI 0.3.2.

- Input device: active Windows microphones, Windows default, or a labelled unavailable saved device.
- Microphone gain: a slider from -24 to +24 dB, with 0 dB exactly at the centre and a 1 dB keyboard step. It starts at your saved gain. Gain applies after preprocessing and clamps samples to the PCM range.
- Transmit mode: server default, hold-to-talk, or toggle-to-talk. The speak key remains the server-configured key shown at the top of the book.
- Apply and save: restarts capture if the device changed and saves preferences locally. Capture is restored to the previous device if applying or saving fails, when that previous device remains available.
- Close: discards unsaved edits. Opening the menu stops active transmission; it does not resume automatically when closed.

Settings are stored at `%LOCALAPPDATA%/OblivionVoice/settings.json`. This file stores only device ID, input gain and transmit-mode preference. Server address, credentials, codec and server routing rules are not saved here. If a saved device is unavailable on startup, capture falls back to Windows default for that session.

Validation: 12 preference/gain checks, existing voice transport checks, and bootstrap lifecycle checks passed. Client compiles with Windows-only NAudio platform-analyzer warnings. Hardware switching, audible gain, and the F5 book require in-game user testing; no microphone audio was recorded or sent during development tests.

Install OblivionVoice and OblivionUI from the bundle while the server is stopped. Preserve existing server/voiceconfig.json. Restart the server and reconnect through ReadyM.
