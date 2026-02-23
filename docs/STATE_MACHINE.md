# State Machine

## States

- `idle`
- `capturing`
- `connecting`
- `streaming`
- `interrupted`
- `failed`
- `ended`

## Key transitions

- `idle -> capturing`: capture permission granted and local capture pipeline started.
- `capturing -> connecting`: local offer/answer generated.
- `connecting -> streaming`: ICE connected and `audio-pcm` DataChannel opened.
- `streaming -> interrupted`: transient LAN drop, DataChannel close, or capture starvation.
- `interrupted -> streaming`: transport recovers and frames resume.
- `* -> failed`: unrecoverable negotiation/capture/transport error.
- `* -> ended`: user ends the session.
