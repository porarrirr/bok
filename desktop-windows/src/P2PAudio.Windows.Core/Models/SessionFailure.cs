namespace P2PAudio.Windows.Core.Models;

public sealed class SessionFailure : Exception
{
    public SessionFailure(FailureCode code, string message) : base(message)
    {
        Code = code;
    }

    public FailureCode Code { get; }
}
