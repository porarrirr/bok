# P2P Device Audio Relay

This repository contains a native Android + iOS implementation for relaying device audio without relay servers.

## Structure

- `docs/`: protocol, constraints, and test matrix.
- `mobile-android/`: Android sender/receiver implementation (Kotlin).
- `mobile-ios/`: iOS sender/receiver implementation (Swift + ReplayKit extension).

## Constraints

- No media relay server and no signaling server.
- LAN-only connection using host ICE candidates.
- 1:1 session via QR exchange.
- QR payload transport supports compressed mode (`p2paudio-z1:` zlib + Base64URL).
- iOS sender can capture ReplayKit app audio only (not system-wide sounds).
