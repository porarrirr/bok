using P2PAudio.Windows.Core.Models;

namespace P2PAudio.Windows.App.Services;

public interface IAudioTransportBackend
{
    TransportMode Mode { get; }
    ConnectionDiagnostics GetDiagnostics();
    BridgeBackendHealth GetBackendHealth();
    bool IsNativeBackend { get; }
    void Close();
}
