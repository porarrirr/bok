using P2PAudio.Windows.Core.Protocol;

namespace P2PAudio.Windows.Core.Tests;

public sealed class VerificationCodeTests
{
    [Fact]
    public void GeneratesSixDigitCode()
    {
        var code = VerificationCode.FromSessionAndFingerprints(
            sessionId: "session-a",
            senderFingerprint: "sender-fp",
            receiverFingerprint: "receiver-fp"
        );

        Assert.Equal(6, code.Length);
        Assert.Matches("^[0-9]{6}$", code);
    }

    [Fact]
    public void MatchesKnownCrossPlatformVector()
    {
        var code = VerificationCode.FromSessionAndFingerprints(
            sessionId: "session-a",
            senderFingerprint: "sender-fp",
            receiverFingerprint: "receiver-fp"
        );

        Assert.Equal("912851", code);
    }
}
