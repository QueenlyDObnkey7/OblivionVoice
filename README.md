# OblivionVoice

Integrated proximity voice chat for OblivionMP / ReadyM. Source version **0.4.3**, with an SDK **0.2.0** minimum declared in the manifest.

The client captures Windows microphone audio, encodes it with Opus and sends it to an authenticated UDP relay. Other players hear spatial stereo playback, with per-speaker buffering, normalization and environment effects. No separate Mumble or TeamSpeak client is used.

## Requirements

- Windows for the client’s WASAPI audio and Windows key-release detection.
- .NET 10 SDK and PowerShell to build/package using the included script.
- Matching official OblivionMP SDK dependencies and server runtime. The project expects flat `Dependencies/Client` and `Dependencies/Server` folders.
- NuGet access during the first build to restore the versions pinned in the project files.

SDK binaries are intentionally excluded from this source repository. Supply a compatible official template’s `Dependencies` directory; the manifest minimum does not guarantee compatibility with every later SDK.

## Build

1. Obtain the official OblivionMP mod template matching your SDK installation.
2. Copy its dependencies using:

```powershell
.\Copy-Official-Dependencies.ps1 -OfficialTemplatePath "C:\path\to\oblivionmp-mod-template"
```

3. Edit `ServerContent/voiceconfig.json`. Set `Network.AdvertisedHost` to the address players can reach. For normal use set both `Debug.Enabled` and `Debug.LoopbackMicrophone` to `false`; the supplied configuration retains the uploaded testing values.
4. Build and package:

```powershell
.\BUILD.ps1
```

By default this creates `Output/mods/OblivionVoice` without deploying. It includes `manifest.json`, a `client` folder with audio dependencies, and a `server` folder with the server assemblies and `voiceconfig.json`. SDK/host assemblies are excluded from the deployed package.

### Build options

| Option | Default | What it does |
| --- | --- | --- |
| `-Configuration` | `Release` | Selects `Release` or `Debug`; Debug packaging also includes project PDB files. |
| `-ServerRoot` | Empty | If supplied, deploys to this server folder’s `mods/OblivionVoice` after building. |
| `-SkipDeploy` | Off | Packages only, even when ServerRoot is supplied. |
| `-NoExplorer` | Off | Prevents Windows Explorer opening after deployment. |

`Copy-Official-Dependencies.ps1` requires `-OfficialTemplatePath`, the root of the official template, and replaces the local Dependencies folder with its copy.

## Install or update

Stop the server, back up any deployed `voiceconfig.json`, and copy `Output/mods/OblivionVoice` into the server’s `mods` folder. Alternatively:

```powershell
.\BUILD.ps1 -ServerRoot "C:\path\to\OBMP" -NoExplorer
```

Deployment replaces the existing mod folder, including its config. Put intended settings in `ServerContent/voiceconfig.json` before rebuilding. The script also recreates `Output` on each build.

Allow inbound UDP on the configured voice port (22100 by default). Restart the server and reconnect players. The server distributes the client half; remove any old manually installed OblivionVoice client copy to avoid duplicate assemblies. Do not deploy this package to the old `server_mods` layout.

The running server reads `mods/OblivionVoice/server/voiceconfig.json`, beside its server DLL. Changes require a restart and fresh client bootstrap/reconnection. If the file is missing, the server generates class defaults, which differ from the shipped example (including bitrate, DTX, routing, transmit mode, pan and debug settings).

## Configuration reference

Values below are the **shipped JSON values**, preserved from the uploaded package. “Ignored”, “unused” and “reserved” describe actual source behavior; those entries remain documented so an apparent setting is not mistaken for a working feature.

