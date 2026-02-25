# Android App

## Requirements

- Android Studio Koala or newer
- Android SDK 35
- JDK 17

## Import

Open this folder as a Gradle project in Android Studio.

## Current implementation status

- Session model + payload validation: implemented.
- QR payload generation and scan flow: implemented.
- WebRTC host-only connection and SDP exchange: implemented.
- `AudioPlaybackCapture` permission + capture startup: implemented.
- DataChannel PCM transport (`audio-pcm`): implemented.
- Receiver-side PCM media playback (`AudioTrack`): implemented.
- Foreground service during sender mode: implemented.

## Important limitations

- Audio capture works only for source apps that allow playback capture (`allowAudioPlaybackCapture`).
- LAN host ICE only (no relay/signaling/NAT traversal).
- Very large SDP payloads may require QR chunking in future revisions.

## Debug logging

- App logs use `Logcat` with tags prefixed by `P2PAudio/`.
- Useful categories:
  - `P2PAudio/MainViewModel`
  - `P2PAudio/PeerConnection`
  - `P2PAudio/CaptureManager`
  - `P2PAudio/AudioSendService`
- Example filter:
  - `adb logcat | findstr P2PAudio/`
