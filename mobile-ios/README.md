# iOS App Skeleton

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

## Notes

- iOS sender captures ReplayKit app audio only.
- Ringtones/notifications/system-wide audio cannot be captured by third-party apps.
- There is no relay/signaling server. QR payload exchange is device-to-device.
- PCM audio is transported over WebRTC DataChannel label `audio-pcm`.
