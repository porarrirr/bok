import CoreImage.CIFilterBuiltins
import SwiftUI
import UIKit

struct ContentView: View {
    @StateObject private var viewModel = AppViewModel()
    @State private var scanTarget: ScanTarget?
    @State private var transientMessage: String?
    @State private var showingLogs = false

    var body: some View {
        ZStack(alignment: .top) {
            LinearGradient(
                colors: [
                    Color(red: 0.93, green: 0.96, blue: 1.00),
                    Color(red: 1.00, green: 0.95, blue: 0.90)
                ],
                startPoint: .topLeading,
                endPoint: .bottomTrailing
            )
            .ignoresSafeArea()

            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    headerBlock
                    statusCard
                    entryCard

                    if let transientMessage {
                        Text(transientMessage)
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(Color(red: 0.05, green: 0.36, blue: 0.49))
                            .padding(.horizontal, 4)
                            .transition(.opacity.combined(with: .move(edge: .top)))
                    }

                    flowSection

                    Text(L10n.tr("main.capture_hint"))
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
                .padding(16)
            }
        }
        .sheet(item: $scanTarget) { target in
            QrScannerView(
                onScanned: { payload in
                    switch target {
                    case .initPayload:
                        viewModel.createConfirm(from: payload)
                    case .confirmPayload:
                        viewModel.prepareConfirmForVerification(from: payload)
                    }
                    scanTarget = nil
                },
                onCancel: {
                    scanTarget = nil
                }
            )
            .ignoresSafeArea()
        }
        .sheet(isPresented: $showingLogs) {
            LogView(logStore: viewModel.logStore)
        }
    }

    private var headerBlock: some View {
        HStack(alignment: .top) {
            VStack(alignment: .leading, spacing: 6) {
                Text(L10n.tr("main.title"))
                    .font(.system(size: 32, weight: .heavy, design: .rounded))
                    .foregroundStyle(Color(red: 0.05, green: 0.17, blue: 0.28))
                Text(L10n.tr("main.subtitle"))
                    .font(.subheadline)
                    .foregroundStyle(Color(red: 0.24, green: 0.34, blue: 0.42))
            }
            Spacer(minLength: 12)
            Button(L10n.tr("action.open_logs")) {
                showingLogs = true
            }
            .buttonStyle(SecondaryActionButtonStyle())
            .frame(width: 88)
        }
    }

    private var statusCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(L10n.tr("status.connection_title"))
                .font(.headline)

            Text(viewModel.streamState.readableLabel)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(viewModel.streamState.themeColor)

            Text(viewModel.statusMessage)
                .font(.subheadline)

            if !viewModel.activeSessionId.isEmpty {
                Text(L10n.tr("status.session_id"))
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Text(viewModel.activeSessionId)
                    .font(.caption.monospaced())
                    .lineLimit(1)
                    .textSelection(.enabled)
            }

            if viewModel.connectionDiagnostics.hasContent {
                Divider().padding(.top, 2)
                Text(L10n.tr("status.network_path_title"))
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                Text(viewModel.connectionDiagnostics.pathType.readableLabel)
                    .font(.subheadline)
                Text(
                    L10n.tr(
                        "status.local_candidates_format",
                        viewModel.connectionDiagnostics.localCandidatesCount
                    )
                )
                .font(.caption)
                .foregroundStyle(.secondary)
                if !viewModel.connectionDiagnostics.selectedCandidatePairType.isEmpty {
                    Text(
                        L10n.tr(
                            "status.selected_pair_format",
                            viewModel.connectionDiagnostics.selectedCandidatePairType
                        )
                    )
                    .font(.caption)
                    .foregroundStyle(.secondary)
                }
                if !viewModel.connectionDiagnostics.failureHint.isEmpty {
                    Text(
                        L10n.tr(
                            "status.failure_hint_format",
                            viewModel.connectionDiagnostics.localizedHint
                        )
                    )
                    .font(.caption)
                    .foregroundStyle(Color(red: 0.74, green: 0.18, blue: 0.22))
                }
            }

            Divider().padding(.top, 2)
            Text(L10n.tr("status.next_action_title"))
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)
            Text(recommendedActionText)
                .font(.subheadline)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(Color.white.opacity(0.92), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(Color.black.opacity(0.06), lineWidth: 1)
        )
    }

    private var entryCard: some View {
        StepCard(
            number: 1,
            title: L10n.tr("flow.entry.title"),
            description: L10n.tr("flow.entry.description")
        ) {
            HStack(spacing: 10) {
                Button(L10n.tr("action.start_sender")) {
                    viewModel.startSenderFlow()
                }
                .buttonStyle(PrimaryActionButtonStyle())

                Button(L10n.tr("action.start_listener_scan")) {
                    viewModel.beginListenerFlow()
                    scanTarget = .initPayload
                }
                .buttonStyle(PrimaryActionButtonStyle())
            }

            Button(L10n.tr("action.stop_session")) {
                viewModel.endSession()
            }
            .buttonStyle(SecondaryActionButtonStyle())
        }
    }

    @ViewBuilder
    private var flowSection: some View {
        switch viewModel.setupStep {
        case .entry:
            EmptyView()
        case .senderShowInit:
            StepCard(
                number: 2,
                title: L10n.tr("flow.sender.step_title"),
                description: L10n.tr("flow.sender.step_description")
            ) {
                if viewModel.initPayloadRaw.isEmpty {
                    Text(L10n.tr("flow.sender.waiting_code"))
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                } else {
                    ConnectionCodePanel(
                        payloadTitle: L10n.tr("flow.sender.payload_title"),
                        payloadValue: viewModel.initPayloadRaw,
                        payloadQrDescription: L10n.tr("flow.sender.qr_description"),
                        onCopyPayload: {
                            UIPasteboard.general.string = viewModel.initPayloadRaw
                            showTransientMessage(L10n.tr("toast.init_payload_copied"))
                        }
                    )
                }

                Button(L10n.tr("flow.sender.scan_button")) {
                    scanTarget = .confirmPayload
                }
                .buttonStyle(SecondaryActionButtonStyle())
                .disabled(viewModel.initPayloadRaw.isEmpty)
                .opacity(viewModel.initPayloadRaw.isEmpty ? 0.6 : 1)
            }
        case .senderVerifyCode:
            StepCard(
                number: 3,
                title: L10n.tr("flow.verification.title"),
                description: L10n.tr("flow.verification.description")
            ) {
                VerificationCodeBlock(code: viewModel.verificationCode)
                HStack(spacing: 10) {
                    Button(L10n.tr("flow.verification.match")) {
                        viewModel.approveVerificationAndConnect()
                    }
                    .buttonStyle(PrimaryActionButtonStyle())

                    Button(L10n.tr("flow.verification.mismatch")) {
                        viewModel.rejectVerificationAndRestart()
                    }
                    .buttonStyle(SecondaryActionButtonStyle())
                }
            }
        case .listenerScanInit:
            StepCard(
                number: 2,
                title: L10n.tr("flow.receiver.step_title"),
                description: L10n.tr("flow.receiver.step_description")
            ) {
                Text(L10n.tr("flow.receiver.waiting_code"))
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        case .listenerShowConfirm:
            StepCard(
                number: 3,
                title: L10n.tr("flow.receiver.confirm_title"),
                description: L10n.tr("flow.receiver.confirm_description")
            ) {
                ConnectionCodePanel(
                    payloadTitle: L10n.tr("flow.receiver.payload_title"),
                    payloadValue: viewModel.confirmPayloadRaw,
                    payloadQrDescription: L10n.tr("flow.receiver.qr_description"),
                    onCopyPayload: {
                        UIPasteboard.general.string = viewModel.confirmPayloadRaw
                        showTransientMessage(L10n.tr("toast.confirm_payload_copied"))
                    }
                )
                VerificationCodeBlock(code: viewModel.verificationCode)
            }
        }
    }

    private var recommendedActionText: String {
        if viewModel.needsBroadcastStartHint {
            return L10n.tr("status.next_action_start_broadcast")
        }
        if viewModel.streamState == .streaming {
            return L10n.tr("status.next_action_connected")
        }
        if viewModel.streamState == .failed {
            switch viewModel.connectionDiagnostics.pathType {
            case .usbTether:
                if viewModel.connectionDiagnostics.localCandidatesCount == 0 {
                    return L10n.tr("status.next_action_usb_enable_tethering")
                }
                return L10n.tr("status.next_action_usb_replug")
            case .wifiLan, .unknown:
                if viewModel.connectionDiagnostics.localCandidatesCount == 0 {
                    return L10n.tr("status.next_action_check_interface")
                }
                return L10n.tr("status.next_action_restart")
            }
        }
        if viewModel.connectionDiagnostics.pathType == .usbTether &&
            viewModel.streamState == .connecting &&
            viewModel.connectionDiagnostics.localCandidatesCount == 0 {
            return L10n.tr("status.next_action_usb_enable_tethering")
        }
        switch viewModel.setupStep {
        case .entry:
            return L10n.tr("status.next_action_entry")
        case .senderShowInit:
            return L10n.tr("status.next_action_show_init")
        case .senderVerifyCode:
            return L10n.tr("status.next_action_verify")
        case .listenerScanInit:
            return L10n.tr("status.next_action_scan_init")
        case .listenerShowConfirm:
            return L10n.tr("status.next_action_show_confirm")
        }
    }

    private func showTransientMessage(_ message: String) {
        withAnimation(.easeOut(duration: 0.15)) {
            transientMessage = message
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.8) {
            withAnimation(.easeIn(duration: 0.2)) {
                transientMessage = nil
            }
        }
    }

    private enum ScanTarget: String, Identifiable {
        case initPayload
        case confirmPayload

        var id: String { rawValue }
    }
}

