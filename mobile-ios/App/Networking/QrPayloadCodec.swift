import Foundation

enum QrPayloadCodec {
    private static let encoder = JSONEncoder()
    private static let decoder = JSONDecoder()
    private static let compressedPrefix = "p2paudio-z1:"
    private static let minBytesForCompression = 256
    private static let maxDecompressedBytes = 512_000

    static func encodeOffer(_ payload: SessionOfferPayload) throws -> String {
        let data = try encoder.encode(payload)
        guard let raw = String(data: data, encoding: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Failed to encode offer payload")
        }
        return compressTransportIfBeneficial(raw, data: data)
    }

    static func encodeAnswer(_ payload: SessionAnswerPayload) throws -> String {
        let data = try encoder.encode(payload)
        guard let raw = String(data: data, encoding: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Failed to encode answer payload")
        }
        return compressTransportIfBeneficial(raw, data: data)
    }

    static func decodeOffer(_ raw: String) throws -> SessionOfferPayload {
        let normalized = try decodeTransportString(raw)
        guard let data = normalized.data(using: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Offer payload is not UTF-8")
        }
        return try decoder.decode(SessionOfferPayload.self, from: data)
    }

    static func decodeAnswer(_ raw: String) throws -> SessionAnswerPayload {
        let normalized = try decodeTransportString(raw)
        guard let data = normalized.data(using: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Answer payload is not UTF-8")
        }
        return try decoder.decode(SessionAnswerPayload.self, from: data)
    }

    private static func compressTransportIfBeneficial(_ raw: String, data: Data) -> String {
        if data.count < minBytesForCompression {
            return raw
        }
        guard #available(iOS 13.0, *) else {
            return raw
        }
        guard let compressed = try? data.compressed(using: .zlib) else {
            return raw
        }
        let encoded = makeBase64UrlSafe(compressed.base64EncodedString())
        if encoded.count >= raw.count {
            return raw
        }
        return "\(compressedPrefix)\(encoded)"
    }

    private static func decodeTransportString(_ raw: String) throws -> String {
        guard raw.hasPrefix(compressedPrefix) else {
            return raw
        }
        let encoded = String(raw.dropFirst(compressedPrefix.count))
        guard !encoded.isEmpty else {
            throw SessionFailure(code: .invalidPayload, message: "Compressed payload is empty")
        }

        let standardBase64 = makeBase64Standard(encoded)
        guard let compressedData = Data(base64Encoded: standardBase64) else {
            throw SessionFailure(code: .invalidPayload, message: "Compressed payload base64 is invalid")
        }
        guard #available(iOS 13.0, *) else {
            throw SessionFailure(code: .invalidPayload, message: "Compressed payload is unsupported on this iOS version")
        }
        guard let decompressed = try? compressedData.decompressed(using: .zlib),
              decompressed.count <= maxDecompressedBytes,
              let normalized = String(data: decompressed, encoding: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Failed to decode compressed payload")
        }
        return normalized
    }

    private static func makeBase64UrlSafe(_ base64: String) -> String {
        base64
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    private static func makeBase64Standard(_ urlSafe: String) -> String {
        var normalized = urlSafe
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")
        let remainder = normalized.count % 4
        if remainder > 0 {
            normalized += String(repeating: "=", count: 4 - remainder)
        }
        return normalized
    }
}
