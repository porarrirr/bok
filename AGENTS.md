  a# Repository Guidelines

  ## Project Structure & Module Organization
  - `docs/` contains protocol and validation references (`PROTOCOL.md`, `STATE_MACHINE.md`, `TEST_MATRIX.md`, JSON schemas).
  - `mobile-android/` is the Android app (Kotlin + Compose + WebRTC). Main code lives under `app/src/main/java/com/example/p2paudio/` with feature folders like `capture/`, `protocol/`, `ui/`, and `webrtc/`.
  - `mobile-ios/` is the iOS app (Swift + ReplayKit + WebRTC) split between `App/` and `AudioBroadcastExtension/`.
  - `desktop-windows/` is the Windows app (WinUI + .NET 8 + native WebRTC bridge) with `src/P2PAudio.Windows.Core/` (protocol/logic) and `src/P2PAudio.Windows.App/` (UI).
  - Do not commit generated artifacts (e.g., `mobile-android/.gradle/`, `mobile-android/app/build/`, `desktop-windows/out/`, `desktop-windows/vcpkg_installed/`).

  ## Build, Test, and Development Commands

  ### Android (from `mobile-android/`)
  - Build debug APK:
    - `.\gradlew.bat assembleDebug`
  - Run all JVM unit tests:
    - `.\gradlew.bat testDebugUnitTest`
  - Run a single test class:
    - `.\gradlew.bat testDebugUnitTest --tests "com.example.p2paudio.protocol.QrPayloadCodecTest"`
  - Run a single test method:
    - `.\gradlew.bat testDebugUnitTest --tests "com.example.p2paudio.protocol.QrPayloadCodecTest.encodeInit compresses and decodes large payload"`
  - Run instrumented tests (requires device/emulator):
    - `.\gradlew.bat connectedDebugAndroidTest`
  - Run lint:
    - `.\gradlew.bat lintDebug`
  - Clean build:
    - `.\gradlew.bat clean assembleDebug`

  ### Windows (from `desktop-windows/`)
  - Build native bridge (requires vcpkg, cmake):
    - `cmake -S src/core-webrtc -B out/core-webrtc -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake -DVCPKG_TARGET_TRIPLET=x64-windows -DP2PAUDIO_USE_LIBDATACHANNEL=ON`
    - `cmake --build out/core-webrtc`
  - Build managed app:
    - `dotnet build src/P2PAudio.Windows.App/P2PAudio.Windows.App.csproj -c Release -r win10-x64 -p:Platform=x64`
  - Run all tests:
    - `dotnet test`
  - Run a single test class:
    - `dotnet test --filter "FullyQualifiedName~QrPayloadCodecTests"`
  - Run a single test method:
    - `dotnet test --filter "FullyQualifiedName~QrPayloadCodecTests.EncodeDecodeInit_RoundTrip"`
  - Run tests with verbose output:
    - `dotnet test -v n`

  ### iOS (from `mobile-ios/`)
  - Open in Xcode and use Xcode build/test commands, or use `xcodebuild` with your scheme.
  - Build:
    - `xcodebuild -scheme P2PAudio -destination 'platform=iOS Simulator,name=iPhone 15' build`
  - Run tests:
    - `xcodebuild test -scheme P2PAudio -destination 'platform=iOS Simulator,name=iPhone 15'`
  - Run a single test class:
    - `xcodebuild test -scheme P2PAudio -destination 'platform=iOS Simulator,name=iPhone 15' -only-testing:P2PAudioTests/QrPayloadCodecTests`
  - Run a single test method:
    - `xcodebuild test -scheme P2PAudio -destination 'platform=iOS Simulator,name=iPhone 15' -only-testing:P2PAudioTests/QrPayloadCodecTests/testEncodeInitCompressesAndDecodesLargePayload`
  - Generate project from `project.yml` (optional):
    - `xcodegen generate`

  ## Coding Style & Naming Conventions

  ### General
  - Use 4-space indentation for Kotlin, Swift, and C#.
  - No trailing whitespace; files end with a newline.
  - Avoid unnecessary comments; prefer self-documenting code.

  ### Kotlin (Android)
  - Types/interfaces: `UpperCamelCase`; methods/properties/variables: `lowerCamelCase`.
  - Constants in companion objects: `UPPER_SNAKE_CASE` for truly constant values (e.g., `private const val MAX_DECOMPRESSED_BYTES = 512_000`).
  - Use `object` for singletons (e.g., `object QrPayloadCodec`, `object PairingPayloadValidator`).
  - Data classes for DTOs/models with `@Serializable` annotation for JSON payloads.
  - Prefer `Result<T>` and `runCatching { }` for error handling; return `SessionFailure?` for validation results.
  - Use `require()` for argument validation; throw `IllegalArgumentException` for invalid input.
  - Imports: alphabetized, grouped (Android SDK, Kotlin, third-party, project packages).
  - Sealed interfaces for restricted type hierarchies (e.g., `sealed interface UiCommand`).
  - Underscores in numeric literals for readability (e.g., `512_000`, `60_000L`).

  ### Swift (iOS)
  - Types/protocols: `UpperCamelCase`; methods/properties/variables: `lowerCamelCase`.
  - Use `enum` for namespaces for static methods (e.g., `enum QrPayloadCodec { static func ... }`).
  - Structs for value types; classes for reference types with identity.
  - Use `throws` for error handling; throw typed errors (e.g., `SessionFailure`).
  - Private constants: `private static let` at type level (e.g., `private static let compressedPrefix = "p2paudio-z1:"`).
  - Test methods: `test` prefix followed by descriptive name (e.g., `testEncodeInitCompressesAndDecodesLargePayload`).

  ### C# (Windows)
  - Types: `UpperCamelCase`; public members: `PascalCase`; private fields: `_camelCase` or `camelCase`.
  - Use `sealed record` for immutable DTOs with `PropertyName` JSON attributes.
  - Static classes for pure utility functions (e.g., `public static class QrPayloadCodec`).
  - Use nullable reference types (`?`) and null-conditional operators.
  - Throw `SessionFailure` for domain-specific errors; use `throw new SessionFailure(FailureCode.InvalidPayload, "message")`.
  - Private constants: `private const string/readonly` (e.g., `private const string CompressedPrefix = "p2paudio-z1:"`).
  - Test naming: `PascalCase_Method_Scenario` (e.g., `EncodeDecodeInit_RoundTrip`).

  ### Cross-Platform Consistency
  - Keep payload field names and semantics consistent with `docs/session-init.schema.json` and `docs/session-confirm.schema.json`.
  - Enum values: `SCREAMING_SNAKE_CASE` in Kotlin (e.g., `INVALID_PAYLOAD`), `PascalCase` in C# (e.g., `InvalidPayload`), `camelCase` in Swift (e.g., `.invalidPayload`).
  - QR prefix constant: `p2paudio-z1:` must be identical across all platforms.

  ## Error Handling Patterns
  - Domain errors use `SessionFailure` with a `FailureCode` enum and message.
  - Validation functions return `SessionFailure?` (null = success) for early exit patterns.
  - Codec functions throw exceptions on decode failure (wrapping in `SessionFailure` where appropriate).
  - Use typed `Result.onSuccess { }.onFailure { }` in Kotlin for operation chains.
  - Catch and wrap low-level exceptions (e.g., `IllegalArgumentException` from base64 decode) into domain errors.

  ## Testing Guidelines
  - Android: JUnit4 for unit tests (`app/src/test`), AndroidX/Espresso/Compose for instrumentation (`app/src/androidTest`).
  - Windows: xUnit with `[Fact]` attributes; use `GlobalUsings.cs` for `global using Xunit;`.
  - iOS: XCTest framework; test files under `Tests/` directory.
  - Test naming: descriptive sentences with backticks in Kotlin (e.g., ``fun `encodeInit compresses and decodes large payload`()``), `PascalCase_Method_Scenario` in C# (e.g., `EncodeDecodeInit_RoundTrip`).
  - Mirror production package paths in test directories.
  - Validate behavior against `docs/TEST_MATRIX.md` (functional, failure, and latency/session-survival targets).
  - Use helper methods for repetitive assertions (e.g., `assertEqualInit` in Swift tests).

  ## Commit & Pull Request Guidelines
  - Use Conventional Commits (e.g., `feat(android): add answer payload validation`, `fix(windows): handle empty compressed payload`, `docs: update protocol spec`).
  - Keep commits scoped to one module (`docs`, `mobile-android`, `mobile-ios`, `desktop-windows`) when possible.
  - PRs should include:
    - concise summary and rationale,
    - linked issue/task,
    - test evidence (commands run, emulator/device notes),
    - screenshots/video for UI or QR flow changes.

  ## Security & Configuration
  - Preserve the LAN-only, host-ICE, no-relay/no-signaling design documented in `docs/PROTOCOL.md`.
  - Do not commit real SDP blobs, QR payload captures, or logs containing device identifiers/fingerprints.
  - Do not commit secrets (API keys, certificates) or `.env` files.
  - Native bridge load failure blocks connection flow; stub mode (`ALLOW_STUB_FOR_DEV=1`) is development-only.

  ## Architecture Overview
  - Session flow: QR code exchange → SDP offer/answer → WebRTC DataChannel (`audio-pcm`) → PCM audio streaming.
  - Sender captures audio (Android: `AudioPlaybackCapture`, iOS: ReplayKit, Windows: WASAPI loopback) → encodes to PCM frames → sends via DataChannel.
  - Receiver receives PCM frames → decodes → plays via platform audio API (Android: `AudioTrack`, iOS: `AVAudioEngine`, Windows: NAudio).
  - QR payload compression (`p2paudio-z1:` prefix) uses zlib + Base64URL encoding for large SDPs.
  - Verification code derived from session ID and public key fingerprints for manual peer verification.

  ## Key Files to Reference
  - `docs/PROTOCOL.md`: v2 pairing protocol specification.
  - `docs/session-init.schema.json` and `docs/session-confirm.schema.json`: JSON schema for payloads.
  - `docs/TEST_MATRIX.md`: functional, failure, and latency targets.
  - `mobile-ios/project.yml`: XcodeGen project spec for iOS.

  ## Platform Requirements
  - Android: Android Studio Koala+, SDK 35, JDK 17, minSdk 29 (Android 10+ for `AudioPlaybackCapture`).
  - iOS: Xcode 15+, iOS 15+, Swift 5.9+, ReplayKit broadcast extension.
  - Windows: Visual Studio 2022 17.10+, .NET 8 SDK, Windows 10 2004+ (19041), vcpkg (manifest mode).