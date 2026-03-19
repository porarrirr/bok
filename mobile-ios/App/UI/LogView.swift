import SwiftUI
import UIKit

struct LogView: View {
    @ObservedObject var logStore: LogStore
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationView {
            Group {
                if logStore.entries.isEmpty {
                    Text(L10n.tr("log.empty"))
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    List(logStore.entries.reversed()) { entry in
                        VStack(alignment: .leading, spacing: 4) {
                            HStack(spacing: 8) {
                                Text(entry.level.rawValue)
                                    .font(.caption.weight(.bold))
                                    .foregroundStyle(levelColor(entry.level))
                                Text(entry.category)
                                    .font(.caption.monospaced())
                                    .foregroundStyle(.secondary)
                                Spacer()
                                Text(entry.timestamp, style: .time)
                                    .font(.caption2)
                                    .foregroundStyle(.secondary)
                            }
                            Text(entry.message)
                                .font(.footnote)
                            if !entry.metadata.isEmpty {
                                Text(
                                    entry.metadata
                                        .sorted(by: { $0.key < $1.key })
                                        .map { "\($0.key)=\($0.value)" }
                                        .joined(separator: " ")
                                )
                                .font(.caption2.monospaced())
                                .foregroundStyle(.secondary)
                                .textSelection(.enabled)
                            }
                        }
                        .padding(.vertical, 2)
                    }
                }
            }
            .navigationTitle(L10n.tr("log.title"))
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button(L10n.tr("action.close")) {
                        dismiss()
                    }
                }
                ToolbarItemGroup(placement: .navigationBarTrailing) {
                    Button(L10n.tr("log.copy_all")) {
                        UIPasteboard.general.string = logStore.exportText()
                    }
                    Button(L10n.tr("log.clear")) {
                        logStore.clear()
                    }
                }
            }
        }
    }

    private func levelColor(_ level: AppLogLevel) -> Color {
        switch level {
        case .debug:
            return .gray
        case .info:
            return .blue
        case .warning:
            return .orange
        case .error:
            return .red
        }
    }
}
