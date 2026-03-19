namespace P2PAudio.Windows.App.Services;

public interface ILoopbackAudioSender : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();
}
