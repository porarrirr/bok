# State Machine

## Stream States

- `idle`
- `capturing`
- `connecting`
- `streaming`
- `interrupted`
- `failed`
- `ended`

## Setup Substates (UI pairing flow)

- `entry`
- `sender_show_init`
- `sender_verify_code`
- `listener_scan_init`
- `listener_show_confirm`

## Key Transitions

- `entry -> sender_show_init`: sender starts capture and init payload generation.
- `entry -> listener_scan_init`: listener starts QR scan flow.
- `sender_show_init -> sender_verify_code`: sender scans listener confirm payload.
- `listener_scan_init -> listener_show_confirm`: listener scans sender init payload and generates confirm payload.
- `sender_verify_code -> connecting`: sender user confirms 6-digit code match.
- `connecting -> streaming`: ICE connected and `audio-pcm` DataChannel opened.
- `streaming -> interrupted`: transient LAN drop, DataChannel close, or capture starvation.
- `interrupted -> streaming`: transport recovers and frames resume.
- `* -> failed`: unrecoverable negotiation/capture/transport error.
- `* -> entry`: payload invalid/expired/session mismatch/verification mismatch (mandatory restart).
- `* -> ended`: user ends the session.
