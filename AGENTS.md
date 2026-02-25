# Repository Guidelines

## Project Structure & Module Organization
- `docs/` contains protocol and validation references (`PROTOCOL.md`, `STATE_MACHINE.md`, `TEST_MATRIX.md`, JSON schemas).
- `mobile-android/` is the Android app (Kotlin + Compose + WebRTC). Main code lives under `app/src/main/java/com/example/p2paudio/` with feature folders like `capture/`, `protocol/`, `ui/`, and `webrtc/`.
- `mobile-ios/` is the iOS app skeleton (Swift + ReplayKit + WebRTC) split between `App/` and `AudioBroadcastExtension/`.
- Do not commit generated artifacts (for example `mobile-android/.gradle/` and `mobile-android/app/build/` outputs).

## Build, Test, and Development Commands
- Android (from `mobile-android/`):
  - `.\gradlew.bat assembleDebug` builds a debug APK.
  - `.\gradlew.bat testDebugUnitTest` runs JVM unit tests.
  - `.\gradlew.bat connectedDebugAndroidTest` runs device/emulator instrumentation tests.
  - `.\gradlew.bat lintDebug` runs Android lint checks.
- iOS:
  - Open `mobile-ios/` in Xcode and wire targets as described in `mobile-ios/README.md`.
  - After project setup, use Xcode build/test or `xcodebuild` with your local scheme.

## Coding Style & Naming Conventions
- Use 4-space indentation for Kotlin and Swift.
- Types/protocols: `UpperCamelCase`; methods/properties/variables: `lowerCamelCase`; constants: `UPPER_SNAKE_CASE` only when truly constant.
- Keep packages/namespaces aligned to feature areas (for example `...protocol.QrPayloadCodec`).
- Keep payload field names and semantics consistent with `docs/session-init.schema.json` and `docs/session-confirm.schema.json`.

## Testing Guidelines
- Android test stack is JUnit4 (`app/src/test`) plus AndroidX/Espresso/Compose instrumentation (`app/src/androidTest`).
- Name tests `*Test` (unit) and `*InstrumentedTest` (device), and mirror production package paths.
- Validate behavior against `docs/TEST_MATRIX.md` (functional, failure, and latency/session-survival targets).

## Commit & Pull Request Guidelines
- The repository currently has no commit history; use Conventional Commits going forward (for example `feat(android): add answer payload validation`).
- Keep commits scoped to one module (`docs`, `mobile-android`, or `mobile-ios`) when possible.
- PRs should include:
  - concise summary and rationale,
  - linked issue/task,
  - test evidence (commands run, emulator/device notes),
  - screenshots/video for UI or QR flow changes.

## Security & Configuration Tips
- Preserve the LAN-only, host-ICE, no-relay/no-signaling design documented in `docs/PROTOCOL.md`.
- Do not commit real SDP blobs, QR payload captures, or logs containing device identifiers/fingerprints.
