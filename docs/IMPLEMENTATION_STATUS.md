# Implementation Status

## Completed

- Android project scaffold with Gradle wrapper and buildable debug APK.
- iOS app source scaffold with ReplayKit + WebRTC integration.
- Cross-platform v2 pairing payload models (`init` / `confirm`) and validation.
- Cross-platform payload transport compression (`p2paudio-z1:` zlib + Base64URL).
- Windows + Android text-payload exchange flow (copy/share/paste).
- 6-digit verification code gate before sender applies remote confirm payload.
- Failure handling reset to Step 1 for payload mismatch/expiry/verification mismatch.
- WebRTC peer controller on Android and iOS using host ICE and full SDP exchange.
- DataChannel-based PCM transport (`audio-pcm`) on Android and iOS.
- Android device-audio capture (`AudioPlaybackCapture`) to PCM sender pipeline.
- Android receiver PCM playback pipeline (`AudioTrack`).
- Windows -> Android UDP + Opus connection-code flow (Windows code generation, Android auto-confirm, automatic sender start without mDNS discovery).
- iOS ReplayKit extension PCM bridge into app process via App Group shared file.
- iOS receiver PCM playback pipeline (`AVAudioEngine`).
- iOS QR camera scanner UI integration (AVFoundation).
- Android + iOS payload codec regression tests updated for v2 decode behavior.
- Android + iOS connection diagnostics (`wifi_lan` / `usb_tether` / `unknown`) and local ICE candidate counters.
- Android + iOS USB-tether-aware failure mapping and user guidance (`usb_tether_unavailable`, `usb_tether_detected_but_not_reachable`, `network_interface_not_usable`).
- Windows implementation extended with:
  - WinUI sender/listener payload flow (text display, copy, paste, manual entry),
  - native bridge contract (`p2paudio_core_webrtc` C ABI) and managed P/Invoke integration,
  - WASAPI loopback sender pipeline to `audio-pcm` packet codec,
  - DataChannel receive polling + PCM playback on Windows listener side,
  - libdatachannel-backed offer/answer/apply/send/receive wiring in `core-webrtc`,
  - stream-state and diagnostics UI (`idle/capturing/connecting/streaming/interrupted/failed/ended`),
  - failure-hint to protocol failure-code normalization for Windows diagnostics,
  - native-required startup gate with development-only stub override (`ALLOW_STUB_FOR_DEV=1`),
  - x64 runtime alignment in app build settings and CI runtime staging checks.
- Windows ViewModel integration tests expanded with full `docs/TEST_MATRIX.md` coverage:
  - sender full flow (offer → verify → connect → stream),
  - listener full flow (import init → generate confirm → receive → stream),
  - expired/invalid/mismatched payload rejection and restart,
  - answer failure and apply-answer failure state transitions,
  - verification reject restart, stop state reset,
  - stream health monitoring (interrupted/recovered states),
  - USB tethering and Wi-Fi/LAN diagnostics display verification,
  - sender-listener verification code consistency check.

## Pending follow-up

- iOS Xcode project wiring, entitlements, and on-device signing validation.
- Optional alternative carriers for very large payloads (for example file transfer or QR on clients that keep it).
- Optional migration from DataChannel PCM transport to RTP custom audio device path.
- End-to-end Android/iOS/Windows interoperability validation matrix execution on physical devices (including USB tethering scenarios).
