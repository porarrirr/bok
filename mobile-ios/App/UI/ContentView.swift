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
            AppTheme.backgroundGradient
                .ignoresSafeArea()

            ScrollView(showsIndicators: false) {
                VStack(alignment: .leading, spacing: 18) {
                    headerBlock
                    statusCard
                    entryCard

                    if let transientMessage {
                        InfoCallout(
                            message: transientMessage,
                            tint: AppTheme.accent,
                            iconName: "checkmark.circle.fill"
                        )
                        .transition(.opacity.combined(with: .move(edge: .top)))
                    }

                    flowSection
                }
                .padding(.horizontal, 16)
                .padding(.top, 14)
                .padding(.bottom, 28)
            }
        }
        .sheet(item: $scanTarget) { target in
            QrScannerView(
                onScanned: { payload in
                    switch target {
                    case .listenerInput:
                        viewModel.listenerInputRaw = payload
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
        HStack(alignment: .top, spacing: 16) {
            VStack(alignment: .leading, spacing: 8) {
                Text(L10n.tr("main.title"))
                    .font(.system(size: 32, weight: .heavy, design: .rounded))
                    .foregroundStyle(AppTheme.primaryText)
                Text(L10n.tr("main.subtitle"))
                    .font(.body)
                    .foregroundStyle(AppTheme.secondaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }

            Spacer(minLength: 12)

            Button {
                showingLogs = true
            } label: {
                VStack(spacing: 6) {
                    Image(systemName: "text.alignleft")
                        .font(.headline.weight(.semibold))
                    Text(L10n.tr("action.open_logs"))
                        .font(.subheadline.weight(.semibold))
                }
                .foregroundStyle(AppTheme.accent)
                .frame(width: 92, minHeight: 72)
                .background(
                    RoundedRectangle(cornerRadius: 18, style: .continuous)
                        .fill(Color.white.opacity(0.96))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 18, style: .continuous)
                        .stroke(AppTheme.accent.opacity(0.28), lineWidth: 1)
                )
            }
            .buttonStyle(.plain)
            .accessibilityLabel(Text(L10n.tr("action.open_logs")))
        }
    }

    private var statusCard: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .top, spacing: 12) {
                VStack(alignment: .leading, spacing: 8) {
                    Text(L10n.tr("status.connection_title"))
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(AppTheme.secondaryText)
                    Text(viewModel.streamState.readableLabel)
                        .font(.title3.weight(.bold))
                        .foregroundStyle(AppTheme.primaryText)
                    Text(viewModel.statusMessage)
                        .font(.body)
                        .foregroundStyle(AppTheme.primaryText)
                        .fixedSize(horizontal: false, vertical: true)
                }

                Spacer(minLength: 12)

                StateBadge(
                    title: viewModel.streamState.readableLabel,
                    systemImage: viewModel.streamState.symbolName,
                    tint: viewModel.streamState.themeColor
                )
            }

            if !viewModel.activeSessionId.isEmpty {
                PanelBlock(title: L10n.tr("status.session_id"), systemImage: "number") {
                    Text(viewModel.activeSessionId)
                        .font(.footnote.monospaced())
                        .foregroundStyle(AppTheme.primaryText)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .textSelection(.enabled)
                }
            }

            PanelBlock(
                title: L10n.tr("status.transport_mode_title"),
                systemImage: "point.3.connected.trianglepath.dotted"
            ) {
                Text(viewModel.transportMode.readableLabel)
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(AppTheme.primaryText)
            }

            if viewModel.connectionDiagnostics.hasContent {
                PanelBlock(title: L10n.tr("status.network_path_title"), systemImage: "wifi") {
                    Text(viewModel.connectionDiagnostics.pathType.readableLabel)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(AppTheme.primaryText)
                    Text(
                        L10n.tr(
                            "status.local_candidates_format",
                            viewModel.connectionDiagnostics.localCandidatesCount
                        )
                    )
                    .font(.footnote)
                    .foregroundStyle(AppTheme.secondaryText)
                    if !viewModel.connectionDiagnostics.selectedCandidatePairType.isEmpty {
                        Text(
                            L10n.tr(
                                "status.selected_pair_format",
                                viewModel.connectionDiagnostics.selectedCandidatePairType
                            )
                        )
                        .font(.footnote)
                        .foregroundStyle(AppTheme.secondaryText)
                    }
                    if !viewModel.connectionDiagnostics.failureHint.isEmpty {
                        InfoCallout(
                            message: viewModel.connectionDiagnostics.localizedHint,
                            tint: AppTheme.danger,
                            iconName: "exclamationmark.triangle.fill"
                        )
                    }
                }
            }

            if viewModel.audioStreamDiagnostics.hasContent() {
                PanelBlock(title: L10n.tr("audio_diagnostics.title"), systemImage: "waveform") {
                    Text(
                        L10n.tr(
                            "audio_diagnostics.source_format",
                            viewModel.audioStreamDiagnostics.source.readableLabel
                        )
                    )
                    .font(.footnote)
                    .foregroundStyle(AppTheme.primaryText)
                    Text(
                        L10n.tr(
                            "audio_diagnostics.format_format",
                            viewModel.audioStreamDiagnostics.sampleRate,
                            viewModel.audioStreamDiagnostics.channels,
                            viewModel.audioStreamDiagnostics.bitsPerSample
                        )
                    )
                    .font(.footnote)
                    .foregroundStyle(AppTheme.secondaryText)
                    Text(
                        L10n.tr(
                            "audio_diagnostics.queue_format",
                            viewModel.audioStreamDiagnostics.queueDepthFrames,
                            viewModel.audioStreamDiagnostics.maxQueueFrames,
                            viewModel.audioStreamDiagnostics.targetPrebufferFrames
                        )
                    )
                    .font(.footnote)
                    .foregroundStyle(AppTheme.secondaryText)
                    Text(
                        L10n.tr(
                            "audio_diagnostics.frames_format",
                            viewModel.audioStreamDiagnostics.playedFrames,
                            viewModel.audioStreamDiagnostics.decodedPackets
                        )
                    )
                    .font(.footnote)
                    .foregroundStyle(AppTheme.secondaryText)
                    Text(
                        L10n.tr(
                            "audio_diagnostics.drops_format",
                            viewModel.audioStreamDiagnostics.staleFrameDrops,
                            viewModel.audioStreamDiagnostics.queueOverflowDrops
                        )
                    )
                    .font(.footnote)
                    .foregroundStyle(AppTheme.secondaryText)
                }
            }

            InfoCallout(
                title: L10n.tr("status.next_action_title"),
                message: recommendedActionText,
                tint: AppTheme.accent,
                iconName: "lightbulb.fill"
            )
        }
        .padding(18)
        .appCardBackground()
    }

    private var entryCard: some View {
        StepCard(
            number: 1,
            title: L10n.tr("flow.entry.title"),
            description: L10n.tr("flow.entry.description")
        ) {
            VStack(alignment: .leading, spacing: 16) {
                VStack(alignment: .leading, spacing: 10) {
                    Text(L10n.tr("transport_mode_title"))
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(AppTheme.secondaryText)
                    TransportModeSelector(
                        selection: Binding(
                            get: { viewModel.transportMode },
                            set: { viewModel.selectTransportMode($0) }
                        )
                    )
                    Text(viewModel.transportMode.descriptionText)
                        .font(.footnote)
                        .foregroundStyle(AppTheme.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                    if viewModel.transportMode == .udpOpus {
                        InfoCallout(
                            message: L10n.tr("transport_mode_udp_sender_note"),
                            tint: AppTheme.danger,
                            iconName: "exclamationmark.circle.fill"
                        )
                    }
                }

                VStack(alignment: .leading, spacing: 10) {
                    Text(L10n.tr("receiver_latency_title"))
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(AppTheme.secondaryText)
                    LatencyPresetSelector(
                        selection: viewModel.receiverLatencyPreset,
                        onSelect: { preset in
                            viewModel.selectReceiverLatencyPreset(preset)
                        }
                    )
                    Text(
                        L10n.tr(
                            "receiver_latency_selected_format",
                            viewModel.receiverLatencyPreset.localizedLabel
                        )
                    )
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(AppTheme.primaryText)
                    Text(viewModel.receiverLatencyPreset.localizedDescription)
                        .font(.footnote)
                        .foregroundStyle(AppTheme.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                    Text(L10n.tr("receiver_latency_note"))
                        .font(.footnote)
                        .foregroundStyle(AppTheme.secondaryText)
                        .fixedSize(horizontal: false, vertical: true)
                }

                VStack(spacing: 10) {
                    Button(L10n.tr("action.start_sender")) {
                        viewModel.startSenderFlow()
                    }
                    .buttonStyle(PrimaryActionButtonStyle())

                    Button(L10n.tr("action.start_listener")) {
                        viewModel.beginListenerFlow()
                    }
                    .buttonStyle(PrimaryActionButtonStyle())

                    Button(L10n.tr("action.stop_session")) {
                        viewModel.endSession()
                    }
                    .buttonStyle(SecondaryActionButtonStyle())
                }
            }
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
                        .font(.body)
                        .foregroundStyle(AppTheme.secondaryText)
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
                    ExpiryStatusView(expiresAtUnixMs: viewModel.payloadExpiresAtUnixMs)
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
                VStack(spacing: 10) {
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
        case .listenerInput:
            StepCard(
                number: 2,
                title: viewModel.transportMode == .webRtc
                    ? L10n.tr("flow.listener_input.title")
                    : L10n.tr("flow.udp_listener_input.title"),
                description: viewModel.transportMode == .webRtc
                    ? L10n.tr("flow.listener_input.description")
                    : L10n.tr("flow.udp_listener_input.description")
            ) {
                PayloadInputPanel(
                    title: viewModel.transportMode == .webRtc
                        ? L10n.tr("flow.listener_input.payload_title")
                        : L10n.tr("flow.udp_listener_input.payload_title"),
                    placeholder: viewModel.transportMode == .webRtc
                        ? L10n.tr("flow.listener_input.placeholder")
                        : L10n.tr("flow.udp_listener_input.placeholder"),
                    text: $viewModel.listenerInputRaw
                )

                HStack(spacing: 10) {
                    Button(L10n.tr("action.paste_from_clipboard")) {
                        if let clipboard = UIPasteboard.general.string?.trimmingCharacters(in: .whitespacesAndNewlines),
                           !clipboard.isEmpty {
                            viewModel.listenerInputRaw = clipboard
                            showTransientMessage(L10n.tr("toast.listener_input_pasted"))
                        }
                    }
                    .buttonStyle(SecondaryActionButtonStyle())

                    Button(L10n.tr("action.scan_qr")) {
                        scanTarget = .listenerInput
                    }
                    .buttonStyle(SecondaryActionButtonStyle())
                }

                Button(
                    viewModel.transportMode == .webRtc
                        ? L10n.tr("flow.listener_input.apply")
                        : L10n.tr("flow.udp_listener_input.apply")
                ) {
                    viewModel.createConfirm(from: viewModel.listenerInputRaw)
                }
                .buttonStyle(PrimaryActionButtonStyle())
                .disabled(viewModel.listenerInputRaw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                .opacity(viewModel.listenerInputRaw.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? 0.6 : 1)
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
                ExpiryStatusView(expiresAtUnixMs: viewModel.payloadExpiresAtUnixMs)
            }
        case .listenerWaitForConnection:
            StepCard(
                number: 3,
                title: viewModel.transportMode == .webRtc
                    ? L10n.tr("flow.listener_wait_connection.title")
                    : L10n.tr("flow.udp_listener_wait_connection.title"),
                description: viewModel.transportMode == .webRtc
                    ? L10n.tr("flow.listener_wait_connection.description")
                    : L10n.tr("flow.udp_listener_wait_connection.description")
            ) {
                if viewModel.transportMode == .webRtc {
                    VerificationCodeBlock(code: viewModel.verificationCode)
                }
                InfoCallout(
                    message: viewModel.transportMode == .webRtc
                        ? L10n.tr("flow.listener_wait_connection.hint")
                        : L10n.tr("flow.udp_listener_wait_connection.hint"),
                    tint: AppTheme.accent,
                    iconName: "bolt.horizontal.circle.fill"
                )
                ExpiryStatusView(expiresAtUnixMs: viewModel.payloadExpiresAtUnixMs)
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
        switch viewModel.setupStep {
        case .entry:
            return L10n.tr("status.next_action_entry")
        case .senderShowInit:
            return L10n.tr("status.next_action_show_init")
        case .senderVerifyCode:
            return L10n.tr("status.next_action_verify")
        case .listenerInput:
            return viewModel.transportMode == .webRtc
                ? L10n.tr("status.next_action_scan_init")
                : L10n.tr("status.next_action_udp_scan_init")
        case .listenerShowConfirm:
            return L10n.tr("status.next_action_show_confirm")
        case .listenerWaitForConnection:
            return viewModel.transportMode == .webRtc
                ? L10n.tr("status.next_action_wait_connection_code")
                : L10n.tr("status.next_action_udp_wait_connection_code")
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
        case listenerInput
        case confirmPayload

        var id: String { rawValue }
    }
}

private enum AppTheme {
    static let backgroundGradient = LinearGradient(
        colors: [
            Color(red: 0.94, green: 0.97, blue: 1.00),
            Color(red: 1.00, green: 0.96, blue: 0.92)
        ],
        startPoint: .topLeading,
        endPoint: .bottomTrailing
    )
    static let cardBackground = Color.white.opacity(0.97)
    static let panelBackground = Color(red: 0.95, green: 0.97, blue: 0.99)
    static let primaryText = Color(red: 0.07, green: 0.16, blue: 0.25)
    static let secondaryText = Color(red: 0.34, green: 0.41, blue: 0.49)
    static let border = Color(red: 0.78, green: 0.84, blue: 0.90)
    static let accent = Color(red: 0.03, green: 0.41, blue: 0.57)
    static let accentSoft = Color(red: 0.86, green: 0.93, blue: 0.98)
    static let danger = Color(red: 0.74, green: 0.18, blue: 0.22)
    static let success = Color(red: 0.10, green: 0.50, blue: 0.20)
    static let warning = Color(red: 0.85, green: 0.47, blue: 0.08)
    static let shadow = Color.black.opacity(0.08)
}

private struct StepCard<Content: View>: View {
    let number: Int
    let title: String
    let description: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 10) {
                Text("\(number)")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(AppTheme.accent)
                    .frame(width: 28, height: 28)
                    .background(
                        RoundedRectangle(cornerRadius: 9, style: .continuous)
                            .fill(AppTheme.accentSoft)
                    )
                Text(title)
                    .font(.title3.weight(.bold))
                    .foregroundStyle(AppTheme.primaryText)
            }

            Text(description)
                .font(.body)
                .foregroundStyle(AppTheme.secondaryText)
                .fixedSize(horizontal: false, vertical: true)

            content
        }
        .padding(18)
        .appCardBackground()
    }
}

private struct PanelBlock<Content: View>: View {
    let title: String
    let systemImage: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Label(title, systemImage: systemImage)
                .font(.caption.weight(.semibold))
                .foregroundStyle(AppTheme.secondaryText)

            VStack(alignment: .leading, spacing: 8) {
                content
            }
        }
        .padding(14)
        .appPanelBackground()
    }
}

private struct StateBadge: View {
    let title: String
    let systemImage: String
    let tint: Color

    var body: some View {
        Label(title, systemImage: systemImage)
            .font(.caption.weight(.semibold))
            .foregroundStyle(tint)
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(
                Capsule(style: .continuous)
                    .fill(tint.opacity(0.12))
            )
            .overlay(
                Capsule(style: .continuous)
                    .stroke(tint.opacity(0.24), lineWidth: 1)
            )
    }
}

private struct InfoCallout: View {
    let title: String?
    let message: String
    let tint: Color
    let iconName: String

    init(title: String? = nil, message: String, tint: Color, iconName: String) {
        self.title = title
        self.message = message
        self.tint = tint
        self.iconName = iconName
    }

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: iconName)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(tint)
                .frame(width: 18, height: 18)

            VStack(alignment: .leading, spacing: 4) {
                if let title, !title.isEmpty {
                    Text(title)
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(tint)
                }
                Text(message)
                    .font(.footnote)
                    .foregroundStyle(AppTheme.primaryText)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(12)
        .background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(tint.opacity(0.10))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(tint.opacity(0.16), lineWidth: 1)
        )
    }
}

