namespace P2PAudio.Windows.App.Services;

public interface ILoopbackAudioSender : IDisposable
{
    bool IsRunning { get; }
    event EventHandler<LoopbackAudioDiagnostics>? DiagnosticsChanged;
    void Start();
    void Stop();
}
