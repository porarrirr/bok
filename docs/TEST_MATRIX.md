# Validation Matrix

## Functional

- Android -> Android: media/game audio playback on receiver.
- Android -> iOS: media/game audio playback on receiver.
- iOS -> Android: ReplayKit captured app audio playback on receiver.
- iOS -> iOS: ReplayKit captured app audio playback on receiver.
- QR-only flow on both platforms:
  - sender init QR generation
  - listener confirm QR generation
  - sender confirm QR scan
  - 6-digit code verification

## Failure

- Permission denied (`permission_denied`).
- Capture unsupported (`audio_capture_not_supported`).
- SDP mismatch (`webrtc_negotiation_failed`).
- LAN disconnect (`network_changed`).
- Expired init/confirm payload (`session_expired`).
- Invalid payload version/phase (`invalid_payload`).
- Session ID mismatch between init/confirm (`invalid_payload`).
- Verification mismatch (user chooses mismatch -> forced restart).
- Legacy v1 payload provided to v2 decoder (must fail validation).
- DataChannel closed or backpressured (`interrupted`/`failed` state transition).

## Performance

- One-way latency target <= 200 ms on stable LAN.
- Session survival for 5 minutes without fatal errors.
- No relay/signaling server dependency (LAN host candidates only).
