import Foundation

@MainActor
final class LogStore: ObservableObject {
    @Published private(set) var entries: [AppLogEntry] = []

    private let maxEntries: Int
    private static let timestampFormatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter
    }()

    init(maxEntries: Int = 1000) {
        self.maxEntries = maxEntries
    }

    func append(
        level: AppLogLevel,
        category: String,
        message: String,
        metadata: [String: String] = [:]
    ) {
        entries.append(
            AppLogEntry(
                timestamp: Date(),
                level: level,
                category: category,
                message: message,
                metadata: metadata
            )
        )

        if entries.count > maxEntries {
            entries.removeFirst(entries.count - maxEntries)
        }
    }

    func clear() {
        entries.removeAll()
    }

    func exportText() -> String {
        entries.map { entry in
            let timestamp = Self.timestampFormatter.string(from: entry.timestamp)
            let metadata = entry.metadata
                .sorted(by: { $0.key < $1.key })
                .map { "\($0.key)=\($0.value)" }
                .joined(separator: " ")
            if metadata.isEmpty {
                return "[\(timestamp)] [\(entry.level.rawValue)] [\(entry.category)] \(entry.message)"
            }
            return "[\(timestamp)] [\(entry.level.rawValue)] [\(entry.category)] \(entry.message) \(metadata)"
        }.joined(separator: "\n")
    }
}
