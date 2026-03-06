using System.Security.Cryptography;
using System.Text;

namespace P2PAudio.Windows.Core.Protocol;

public static class VerificationCode
{
    public static string FromSessionAndFingerprints(
        string sessionId,
        string senderFingerprint,
        string receiverFingerprint
    )
    {
        var source = $"{sessionId}|{senderFingerprint}|{receiverFingerprint}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var numeric =
            ((uint)digest[0] << 24) |
            ((uint)digest[1] << 16) |
            ((uint)digest[2] << 8) |
            digest[3];
        var value = numeric % 1_000_000U;
        return value.ToString("D6");
    }
}
