package com.example.p2paudio.model

data class ConnectionDiagnostics(
    val pathType: NetworkPathType = NetworkPathType.UNKNOWN,
    val localCandidatesCount: Int = 0,
    val selectedCandidatePairType: String = "",
    val failureHint: String = ""
)
