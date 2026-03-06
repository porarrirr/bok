namespace P2PAudio.Windows.Core.Models;

public sealed record ConnectionDiagnostics(
    NetworkPathType PathType = NetworkPathType.Unknown,
    int LocalCandidatesCount = 0,
    string SelectedCandidatePairType = "",
    string FailureHint = "",
    FailureCode? NormalizedFailureCode = null
);