private struct StepCard<Content: View>: View {
    let number: Int
    let title: String
    let description: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                Text("\(number)")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(Color(red: 0.02, green: 0.35, blue: 0.49))
                    .frame(width: 24, height: 24)
                    .background(
                        RoundedRectangle(cornerRadius: 7, style: .continuous)
                            .fill(Color(red: 0.84, green: 0.93, blue: 1.00))
                    )
                Text(title)
                    .font(.title3.weight(.semibold))
                    .foregroundStyle(Color(red: 0.08, green: 0.21, blue: 0.28))
            }

            Text(description)
                .font(.subheadline)
                .foregroundStyle(Color(red: 0.30, green: 0.39, blue: 0.45))

            content
        }
        .padding(14)
        .background(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .fill(Color.white.opacity(0.92))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(Color.black.opacity(0.06), lineWidth: 1)
        )
    }
}

private struct VerificationCodeBlock: View {
    let code: String

    var body: some View {
        if code.isEmpty {
            EmptyView()
        } else {
            VStack(alignment: .leading, spacing: 6) {
                Text(L10n.tr("flow.verification.code_label"))
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(.secondary)
                Text(code)
                    .font(.system(size: 34, weight: .black, design: .rounded))
                    .foregroundStyle(Color(red: 0.05, green: 0.17, blue: 0.28))
            }
        }
    }
}