private struct TransportModeSelector: View {
    @Binding var selection: TransportMode

    var body: some View {
        HStack(spacing: 10) {
            ForEach(TransportMode.allCases) { mode in
                Button {
                    selection = mode
                } label: {
                    Text(mode.readableLabel)
                        .font(.subheadline.weight(.semibold))
                        .foregroundStyle(selection == mode ? Color.white : AppTheme.primaryText)
                        .frame(maxWidth: .infinity, minHeight: 48)
                        .background(
                            RoundedRectangle(cornerRadius: 14, style: .continuous)
                                .fill(selection == mode ? AppTheme.accent : AppTheme.panelBackground)
                        )
                        .overlay(
                            RoundedRectangle(cornerRadius: 14, style: .continuous)
                                .stroke(selection == mode ? AppTheme.accent : AppTheme.border, lineWidth: 1)
                        )
                }
                .buttonStyle(.plain)
            }
        }
    }
}

private struct LatencyPresetSelector: View {
    let selection: PlaybackLatencyPreset
    let onSelect: (PlaybackLatencyPreset) -> Void

    var body: some View {
        Menu {
            ForEach(PlaybackLatencyPreset.allCases) { preset in
                Button {
                    onSelect(preset)
                } label: {
                    if preset == selection {
                        Label(preset.localizedLabel, systemImage: "checkmark")
                    } else {
                        Text(preset.localizedLabel)
                    }
                }
            }
        } label: {
            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(selection.localizedLabel)
                        .font(.title3.weight(.bold))
                        .foregroundStyle(AppTheme.accent)
                    Text(L10n.tr("receiver_latency_title"))
                        .font(.caption)
                        .foregroundStyle(AppTheme.secondaryText)
                }

                Spacer(minLength: 12)

                Image(systemName: "chevron.up.chevron.down")
                    .font(.caption.weight(.bold))
                    .foregroundStyle(AppTheme.secondaryText)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 12)
            .frame(maxWidth: .infinity)
            .appPanelBackground()
        }
    }
}

