# OblivionVoice 0.6.0 — speaking icons

Requires OblivionUI 0.4.0. Install the complete UI and Voice folders, restart the server, then restart the game and reconnect.

- A small ivory-and-bronze speaker symbol appears at bottom right during detected microphone speech while transmitting. It clears shortly after speech stops; holding push-to-talk silently does not keep it lit.
- Other active speakers use the same symbol above their heads. The old green "speaking" marker is removed.
- Overhead symbols disappear behind walls, around corners, off screen, while sneaking or dead, and whenever visibility cannot be established.
- The UI controls only indicators; hearing range, attenuation, push-to-talk/toggle behavior, microphone choice and gain are unchanged. Voice settings still use F5.
- The UI demo now uses Alt+U so Alt+B remains available for the arena shop.

In-game verification: speak with PTT/toggle; stop speaking; open/close the settings book; have a second speaker stand in view, walk behind a solid wall/corner and sneak while still talking. Verify the remote icon hides promptly and returns when standing in clear view. The local icon should stay anchored to the bottom right at your resolution.

Automated validation includes the shared UI policy checks and actual UE 5.3.2 collision queries. These do not substitute for a two-player OBMP test on the game's collision geometry.