private struct ConnectionCodePanel: View {
    let payloadTitle: String
    let payloadValue: String
    let payloadQrDescription: String
    let onCopyPayload: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(payloadTitle)
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)

            ScrollView {
                Text(payloadValue)
                    .font(.caption.monospaced())
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled)
            }
            .frame(minHeight: 88, maxHeight: 140)
            .padding(10)
            .background(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(Color.white)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .stroke(Color.black.opacity(0.12), lineWidth: 1)
            )

            HStack {
                Button(L10n.tr("action.copy_payload"), action: onCopyPayload)
                    .font(.caption.weight(.semibold))
                Spacer()
                Text(L10n.tr("common.char_count_format", payloadValue.count))
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            HStack {
                Spacer()
                QRCodeImage(payload: payloadValue)
                    .frame(width: 220, height: 220)
                    .accessibilityLabel(Text(payloadQrDescription))
                Spacer()
            }
        }
    }
}

private struct PrimaryActionButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(maxWidth: .infinity, minHeight: 44)
            .padding(.vertical, 10)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.white)
            .background(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(Color(red: 0.02, green: 0.36, blue: 0.47))
            )
            .opacity(configuration.isPressed ? 0.85 : 1)
            .scaleEffect(configuration.isPressed ? 0.99 : 1)
    }
}

