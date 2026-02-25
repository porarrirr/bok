# Implementation Status

## Completed

- Android project scaffold with Gradle wrapper and buildable debug APK.
- iOS app source scaffold with ReplayKit + WebRTC integration.
- Cross-platform v2 pairing payload models (`init` / `confirm`) and validation.
- Cross-platform QR payload transport compression (`p2paudio-z1:` zlib + Base64URL).
- QR-only wizard flow on Android and iOS (manual code entry removed).
- 6-digit verification code gate before sender applies remote confirm payload.
- Failure handling reset to Step 1 for payload mismatch/expiry/verification mismatch.
- WebRTC peer controller on Android and iOS using host ICE and full SDP exchange.
- DataChannel-based PCM transport (`audio-pcm`) on Android and iOS.
- Android device-audio capture (`AudioPlaybackCapture`) to PCM sender pipeline.
- Android receiver PCM playback pipeline (`AudioTrack`).
- iOS ReplayKit extension PCM bridge into app process via App Group shared file.
- iOS receiver PCM playback pipeline (`AVAudioEngine`).
- iOS QR camera scanner UI integration (AVFoundation).
- Android + iOS QR payload codec regression tests updated for v2 decode behavior.

## Pending follow-up

- iOS Xcode project wiring, entitlements, and on-device signing validation.
- Optional QR payload chunking for edge cases where compressed SDP still exceeds one symbol.
- Optional migration from DataChannel PCM transport to RTP custom audio device path.
