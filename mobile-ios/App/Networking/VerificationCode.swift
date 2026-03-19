import CryptoKit
import Foundation

enum VerificationCode {
    static func fromSessionAndFingerprints(
        sessionId: String,
        senderFingerprint: String,
        receiverFingerprint: String
    ) -> String {
        let source = "\(sessionId)|\(senderFingerprint)|\(receiverFingerprint)"
        let digest = SHA256.hash(data: Data(source.utf8))
        let bytes = Array(digest.prefix(4))
        let numeric = (UInt32(bytes[0]) << 24) |
            (UInt32(bytes[1]) << 16) |
            (UInt32(bytes[2]) << 8) |
            UInt32(bytes[3])
        let value = numeric % 1_000_000
        return String(format: "%06u", value)
    }
}
