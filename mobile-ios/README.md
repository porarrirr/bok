# iOS App

This folder contains Swift source files for an iOS target using ReplayKit + WebRTC.

## Required Xcode setup

1. Create an iOS App target named `P2PAudio`.
2. Add a Broadcast Upload Extension target named `AudioBroadcastExtension`.
3. Add Swift Package dependency:
   - `https://github.com/webrtc-sdk/webrtc`
4. Add required capabilities:
   - App Groups (for app + broadcast extension shared state and PCM bridge file).
   - Background Modes -> Audio, AirPlay, and Picture in Picture.
5. Add privacy keys in app `Info.plist`:
   - `NSCameraUsageDescription` (QR scanner).
   - `NSMicrophoneUsageDescription` (if ReplayKit UI path requests it in your target setup).
6. Wire these source files into app/extension targets.
7. Optional: use `mobile-ios/project.yml` (`xcodegen`) to generate `P2PAudio.xcodeproj`.

## GitHub Actions: IPA build

Workflow: `.github/workflows/ios-ipa.yml`

Run it from `Actions -> Build iOS IPA -> Run workflow`.
This workflow builds an unsigned `.ipa` (no code signing secrets required).
If `project_path` is missing, CI auto-generates the project from `project_spec_path` using `xcodegen`.
You can also:
- push a Git tag (for example `v0.1.0`) to automatically build and publish `unsigned.ipa` to that tag's GitHub Release,
- or publish a GitHub Release to build and attach `unsigned.ipa` to the published release tag.

`workflow_dispatch` inputs:

- `project_path`: `.xcodeproj` or `.xcworkspace` path in this repo.
- `project_spec_path`: `xcodegen` spec path used as fallback when `project_path` does not exist.
- `scheme`: Scheme name to archive.
- `configuration`: Usually `Release`.
- `publish_release`: `true` にすると `unsigned.ipa` を GitHub Releases に配置。
- `release_tag`: リリースタグ（手動実行で空なら `ios-unsigned-<run_number>`。タグpush実行ではそのタグ名）。
- `release_name`: リリース名（空なら自動）。

## Notes

- iOS sender captures ReplayKit app audio only.
- Ringtones/notifications/system-wide audio cannot be captured by third-party apps.
- There is no relay/signaling server. QR payload exchange is device-to-device.
- PCM audio is transported over WebRTC DataChannel label `audio-pcm`.
- QR payload transport supports compressed mode (`p2paudio-z1:` zlib + Base64URL).
- Unsigned IPA cannot be installed directly on physical iOS devices without re-signing.

## In-app logs

- The app has a built-in log screen (`Logs` button on the main screen).
- The log viewer stores recent entries in-memory (ring buffer style) and supports:
  - clear logs,
  - copy all logs to clipboard.
- Logs include WebRTC state transitions, ReplayKit bridge events, payload flow milestones, and failure diagnostics.
- Sensitive data policy:
  - SDP and payload full contents are not printed; only metadata such as lengths/session IDs are shown.
