import AVFoundation
import SwiftUI

struct QrScannerView: UIViewControllerRepresentable {
    let onScanned: (String) -> Void
    let onCancel: () -> Void

    func makeUIViewController(context: Context) -> ScannerViewController {
        let controller = ScannerViewController()
        controller.onScanned = onScanned
        controller.onCancel = onCancel
        return controller
    }

    func updateUIViewController(_ uiViewController: ScannerViewController, context: Context) {}

    final class ScannerViewController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
        var onScanned: ((String) -> Void)?
        var onCancel: (() -> Void)?

        private let session = AVCaptureSession()
        private var previewLayer: AVCaptureVideoPreviewLayer?
        private var hasScanned = false

        override func viewDidLoad() {
            super.viewDidLoad()
            view.backgroundColor = .black
            setupCloseButton()
            configureCaptureSession()
        }

        override func viewDidAppear(_ animated: Bool) {
            super.viewDidAppear(animated)
            if !session.isRunning {
                session.startRunning()
            }
        }

        override func viewWillDisappear(_ animated: Bool) {
            super.viewWillDisappear(animated)
            if session.isRunning {
                session.stopRunning()
            }
        }

        override func viewDidLayoutSubviews() {
            super.viewDidLayoutSubviews()
            previewLayer?.frame = view.bounds
        }

        private func setupCloseButton() {
            let button = UIButton(type: .system)
            button.setTitle(L10n.tr("action.close"), for: .normal)
            button.tintColor = .white
            button.addTarget(self, action: #selector(closeTapped), for: .touchUpInside)
            button.translatesAutoresizingMaskIntoConstraints = false
            view.addSubview(button)

            NSLayoutConstraint.activate([
                button.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 12),
                button.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -16)
            ])
        }

        private func configureCaptureSession() {
            guard let device = AVCaptureDevice.default(for: .video) else {
                onCancel?()
                return
            }
            guard let input = try? AVCaptureDeviceInput(device: device) else {
                onCancel?()
                return
            }
            let output = AVCaptureMetadataOutput()

            guard session.canAddInput(input), session.canAddOutput(output) else {
                onCancel?()
                return
            }

            session.addInput(input)
            session.addOutput(output)
            output.setMetadataObjectsDelegate(self, queue: DispatchQueue.main)
            output.metadataObjectTypes = [.qr]

            let preview = AVCaptureVideoPreviewLayer(session: session)
            preview.videoGravity = .resizeAspectFill
            preview.frame = view.bounds
            view.layer.insertSublayer(preview, at: 0)
            previewLayer = preview
        }

        @objc
        private func closeTapped() {
            onCancel?()
        }

        func metadataOutput(
            _ output: AVCaptureMetadataOutput,
            didOutput metadataObjects: [AVMetadataObject],
            from connection: AVCaptureConnection
        ) {
            guard !hasScanned else {
                return
            }
            guard let object = metadataObjects.first as? AVMetadataMachineReadableCodeObject,
                  object.type == .qr,
                  let value = object.stringValue,
                  !value.isEmpty else {
                return
            }

            hasScanned = true
            onScanned?(value)
        }
    }
}
