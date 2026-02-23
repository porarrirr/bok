# Validation Matrix

## Functional

- Android -> Android: media/game audio playback on receiver.
- Android -> iOS: media/game audio playback on receiver.
- iOS -> Android: ReplayKit captured app audio playback on receiver.
- iOS -> iOS: ReplayKit captured app audio playback on receiver.
- QR camera scan flow on iOS for both Offer and Answer payloads.

## Failure

- Permission denied (`permission_denied`).
- Capture unsupported (`audio_capture_not_supported`).
- SDP mismatch (`webrtc_negotiation_failed`).
- LAN disconnect (`network_changed`).
- Expired QR payload (`session_expired`).
- DataChannel closed or backpressured (`interrupted`/`failed` state transition).

## Performance

- One-way latency target <= 200 ms on stable LAN.
- Session survival for 5 minutes without fatal errors.
- No relay/signaling server dependency (LAN host candidates only).