private struct VerificationCodeBlock: View {
    let code: String

    var body: some View {
        if code.isEmpty {
            EmptyView()
        } else {
            VStack(alignment: .leading, spacing: 8) {
                Text(L10n.tr("flow.verification.code_label"))
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(AppTheme.secondaryText)
                Text(code)
                    .font(.system(size: 34, weight: .black, design: .rounded))
                    .foregroundStyle(AppTheme.primaryText)
                    .tracking(1.2)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(16)
            .appPanelBackground()
        }
    }
}

private struct PayloadInputPanel: View {
    let title: String
    let placeholder: String
    @Binding var text: String

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(.caption.weight(.semibold))
                .foregroundStyle(AppTheme.secondaryText)

            ZStack(alignment: .topLeading) {
                if text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text(placeholder)
                        .font(.footnote.monospaced())
                        .foregroundStyle(AppTheme.secondaryText)
                        .padding(.horizontal, 14)
                        .padding(.vertical, 14)
                }

                TextEditor(text: $text)
                    .font(.footnote.monospaced())
                    .foregroundColor(AppTheme.primaryText)
                    .frame(minHeight: 128)
                    .padding(6)
                    .background(Color.clear)
            }
            .appPanelBackground()

            HStack {
                Spacer()
                Text(L10n.tr("common.char_count_format", text.count))
                    .font(.caption)
                    .foregroundStyle(AppTheme.secondaryText)
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
        VStack(alignment: .leading, spacing: 12) {
            Text(payloadTitle)
                .font(.caption.weight(.semibold))
                .foregroundStyle(AppTheme.secondaryText)

            ScrollView {
                Text(payloadValue)
                    .font(.footnote.monospaced())
                    .foregroundStyle(AppTheme.primaryText)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled)
            }
            .frame(minHeight: 100, maxHeight: 148)
            .padding(12)
            .appPanelBackground()

            HStack {
                Button(L10n.tr("action.copy_payload"), action: onCopyPayload)
                    .font(.caption.weight(.semibold))
                    .foregroundStyle(AppTheme.accent)
                Spacer()
                Text(L10n.tr("common.char_count_format", payloadValue.count))
                    .font(.caption)
                    .foregroundStyle(AppTheme.secondaryText)
            }

            ResponsiveQRCode(payload: payloadValue, accessibilityDescription: payloadQrDescription)
                .padding(14)
                .frame(maxWidth: .infinity)
                .appPanelBackground()
        }
    }
}

