import Foundation

enum L10n {
    static func tr(_ key: String) -> String {
        NSLocalizedString(key, comment: "")
    }

    static func tr(_ key: String, _ args: CVarArg...) -> String {
        let format = NSLocalizedString(key, comment: "")
        return String(format: format, locale: Locale.current, arguments: args)
    }
}
