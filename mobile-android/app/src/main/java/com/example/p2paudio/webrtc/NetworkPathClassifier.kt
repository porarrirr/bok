package com.example.p2paudio.webrtc

import com.example.p2paudio.model.NetworkPathType
import java.net.NetworkInterface

internal object NetworkPathClassifier {

    fun classifyFromCandidateSdp(candidateSdp: String): NetworkPathType {
        val candidateAddress = extractCandidateAddress(candidateSdp) ?: return classifyFromLocalInterfaces()
        return classifyFromAddress(candidateAddress) ?: classifyFromLocalInterfaces()
    }

    fun classifyFromLocalInterfaces(): NetworkPathType {
        val interfaces = runCatching { NetworkInterface.getNetworkInterfaces()?.toList().orEmpty() }
            .getOrDefault(emptyList())
            .filter { it.isUp && !it.isLoopback }

        val hasUsbLike = interfaces.any { isUsbLikeInterface(it.name) || isUsbLikeInterface(it.displayName) }
        if (hasUsbLike) {
            return NetworkPathType.USB_TETHER
        }

        val hasWifiLike = interfaces.any { isWifiLikeInterface(it.name) || isWifiLikeInterface(it.displayName) }
        if (hasWifiLike) {
            return NetworkPathType.WIFI_LAN
        }

        return NetworkPathType.UNKNOWN
    }

    private fun classifyFromAddress(address: String): NetworkPathType? {
        val interfaces = runCatching { NetworkInterface.getNetworkInterfaces()?.toList().orEmpty() }
            .getOrDefault(emptyList())
            .filter { it.isUp && !it.isLoopback }

        val matched = interfaces.firstOrNull { iface ->
            iface.inetAddresses.toList().any { it.hostAddress == address }
        } ?: return null

        if (isUsbLikeInterface(matched.name) || isUsbLikeInterface(matched.displayName)) {
            return NetworkPathType.USB_TETHER
        }
        if (isWifiLikeInterface(matched.name) || isWifiLikeInterface(matched.displayName)) {
            return NetworkPathType.WIFI_LAN
        }
        return NetworkPathType.UNKNOWN
    }

    private fun isUsbLikeInterface(name: String?): Boolean {
        val normalized = name.orEmpty().lowercase()
        if (normalized.isBlank()) return false
        return normalized.contains("rndis") ||
            normalized.contains("usb") ||
            normalized.contains("tether") ||
            normalized.contains("eth")
    }

    private fun isWifiLikeInterface(name: String?): Boolean {
        val normalized = name.orEmpty().lowercase()
        if (normalized.isBlank()) return false
        return normalized.contains("wlan") ||
            normalized.contains("wifi") ||
            normalized.contains("wi-fi")
    }

    private fun extractCandidateAddress(candidateSdp: String): String? {
        val parts = candidateSdp.trim().split(' ')
        if (parts.size < 6) {
            return null
        }
        return parts.getOrNull(4)
    }
}
