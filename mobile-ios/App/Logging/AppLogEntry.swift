import Foundation

enum AppLogLevel: String {
    case debug = "DEBUG"
    case info = "INFO"
    case warning = "WARN"
    case error = "ERROR"
}

struct AppLogEntry: Identifiable {
    let id = UUID()
    let timestamp: Date
    let level: AppLogLevel
    let category: String
    let message: String
    let metadata: [String: String]
}
