# Protocol

## Pairing Flow

### Fallback transport: out-of-band payload exchange

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

Windows and Android still accept these transport strings as text (copy/share/paste). Other clients may render the same transport string as QR if their UI still supports it.

### Primary Windows -> Android transport: connection code

1. Windows sender creates the usual `PairingInitPayload`, then starts a temporary LAN listener on a local host address.
2. Windows encodes that listener endpoint and a one-time token as a `p2paudio-c1:` connection code.
3. Android listener pastes the connection code, fetches the init payload directly from Windows, and validates it.
4. Android creates the SDP answer, computes the same 6-digit verification code, and posts the confirm payload back to Windows automatically.
5. Windows validates the confirm payload, applies the answer, and begins streaming without requiring the confirm payload to be pasted manually.

This connection code flow does not use any external relay or signaling server: the temporary listener runs on the Windows sender itself and is reachable only on the local network.

### Primary Windows -> Android transport for UDP + Opus: connection code

1. Windows sender creates a `UdpInitPayload`, then starts the same temporary LAN listener pattern used by WebRTC connection codes.
2. Windows encodes that listener endpoint and a one-time token as a `p2paudio-c1:` connection code.
3. Android listener pastes the connection code, fetches the UDP init payload directly from Windows, validates it, and opens its local UDP receive socket.
4. Android posts a `UdpConfirmPayload` back to Windows automatically.
5. Windows uses the incoming POST source address together with the confirmed UDP port and begins streaming immediately.

This UDP + Opus connection code flow also avoids external relay/signaling services and does not require mDNS receiver discovery.

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

## UDP Init Payload (`UdpInitPayload`)

```json
{
  "version": "2",
  "phase": "udp_init",
  "transport": "udp_opus",
  "sessionId": "uuid",
  "senderDeviceName": "windows-pc",
  "expiresAtUnixMs": 1760000000000
}
```

## UDP Confirm Payload (`UdpConfirmPayload`)

```json
{
  "version": "2",
  "phase": "udp_confirm",
  "transport": "udp_opus",
  "sessionId": "uuid",
  "receiverDeviceName": "pixel-8",
  "receiverPort": 49152,
  "expiresAtUnixMs": 1760000000000
}
```

## Transport String Encoding

- Canonical payload is JSON (`PairingInitPayload` / `PairingConfirmPayload` / `UdpInitPayload` / `UdpConfirmPayload`).
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

- Windows and Android currently use a 10-minute payload / connection-code TTL.
- Receivers must reject expired payloads and expired connection codes.
- `sessionId` must match between init and confirm payloads.
- Reject invalid version, phase, and expired payload.
- Use host ICE candidates only (LAN scope, including USB tethering IP links).
- WebRTC audio transport uses DataChannel only; Windows -> Android UDP + Opus uses direct UDP after the same connection-code setup. Neither flow uses external relay/signaling servers.

## USB Tethering Path (Windows <-> Mobile)

- USB is treated as an IP network path, not as direct accessory communication.
- Supported path examples:
  - Android USB tethering to Windows.
  - iPhone Personal Hotspot over USB to Windows.
- Protocol payloads and verification rules are unchanged on USB.
