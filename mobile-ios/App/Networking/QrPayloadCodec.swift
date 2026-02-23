import Foundation

enum QrPayloadCodec {
    private static let encoder = JSONEncoder()
    private static let decoder = JSONDecoder()

    static func encodeOffer(_ payload: SessionOfferPayload) throws -> String {
        let data = try encoder.encode(payload)
        guard let string = String(data: data, encoding: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Failed to encode offer payload")
        }
        return string
    }

    static func encodeAnswer(_ payload: SessionAnswerPayload) throws -> String {
        let data = try encoder.encode(payload)
        guard let string = String(data: data, encoding: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Failed to encode answer payload")
        }
        return string
    }

    static func decodeOffer(_ raw: String) throws -> SessionOfferPayload {
        guard let data = raw.data(using: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Offer payload is not UTF-8")
        }
        return try decoder.decode(SessionOfferPayload.self, from: data)
    }

    static func decodeAnswer(_ raw: String) throws -> SessionAnswerPayload {
        guard let data = raw.data(using: .utf8) else {
            throw SessionFailure(code: .invalidPayload, message: "Answer payload is not UTF-8")
        }
        return try decoder.decode(SessionAnswerPayload.self, from: data)
    }
}
