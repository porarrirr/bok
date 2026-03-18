# Protocol

## Pairing Flow (out-of-band payload exchange)

1. Sender starts capture and gathers host ICE candidates, then creates an SDP offer.
2. Sender encodes `PairingInitPayload` as a transport string and shares it out-of-band.
3. Listener imports and validates init payload, then creates an SDP answer.
4. Listener encodes `PairingConfirmPayload` as a transport string and shares it back out-of-band.
5. Sender imports and validates confirm payload.
6. Both devices compute and display the same 6-digit verification code from:
   - `sessionId`
   - sender fingerprint
   - listener fingerprint
7. User confirms the 6-digit match on sender side.
8. Sender applies remote answer; WebRTC DataChannel `audio-pcm` carries PCM audio.

Windows and Android exchange these transport strings as text (copy/share/paste). Other clients may render the same transport string as QR if their UI still supports it.

## Init Payload (`PairingInitPayload`)

```json
{
  "version": "2",
  "phase": "init",
  "sessionId": "uuid",
  "senderDeviceName": "pixel-8",
  "senderPubKeyFingerprint": "sha-256-fingerprint",
  "offerSdp": "v=0...",
  "expiresAtUnixMs": 1760000000000
}
```

## Confirm Payload (`PairingConfirmPayload`)

```json
{
  "version": "2",
  "phase": "confirm",
  "sessionId": "uuid",
  "receiverDeviceName": "iphone-15",
  "receiverPubKeyFingerprint": "sha-256-fingerprint",
  "answerSdp": "v=0...",
  "expiresAtUnixMs": 1760000000000
}
```

## Transport String Encoding

- Canonical payload is JSON (`PairingInitPayload` / `PairingConfirmPayload`).
- Implementations may emit a compressed transport string:
  - Prefix: `p2paudio-z1:`
  - Body: `zlib(json-bytes)` encoded as Base64URL without padding.
- Decoder accepts:
  - raw JSON payload
  - compressed transport string with the prefix above
- Payloads with `version != "2"` or invalid `phase` are rejected.

## Verification Code

- Compute SHA-256 over: `sessionId + "|" + senderFingerprint + "|" + receiverFingerprint`.
- Convert the first 4 digest bytes to a number and format as 6 digits (`000000`-`999999`).
- If user reports mismatch, both devices must discard session and restart from Step 1.

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
- `sessionId` must match between init and confirm payloads.
- Reject invalid version, phase, and expired payload.
- Use host ICE candidates only (LAN scope, including USB tethering IP links).
- Audio transport uses DataChannel only; no relay/signaling server.

## USB Tethering Path (Windows <-> Mobile)

- USB is treated as an IP network path, not as direct accessory communication.
- Supported path examples:
  - Android USB tethering to Windows.
  - iPhone Personal Hotspot over USB to Windows.
- Protocol payloads and verification rules are unchanged on USB.
