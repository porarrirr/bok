import XCTest
@testable import P2PAudio

final class PcmPlayerQueuePolicyTests: XCTestCase {
    func testTrimOverflowDropsOldestPendingFramesAndAdvancesExpectedSequence() {
        var pendingSequenceNumbers = [10, 11, 12, 13]

        let result = trimOverflowFramesForRealtimePlayback(
            pendingSequenceNumbers: &pendingSequenceNumbers,
            scheduledFrames: 2,
            maxQueueFrames: 4,
            expectedSequence: 10
        )

        XCTAssertEqual(result.droppedFrameCount, 2)
        XCTAssertEqual(result.nextExpectedSequence, 12)
        XCTAssertEqual(pendingSequenceNumbers, [12, 13])
    }

    func testTrimOverflowKeepsExpectedSequenceWhenDroppingOlderFrames() {
        var pendingSequenceNumbers = [4, 5, 6]

        let result = trimOverflowFramesForRealtimePlayback(
            pendingSequenceNumbers: &pendingSequenceNumbers,
            scheduledFrames: 1,
            maxQueueFrames: 3,
            expectedSequence: 8
        )

        XCTAssertEqual(result.droppedFrameCount, 1)
        XCTAssertEqual(result.nextExpectedSequence, 8)
        XCTAssertEqual(pendingSequenceNumbers, [5, 6])
    }

    func testPlaybackThresholdsUseConfiguredPrebufferFramesOnIOS() {
        XCTAssertEqual(
            playbackStartThresholdFrames(startupPrebufferFrames: 4),
            1
        )
        XCTAssertEqual(
            playbackResumeThresholdFrames(steadyPrebufferFrames: 4),
            1
        )
    }
}
