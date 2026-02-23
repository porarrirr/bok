# Known Limitations

1. Large SDP payloads can exceed one QR symbol capacity in some environments.
2. iOS sender can capture ReplayKit app audio only; ringtones/notifications/system-wide audio are not capturable by third-party apps.
3. Internet/NAT traversal is intentionally unsupported by design.
4. Android capture availability depends on source app policy (`allowAudioPlaybackCapture`).
5. ReplayKit extension-to-app bridge currently uses shared App Group file transport; frame drops can increase on heavily loaded devices.
