import CoreImage.CIFilterBuiltins
import SwiftUI
import UIKit

struct ContentView: View {
    @StateObject private var viewModel = AppViewModel()
    @State private var offerInput = ""
    @State private var answerInput = ""
    @State private var scanTarget: ScanTarget?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 12) {
                Text("State: \(viewModel.streamState.rawValue)")
                Text(viewModel.statusMessage)
                if !viewModel.activeSessionId.isEmpty {
                    Text("Session: \(viewModel.activeSessionId)")
                }

                HStack {
                    Button("Start sender") {
                        viewModel.startSenderFlow()
                    }
                    Button("Stop") {
                        viewModel.endSession()
                    }
                }

                Text("Receiver")
                TextEditor(text: $offerInput)
                    .frame(height: 120)
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.gray.opacity(0.4)))
                HStack {
                    Button("Scan offer QR") {
                        scanTarget = .offer
                    }
                    Button("Create answer from offer") {
                        viewModel.createAnswer(from: offerInput)
                    }
                }

                if !viewModel.offerPayloadRaw.isEmpty {
                    Text("Sender Offer QR")
                    QRCodeImage(payload: viewModel.offerPayloadRaw)
                        .frame(width: 220, height: 220)
                }

                Text("Sender")
                TextEditor(text: $answerInput)
                    .frame(height: 120)
                    .overlay(RoundedRectangle(cornerRadius: 8).stroke(Color.gray.opacity(0.4)))
                HStack {
                    Button("Scan answer QR") {
                        scanTarget = .answer
                    }
                    Button("Apply answer") {
                        viewModel.applyAnswer(from: answerInput)
                    }
                }

                if !viewModel.answerPayloadRaw.isEmpty {
                    Text("Receiver Answer QR")
                    QRCodeImage(payload: viewModel.answerPayloadRaw)
                        .frame(width: 220, height: 220)
                }

                Text("iOS sender captures ReplayKit app audio only (not ringtones/system sounds).")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
            .padding(16)
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

    private enum ScanTarget: String, Identifiable {
        case offer
        case answer

        var id: String { rawValue }
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
