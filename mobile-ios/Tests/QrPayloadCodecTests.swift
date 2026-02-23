import Foundation
import XCTest
@testable import P2PAudio

final class QrPayloadCodecTests: XCTestCase {

    func testEncodeOfferCompressesAndDecodesLargePayload() throws {
        let payload = SessionOfferPayload(
            sessionId: "session-1",
            senderDeviceName: "iphone",
            senderPubKeyFingerprint: "fp",
            offerSdp: "v=0\n" + String(repeating: "a=candidate:1 1 UDP 12345 192.168.0.10 5000 typ host\n", count: 120),
            expiresAtUnixMs: 1_760_000_000_000
        )

        let encoded = try QrPayloadCodec.encodeOffer(payload)
        XCTAssertTrue(encoded.hasPrefix("p2paudio-z1:"))

        let decoded = try QrPayloadCodec.decodeOffer(encoded)
        assertEqualOffer(decoded, payload)
    }

    func testDecodeOfferSupportsLegacyRawJson() throws {
        let payload = SessionOfferPayload(
            sessionId: "session-legacy",
            senderDeviceName: "iphone",
            senderPubKeyFingerprint: "fp",
            offerSdp: "v=0\na=fingerprint:sha-256 test\n",
            expiresAtUnixMs: 1_760_000_000_000
        )

        let data = try JSONEncoder().encode(payload)
        let raw = try XCTUnwrap(String(data: data, encoding: .utf8))

        let decoded = try QrPayloadCodec.decodeOffer(raw)
        assertEqualOffer(decoded, payload)
    }

    func testDecodeOfferRejectsEmptyCompressedPayload() {
        XCTAssertThrowsError(try QrPayloadCodec.decodeOffer("p2paudio-z1:")) { error in
            guard let failure = error as? SessionFailure else {
                return XCTFail("Expected SessionFailure")
            }
            XCTAssertEqual(failure.code, .invalidPayload)
        }
    }

    func testDecodeOfferRejectsInvalidCompressedBase64() {
        XCTAssertThrowsError(try QrPayloadCodec.decodeOffer("p2paudio-z1:***invalid***")) { error in
            guard let failure = error as? SessionFailure else {
                return XCTFail("Expected SessionFailure")
            }
            XCTAssertEqual(failure.code, .invalidPayload)
        }
    }

    private func assertEqualOffer(_ lhs: SessionOfferPayload, _ rhs: SessionOfferPayload) {
        XCTAssertEqual(lhs.version, rhs.version)
        XCTAssertEqual(lhs.role, rhs.role)
        XCTAssertEqual(lhs.sessionId, rhs.sessionId)
        XCTAssertEqual(lhs.senderDeviceName, rhs.senderDeviceName)
        XCTAssertEqual(lhs.senderPubKeyFingerprint, rhs.senderPubKeyFingerprint)
        XCTAssertEqual(lhs.offerSdp, rhs.offerSdp)
        XCTAssertEqual(lhs.expiresAtUnixMs, rhs.expiresAtUnixMs)
    }
}