private struct ExpiryStatusView: View {
    let expiresAtUnixMs: Int64

    var body: some View {
        if expiresAtUnixMs > 0 {
            TimelineView(.periodic(from: Date(), by: 1)) { timeline in
                let remainingSeconds = max(
                    Int((expiresAtUnixMs - Int64(timeline.date.timeIntervalSince1970 * 1000)) / 1000),
                    0
                )
                if remainingSeconds > 0 {
                    Text(L10n.tr("status.expiry_remaining_format", remainingSeconds))
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(AppTheme.secondaryText)
                } else {
                    Text(L10n.tr("status.expiry_expired"))
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(AppTheme.danger)
                }
            }
        }
    }
}

private struct PrimaryActionButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(maxWidth: .infinity, minHeight: 48)
            .padding(.vertical, 10)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.white)
            .background(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(AppTheme.accent)
            )
            .shadow(color: AppTheme.accent.opacity(0.18), radius: 14, x: 0, y: 6)
            .opacity(configuration.isPressed ? 0.9 : 1)
            .scaleEffect(configuration.isPressed ? 0.99 : 1)
    }
}

private struct SecondaryActionButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(maxWidth: .infinity, minHeight: 48)
            .padding(.vertical, 10)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(AppTheme.accent)
            .background(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(Color.white.opacity(0.96))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .stroke(AppTheme.accent.opacity(0.28), lineWidth: 1)
            )
            .opacity(configuration.isPressed ? 0.9 : 1)
            .scaleEffect(configuration.isPressed ? 0.99 : 1)
    }
}