| Option | Shipped value | Effect |
| --- | --- | --- |
| `Enabled` | `true` | Enables voice bootstrap and relay operation. |
| `Network.BindAddress` | `"0.0.0.0"` | Local IP the UDP relay listens on. `0.0.0.0` listens on all IPv4 interfaces; use an IP assigned to the server. |
| `Network.AdvertisedHost` | `"127.0.0.1"` | IP address or hostname given to players. Replace `127.0.0.1` with the server’s reachable public IP/hostname for remote players; localhost only works on the server machine. |
| `Network.Port` | `22100` | Voice UDP port, separate from the game port. Allowed: 1–65535. Allow inbound UDP and forward this port if behind a router. |
| `Network.SessionTokenTtlSeconds` | `60` | Seconds a newly issued, single-use authentication token remains valid before connection. Allowed: 10–600. |
| `Network.ClientTimeoutSeconds` | `20` | Seconds without client activity before the relay removes a voice connection. Allowed: 5–300. |
| `Network.MaxPacketBytes` | `1200` | Maximum incoming UDP datagram size in bytes; larger packets are discarded. Allowed: 256–65000. Keep large enough for encoded frames. |
| `Network.MaxAudioPacketsPerSecond` | `80` | Per-client audio packet rate cap. Allowed: 10–500. 20 ms frames produce about 50 packets/second; 10 ms produces about 100, so raise the cap if using 10 ms. |
| `Audio.SampleRate` | `48000` | Capture/codec sample rate in Hz. This release requires 48000. |
| `Audio.Channels` | `1` | Capture/codec channel count. This release requires 1 (mono); playback uses stereo for panning. |
| `Audio.FrameMilliseconds` | `20` | Duration of one Opus frame. Allowed: 10, 20, 40 or 60 ms. Smaller frames increase packet rate; larger frames add delay and produce larger packets. |
| `Audio.Bitrate` | `64000` | Opus target bitrate in bits/second. Allowed: 6000–128000. Higher values increase bandwidth. Keep frame size within the packet limit. |
| `Audio.EnableDtx` | `false` | Enables Opus discontinuous transmission to reduce encoded data during silence. |
| `Audio.EnableFec` | `true` | Requests Opus in-band forward error correction at the encoder. Current playback decodes with FEC disabled and uses packet-loss concealment, so this is not complete receive-side FEC recovery. |
| `Audio.MicrophoneBufferMilliseconds` | `40` | Requested WASAPI capture buffer duration in milliseconds, clamped to at least 20. Larger buffers can add capture delay. |
| `Audio.PlaybackBufferMilliseconds` | `250` | Currently ineffective: forwarded to playback.Start, but its buffer parameter is unused. The WASAPI builder uses its default latency. |
| `Audio.NoiseSuppressionEnabled` | `true` | Ignored JSON entry: absent from the server config model/RPC. Client noise suppression stays at its source default, false. |
| `Audio.VoiceActivityDetectionEnabled` | `true` | Ignored JSON entry: absent from the server config model/RPC. Client voice activity gating stays at its source default, false. |
| `Audio.VadOpenThresholdDb` | `9.0` | Ignored JSON entry. The client source default is 9 dB above the tracked noise floor to open the gate; VAD is currently off. |
| `Audio.VadCloseThresholdDb` | `5.0` | Ignored JSON entry. The client source default is 5 dB above the noise floor to close the gate; VAD is currently off. |
| `Audio.LoudnessNormalizationEnabled` | `true` | Ignored JSON entry. Normalization effects are added in client code and cannot be switched off through this JSON option. |
| `Proximity.DefaultMode` | `"Normal"` | Starting voice range mode: `Whisper`, `Normal` or `Shout`. |
| `Proximity.WhisperMeters` | `2.0` | Whisper distance in metres. Must be greater than zero and less than NormalMeters. |
| `Proximity.NormalMeters` | `8.0` | Normal speaking distance in metres. Must be greater than WhisperMeters and less than ShoutMeters. |
| `Proximity.ShoutMeters` | `25.0` | Shouting distance in metres. Must be greater than NormalMeters. |
| `Proximity.MaximumReceiveMeters` | `35.0` | Upper cap used by server routing. Must be at least ShoutMeters. With valid ordered ranges it normally does not reduce any mode’s range. |
| `Proximity.UseParentCell` | `true` | Currently ineffective as a routing switch. The client cannot apply it; server cell filtering instead follows ServerSideRouting and runs regardless of this value. |
| `Proximity.ServerSideRouting` | `true` | When true, relay filters by speaker range and shared space when positions are available, and includes the speaker environment. Missing positions can fall back to forwarding. When false, relay broadcasts and the client filters by distance; server cell filtering is off and environment falls back to Outdoor. |
| `Proximity.WorldUnitsPerMeter` | `100.0` | Converts game coordinates into metres for distance checks. Keep positive. 70 is the supplied assumption, not a verified SDK unit guarantee; calibrate in game before tuning ranges. |
| `Proximity.InvertPan` | `true` | Swaps the left/right spatial panning direction. Change if a speaker on your left sounds on your right. |
| `Controls.TransmitKey` | `"N"` | Key that starts/stops local microphone transmission. Must be a valid ReadyM Key enum name; `N` is supplied. |
| `Controls.TransmitMode` | `"Toggle"` | `Hold`: transmit while the key is held. `Toggle`: press once to start and again to stop. |
| `Controls.CycleRangeKey` | `"H"` | Cycles Whisper → Normal → Shout. Must be a valid ReadyM Key enum name. |
| `Controls.ToggleMuteKey` | `"M"` | Toggles incoming voice playback. This does not stop your microphone transmission. |
| `Controls.RadioTransmitKey` | `"CapsLock"` | Reserved and currently inactive: no radio transmit key binding is registered. |
| `Radio.Enabled` | `false` | Reserved: copied to client settings, but this release has no working radio transmission/routing implementation. |
| `Radio.DefaultChannel` | `1` | Reserved and unused. Does not assign a working radio channel. |
| `Radio.MaximumChannels` | `32` | Reserved and unused. Does not enforce a radio channel limit. |
| `Ui.Enabled` | `true` | Currently unused; does not hide the voice HUD. |
| `Ui.ShowSpeakingIndicator` | `true` | Currently unused; does not control speaking indicators. |
| `Ui.ShowCurrentRange` | `true` | Currently unused; does not control range display. |
| `Ui.BrandName` | `"OblivionVoice"` | Label used in the client initialization log. It is not a general HUD branding override. |
| `Debug.Enabled` | `true` | Enables gated diagnostic logging and ForceTransmit handling. Some startup/RPC/trace logs still run when false. LoopbackMicrophone is checked independently by the server. |
| `Debug.LogPackets` | `false` | Verbose packet diagnostics when Debug.Enabled is true. |
| `Debug.LoopbackMicrophone` | `true` | Allows the relay to send your own voice back to you for testing. Server forwarding checks this independently of Debug.Enabled; explicitly set false to disable loopback. |
| `Debug.LogClientStats` | `true` | Periodic client audio counters and server relay stats when Debug.Enabled is true. |
| `Debug.LogAudioLevels` | `true` | Logs microphone RMS levels when Debug.Enabled is true. |
| `Debug.LogKeyEvents` | `true` | Logs voice key actions and transmission state changes when Debug.Enabled is true. |
| `Debug.ForceTransmit` | `false` | With Debug.Enabled true, continuously transmits after initialization without normal push-to-talk. Testing only; leave false for normal use. |
| `Debug.StatsIntervalSeconds` | `2` | Diagnostic reporting interval in seconds. Allowed: 1–60, even if debug is disabled. |

