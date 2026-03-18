# P2P Device Audio Relay

This repository contains native Android + iOS implementations for relaying device audio without relay servers.
It also includes a Windows implementation for the same v2 protocol.

## Structure

- `docs/`: protocol, constraints, and test matrix.
- `mobile-android/`: Android sender/receiver implementation (Kotlin).
- `mobile-ios/`: iOS sender/receiver implementation (Swift + ReplayKit extension).
- `desktop-windows/`: Windows implementation (WinUI + core protocol/audio logic + native WebRTC bridge).

## Constraints

- No media relay server and no signaling server.
- LAN-only connection using host ICE candidates.
- USB tethering is supported as a LAN-equivalent IP path (Android USB tethering / iPhone Personal Hotspot over USB).
- 1:1 session via out-of-band payload exchange.
- Payload transport supports compressed mode (`p2paudio-z1:` zlib + Base64URL).
- iOS sender can capture ReplayKit app audio only (not system-wide sounds).
