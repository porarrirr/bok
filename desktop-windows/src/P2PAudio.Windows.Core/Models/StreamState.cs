namespace P2PAudio.Windows.Core.Models;

public enum StreamState
{
    Idle,
    Capturing,
    Connecting,
    Streaming,
    Interrupted,
    Failed,
    Ended
}
