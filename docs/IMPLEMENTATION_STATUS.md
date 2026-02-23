# Implementation Status

## Completed

- Android project scaffold with Gradle wrapper and buildable debug APK.
- Session payload models, codec, and validation on Android and iOS.
- QR generation on both platforms.
- Android sender/receiver flow wiring (permission -> offer -> answer -> apply).
- iOS sender/receiver flow wiring (offer/answer generation and application).
- WebRTC peer controller on Android and iOS using host ICE and full SDP exchange.
- DataChannel-based PCM transport (`audio-pcm`) on Android and iOS.
- Android device-audio capture (`AudioPlaybackCapture`) to PCM sender pipeline.
- Android receiver PCM media playback pipeline (`AudioTrack`).
- iOS ReplayKit extension PCM bridge into app process via App Group shared file.
- iOS receiver PCM media playback pipeline (`AVAudioEngine`).
- iOS QR camera scanner UI integration (AVFoundation).

## Pending follow-up

- iOS Xcode project wiring, entitlements, and on-device signing validation.
- QR payload chunking/compression for very large SDPs in strict camera environments.
- Optional migration from DataChannel PCM transport to RTP custom audio device path.
