# Protocol

## Pairing Flow

1. Sender gathers host ICE candidates and creates a full SDP offer.
2. Sender shows `SessionOfferPayload` as a QR code.
3. Receiver scans, validates payload, creates full SDP answer.
4. Receiver shows `SessionAnswerPayload` as a QR code.
5. Sender scans, validates payload, applies remote answer.
6. A WebRTC DataChannel labeled `audio-pcm` carries PCM frames from sender to receiver.

## Offer Payload

```json
{
  "version": "1",
  "role": "sender",
  "sessionId": "uuid",
  "senderDeviceName": "pixel-8",
  "senderPubKeyFingerprint": "sha-256-fingerprint",
  "offerSdp": "v=0...",
  "expiresAtUnixMs": 1760000000000
}
```

## Answer Payload

```json
{
  "version": "1",
  "role": "receiver",
  "sessionId": "uuid",
  "receiverDeviceName": "iphone-15",
  "receiverPubKeyFingerprint": "sha-256-fingerprint",
  "answerSdp": "v=0...",
  "expiresAtUnixMs": 1760000000000
}
```

## QR Transport Encoding

- Canonical payload is JSON (`SessionOfferPayload` / `SessionAnswerPayload`).
- Implementations may emit a compressed transport string:
  - Prefix: `p2paudio-z1:`
  - Body: `zlib(json-bytes)` encoded as Base64URL without padding.
- Decoders must accept both:
  - legacy raw JSON string
  - compressed transport string with the prefix above

## Audio PCM Packet (`audio-pcm`)

Binary packet format (little-endian):

- `version` (`u8`) - currently `1`
- `channels` (`u8`) - `1` or `2`
- `bitsPerSample` (`u16`) - currently `16`
- `sampleRate` (`u32`) - currently `48000`
- `frameSamplesPerChannel` (`u16`) - currently `960` (20 ms @ 48 kHz)
- `sequence` (`u32`) - monotonically increasing per session
- `timestampMs` (`u64`) - sender wall-clock milliseconds
- `pcmPayload` (`bytes`) - interleaved PCM16 samples

## Rules

- Payload expires after 60 seconds.
- `sessionId` must match between offer and answer.
- Reject invalid role, version, and expired payload.
- Use host candidates only (LAN scope).
- Audio transport uses DataChannel only; no relay/signaling server.