private extension View {
    func appCardBackground() -> some View {
        background(
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .fill(AppTheme.cardBackground)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 20, style: .continuous)
                .stroke(AppTheme.border.opacity(0.55), lineWidth: 1)
        )
        .shadow(color: AppTheme.shadow, radius: 18, x: 0, y: 8)
    }

    func appPanelBackground() -> some View {
        background(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .fill(AppTheme.panelBackground)
        )
        .overlay(
            RoundedRectangle(cornerRadius: 14, style: .continuous)
                .stroke(AppTheme.border.opacity(0.72), lineWidth: 1)
        )
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
            return AppTheme.success
        case .failed:
            return AppTheme.danger
        case .capturing, .connecting:
            return AppTheme.warning
        default:
            return AppTheme.secondaryText
        }
    }

    var symbolName: String {
        switch self {
        case .idle:
            return "pause.circle.fill"
        case .capturing:
            return "waveform.circle.fill"
        case .connecting:
            return "point.3.connected.trianglepath.dotted"
        case .streaming:
            return "play.circle.fill"
        case .interrupted:
            return "pause.circle.fill"
        case .failed:
            return "xmark.octagon.fill"
        case .ended:
            return "stop.circle.fill"
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
        case "peer_unreachable":
            return L10n.tr("status.hint_peer_unreachable")
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

private extension TransportMode {
    var readableLabel: String {
        switch self {
        case .webRtc:
            return L10n.tr("transport_mode_webrtc")
        case .udpOpus:
            return L10n.tr("transport_mode_udp")
        }
    }

    var descriptionText: String {
        switch self {
        case .webRtc:
            return L10n.tr("transport_mode_webrtc_note")
        case .udpOpus:
            return L10n.tr("transport_mode_udp_note")
        }
    }
}

private extension AudioStreamSource {
    var readableLabel: String {
        switch self {
        case .none:
            return L10n.tr("audio_diagnostics.source_none")
        case .webRtcReceive:
            return L10n.tr("audio_diagnostics.source_webrtc")
        case .udpOpusReceive:
            return L10n.tr("audio_diagnostics.source_udp")
        }
    }
}

private func recommendedQrDisplaySize(for payloadLength: Int) -> CGFloat {
    switch payloadLength {
    case 900...:
        return 360
    case 650...:
        return 320
    default:
        return 280
    }
}

private struct ResponsiveQRCode: View {
    let payload: String
    let accessibilityDescription: String

    private var preferredSize: CGFloat {
        recommendedQrDisplaySize(for: payload.count)
    }

    var body: some View {
        GeometryReader { geometry in
            let qrSize = min(geometry.size.width, preferredSize)
            HStack {
                Spacer()
                QRCodeImage(payload: payload, preferredSize: qrSize)
                    .frame(width: qrSize, height: qrSize)
                    .accessibilityLabel(Text(accessibilityDescription))
                Spacer()
            }
        }
        .frame(height: preferredSize)
    }
}

private struct QRCodeImage: View {
    private let payload: String
    private let preferredSize: CGFloat
    private let context = CIContext()
    private let filter = CIFilter.qrCodeGenerator()

    init(payload: String, preferredSize: CGFloat) {
        self.payload = payload
        self.preferredSize = preferredSize
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
        filter.setValue("L", forKey: "inputCorrectionLevel")
        guard let outputImage = filter.outputImage else {
            return nil
        }

        let moduleWidth = max(outputImage.extent.width, 1)
        let scale = max(1, Int(ceil(preferredSize / moduleWidth)))
        let transformed = outputImage.transformed(by: CGAffineTransform(scaleX: CGFloat(scale), y: CGFloat(scale)))
        guard let cgImage = context.createCGImage(transformed, from: transformed.extent) else {
            return nil
        }

        return UIImage(cgImage: cgImage)
    }
}