## Controls

The shipped bindings are N to toggle microphone transmission, H to cycle speaking range, and M to mute/unmute incoming voices. Toggle mode leaves the microphone transmitting until N is pressed again. Radio is not implemented.

Keys must exist in the installed SDK’s `Key` enum. Hold mode also requires the Windows release detector to recognize the key: letters, digits, F1–F24, CapsLock, supported Shift/Control/Alt names, Space, Mouse4 or Mouse5. A name accepted by Windows still needs to be accepted by the SDK.

## Project layout

| Path | Purpose |
| --- | --- |
| `OblivionVoice.Client/` | Input, microphone capture, Opus encoding, UDP client, spatial playback and HUD. |
| `OblivionVoice.Server/` | Config loading, session tokens, UDP relay and player position cache. |
| `OblivionVoice.Common/` | Shared RPC contracts, voice modes and protocol values. |
| `Content/manifest.json` | Mod identity, version, author and SDK dependency. |
| `ServerContent/voiceconfig.json` | Configuration copied into the packaged server half. |
| `BUILD.ps1` | Build, package and optional local deployment. |
| `ModFiles.ps1` | Assembly/content lists used by BUILD.ps1. |
| `Copy-Official-Dependencies.ps1` | Copies SDK dependencies from a local official template. |
| `OblivionVoice.sln`, `global.json` | Solution and SDK selection. |
| `THIRD_PARTY.md` | Dependency inventory. |
