# Known Limitations

1. QR payload compression is enabled, but very large SDPs can still exceed one QR symbol in strict camera environments.
2. iOS sender can capture ReplayKit app audio only; ringtones/notifications/system-wide audio are not capturable by third-party apps.
3. Android capture availability depends on source app policy (`allowAudioPlaybackCapture`) and OS capture restrictions.
4. Internet/NAT traversal is intentionally unsupported by design (LAN host ICE only).
5. ReplayKit extension-to-app bridge currently uses shared App Group file transport; frame drops can increase on heavily loaded devices.
