# core-webrtc (C++ native bridge)

This folder provides the native bridge contract for Windows WebRTC session control.

Current state:

- Session controller C++ API is implemented.
- C ABI export layer is implemented for .NET P/Invoke.
- Build links `libdatachannel` when available in the toolchain.
- Offer/answer generation and apply-answer flow are wired.
- `audio-pcm` DataChannel send/receive is wired through the C ABI.
- ICE diagnostics (host candidate count, selected pair type, failure hints) are surfaced to managed code.

Expected responsibilities:

- Create offer/answer with host ICE only.
- Apply remote answer.
- Open/bind `audio-pcm` DataChannel.
- Surface ICE candidate diagnostics and selected pair type.
