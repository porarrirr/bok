import CoreImage.CIFilterBuiltins
import SwiftUI
import UIKit

struct ContentView: View {
    @StateObject private var viewModel = AppViewModel()
    @State private var offerInput = ""
    @State private var answerInput = ""
    @State private var scanTarget: ScanTarget?
    @State private var transientMessage: String?

    var body: some View {
        ZStack(alignment: .top) {
            LinearGradient(
                colors: [
                    Color(red: 0.92, green: 0.97, blue: 0.98),
                    Color(red: 0.97, green: 0.94, blue: 0.90)
                ],
                startPoint: .topLeading,
                endPoint: .bottomTrailing
            )
            .ignoresSafeArea()

            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    headerBlock
                    statusCard
                    actionRow

                    if let transientMessage {
                        Text(transientMessage)
                            .font(.footnote.weight(.semibold))
                            .foregroundStyle(Color(red: 0.03, green: 0.35, blue: 0.47))
                            .padding(.horizontal, 4)
                            .transition(.opacity.combined(with: .move(edge: .top)))
                    }

                    PayloadFlowCard(
                        title: "Receiver Flow",
                        subtitle: "Scan or paste sender offer, then return generated answer.",
                        inputTitle: "Offer payload",
                        inputText: $offerInput,
                        scanButtonTitle: "Scan Offer QR",
                        submitButtonTitle: "Create Answer",
                        onScan: { scanTarget = .offer },
                        onSubmit: {
                            viewModel.createAnswer(from: offerInput.trimmingCharacters(in: .whitespacesAndNewlines))
                        },
                        payloadTitle: "Generated answer payload",
                        payloadValue: viewModel.answerPayloadRaw,
                        payloadQrDescription: "Answer QR",
                        onCopyPayload: {
                            UIPasteboard.general.string = viewModel.answerPayloadRaw
                            showTransientMessage("Answer payload copied.")
                        }
                    )

                    PayloadFlowCard(
                        title: "Sender Flow",
                        subtitle: "Start sender to generate offer, then import receiver answer.",
                        inputTitle: "Answer payload",
                        inputText: $answerInput,
                        scanButtonTitle: "Scan Answer QR",
                        submitButtonTitle: "Apply Answer",
                        onScan: { scanTarget = .answer },
                        onSubmit: {
                            viewModel.applyAnswer(from: answerInput.trimmingCharacters(in: .whitespacesAndNewlines))
                        },
                        payloadTitle: "Generated offer payload",
                        payloadValue: viewModel.offerPayloadRaw,
                        payloadQrDescription: "Offer QR",
                        onCopyPayload: {
                            UIPasteboard.general.string = viewModel.offerPayloadRaw
                            showTransientMessage("Offer payload copied.")
                        }
                    )

                    Text("iOS sender captures ReplayKit app audio only (not ringtones/system sounds).")
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
                    case .offer:
                        offerInput = payload
                        viewModel.createAnswer(from: payload)
                    case .answer:
                        answerInput = payload
                        viewModel.applyAnswer(from: payload)
                    }
                    scanTarget = nil
                },
                onCancel: {
                    scanTarget = nil
                }
            )
            .ignoresSafeArea()
        }
    }

    private var headerBlock: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("P2P Audio Bridge")
                .font(.system(size: 30, weight: .bold, design: .rounded))
                .foregroundStyle(Color(red: 0.07, green: 0.17, blue: 0.25))
            Text("Pair devices locally via QR or payload text.")
                .font(.subheadline)
                .foregroundStyle(Color(red: 0.22, green: 0.34, blue: 0.42))
        }
    }

    private var statusCard: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Connection Status")
                .font(.headline)
            Text(viewModel.streamState.readableLabel)
                .font(.subheadline.weight(.semibold))
                .foregroundStyle(viewModel.streamState.themeColor)
            Text(viewModel.statusMessage)
                .font(.subheadline)
            if !viewModel.activeSessionId.isEmpty {
                Text("Session ID")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Text(viewModel.activeSessionId)
                    .font(.caption.monospaced())
                    .lineLimit(1)
                    .textSelection(.enabled)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(14)
        .background(Color.white.opacity(0.92), in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        .overlay(
            RoundedRectangle(cornerRadius: 16, style: .continuous)
                .stroke(Color.black.opacity(0.06), lineWidth: 1)
        )
    }

    private var actionRow: some View {
        HStack(spacing: 10) {
            Button("Start Sender") {
                viewModel.startSenderFlow()
            }
            .buttonStyle(PrimaryActionButtonStyle())

            Button("Stop Session") {
                viewModel.endSession()
            }
            .buttonStyle(SecondaryActionButtonStyle())
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
        case offer
        case answer

        var id: String { rawValue }
    }
}

private struct PayloadFlowCard: View {
    let title: String
    let subtitle: String
    let inputTitle: String
    @Binding var inputText: String
    let scanButtonTitle: String
    let submitButtonTitle: String
    let onScan: () -> Void
    let onSubmit: () -> Void
    let payloadTitle: String
    let payloadValue: String
    let payloadQrDescription: String
    let onCopyPayload: () -> Void

    private var trimmedInput: String {
        inputText.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title)
                .font(.title3.weight(.semibold))
                .foregroundStyle(Color(red: 0.08, green: 0.21, blue: 0.28))
            Text(subtitle)
                .font(.subheadline)
                .foregroundStyle(Color(red: 0.30, green: 0.39, blue: 0.45))

            Text(inputTitle)
                .font(.caption.weight(.semibold))
                .foregroundStyle(.secondary)

            TextEditor(text: $inputText)
                .frame(minHeight: 110)
                .padding(8)
                .background(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .fill(Color.white)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .stroke(Color.black.opacity(0.15), lineWidth: 1)
                )

            HStack(spacing: 10) {
                Button(scanButtonTitle, action: onScan)
                    .buttonStyle(SecondaryActionButtonStyle())
                Button(submitButtonTitle, action: onSubmit)
                    .buttonStyle(PrimaryActionButtonStyle())
                    .disabled(trimmedInput.isEmpty)
                    .opacity(trimmedInput.isEmpty ? 0.6 : 1)
            }

            if !payloadValue.isEmpty {
                Divider().padding(.top, 4)
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
                    Button("Copy payload", action: onCopyPayload)
                        .font(.caption.weight(.semibold))
                    Spacer()
                    Text("\(payloadValue.count) chars")
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

private struct PrimaryActionButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .frame(maxWidth: .infinity)
            .padding(.vertical, 12)
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
            .frame(maxWidth: .infinity)
            .padding(.vertical, 12)
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
        case .idle: return "Idle"
        case .capturing: return "Capturing"
        case .connecting: return "Connecting"
        case .streaming: return "Streaming"
        case .interrupted: return "Interrupted"
        case .failed: return "Failed"
        case .ended: return "Ended"
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
