import AVFoundation
import SwiftUI

@main
struct P2PAudioApp: App {
    @Environment(\.scenePhase) private var scenePhase

    init() {
        do {
            try AudioPlaybackSession.shared.activateForPlayback()
        } catch {
            FileHandle.standardError.write(Data("P2PAudio audio session setup failed: \(error)\n".utf8))
        }
    }

    var body: some Scene {
        WindowGroup {
            ContentView()
                .onAppear {
                    do {
                        try AudioPlaybackSession.shared.activateForPlayback()
                    } catch {
                        FileHandle.standardError.write(
                            Data("P2PAudio audio session activation on appear failed: \(error)\n".utf8)
                        )
                    }
                }
                .onChange(of: scenePhase) { newValue in
                    guard newValue == .active else { return }
                    do {
                        try AudioPlaybackSession.shared.activateForPlayback()
                    } catch {
                        FileHandle.standardError.write(
                            Data("P2PAudio audio session activation on active failed: \(error)\n".utf8)
                        )
                    }
                }
        }
    }
}
