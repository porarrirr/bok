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
- `path_diagnosing`
- `sender_show_init`
- `sender_verify_code`
- `listener_scan_init`
- `listener_show_confirm`

State names are retained for compatibility with existing code, even when a platform uses pasted/shared payload text instead of QR scanning.

## Key Transitions

- `entry -> sender_show_init`: sender starts capture and init payload generation.
- `entry -> listener_scan_init`: listener starts init-payload intake flow.
- `entry -> path_diagnosing`: classify local path (`wifi_lan` / `usb_tether` / `unknown`) before/while ICE gathering.
- `path_diagnosing -> sender_show_init`: sender payload generation continues with diagnostics attached.
- `path_diagnosing -> listener_scan_init`: listener init-payload intake continues with diagnostics attached.
- `sender_show_init -> sender_verify_code`: sender imports listener confirm payload.
- `listener_scan_init -> listener_show_confirm`: listener imports sender init payload and generates confirm payload.
- `sender_verify_code -> connecting`: sender user confirms 6-digit code match.
- `connecting -> streaming`: ICE connected and `audio-pcm` DataChannel opened.
- `streaming -> interrupted`: transient LAN drop, DataChannel close, or capture starvation.
- `interrupted -> streaming`: transport recovers and frames resume.
- `* -> failed`: unrecoverable negotiation/capture/transport error.
- `* -> entry`: payload invalid/expired/session mismatch/verification mismatch (mandatory restart).
- `* -> ended`: user ends the session.