private struct SecondaryActionButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(maxWidth: .infinity, minHeight: 44)
            .padding(.vertical, 10)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(Color(red: 0.02, green: 0.36, blue: 0.47))
            .background(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .fill(Color.white.opacity(0.88))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 12, style: .continuous)
                    .stroke(Color(red: 0.02, green: 0.36, blue: 0.47).opacity(0.35), lineWidth: 1)
            )
            .opacity(configuration.isPressed ? 0.85 : 1)
            .scaleEffect(configuration.isPressed ? 0.99 : 1)
    }
}

private extension AudioStreamState {
    var readableLabel: String {
        switch self {
        case .idle: return L10n.tr("stream_state.idle")
        case .capturing: return L10n.tr("stream_state.capturing")
        case .connecting: return L10n.tr("stream_state.connecting")
        case .streaming: return L10n.tr("stream_state.streaming")
        case .interrupted: return L10n.tr("stream_state.interrupted")
        case .failed: return L10n.tr("stream_state.failed")
        case .ended: return L10n.tr("stream_state.ended")
        }
    }

    var themeColor: Color {
        switch self {
        case .streaming:
            return Color(red: 0.10, green: 0.50, blue: 0.20)
        case .failed:
            return Color(red: 0.74, green: 0.18, blue: 0.22)
        case .capturing, .connecting:
            return Color(red: 0.85, green: 0.47, blue: 0.08)
        default:
            return Color(red: 0.33, green: 0.38, blue: 0.42)
        }
    }
}

private extension ConnectionDiagnostics {
    var hasContent: Bool {
        pathType != .unknown ||
            localCandidatesCount > 0 ||
            !selectedCandidatePairType.isEmpty ||
            !failureHint.isEmpty
    }

    var localizedHint: String {
        switch failureHint {
        case "usb_tether_check":
            return L10n.tr("status.hint_usb_tether_check")
        case "wifi_lan_check":
            return L10n.tr("status.hint_wifi_check")
        case "network_interface_check":
            return L10n.tr("status.hint_network_interface_check")
        case "peer_disconnected":
            return L10n.tr("status.peer_disconnected")
        default:
            return failureHint
        }
    }
}

private extension NetworkPathType {
    var readableLabel: String {
        switch self {
        case .wifiLan:
            return L10n.tr("status.network_path_wifi")
        case .usbTether:
            return L10n.tr("status.network_path_usb")
        case .unknown:
            return L10n.tr("status.network_path_unknown")
        }
    }
}

private struct QRCodeImage: View {
    private let payload: String
    private let context = CIContext()
    private let filter = CIFilter.qrCodeGenerator()

    init(payload: String) {
        self.payload = payload
    }

    var body: some View {
        if let image = generateImage() {
            Image(uiImage: image)
                .resizable()
                .interpolation(.none)
                .scaledToFit()
        } else {
            Color.gray
        }
    }

    private func generateImage() -> UIImage? {
        let data = Data(payload.utf8)
        filter.setValue(data, forKey: "inputMessage")
        filter.setValue("M", forKey: "inputCorrectionLevel")
        guard let outputImage = filter.outputImage else {
            return nil
        }

        let transformed = outputImage.transformed(by: CGAffineTransform(scaleX: 10, y: 10))
        guard let cgImage = context.createCGImage(transformed, from: transformed.extent) else {
            return nil
        }

        return UIImage(cgImage: cgImage)
    }
}
