import Foundation
import Darwin

enum NetworkPathClassifier {
    static func classify(fromCandidateSdp candidateSdp: String) -> NetworkPathType {
        guard let address = extractCandidateAddress(candidateSdp) else {
            return classifyFromLocalInterfaces()
        }
        return classifyAddress(address) ?? classifyFromLocalInterfaces()
    }

    static func classifyFromLocalInterfaces() -> NetworkPathType {
        let interfaces = listInterfaceNames()
        if interfaces.contains(where: isUsbLike) {
            return .usbTether
        }
        if interfaces.contains(where: isWifiLike) {
            return .wifiLan
        }
        return .unknown
    }

    private static func classifyAddress(_ address: String) -> NetworkPathType? {
        let interfaceByAddress = listInterfaceAddresses()
        guard let iface = interfaceByAddress[address] else {
            return nil
        }
        if isUsbLike(iface) {
            return .usbTether
        }
        if isWifiLike(iface) {
            return .wifiLan
        }
        return .unknown
    }

    private static func extractCandidateAddress(_ candidateSdp: String) -> String? {
        let parts = candidateSdp
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .split(separator: " ")
        guard parts.count >= 6 else {
            return nil
        }
        return String(parts[4])
    }

    private static func isUsbLike(_ name: String) -> Bool {
        let normalized = name.lowercased()
        return normalized.contains("en") && normalized.contains("usb") ||
            normalized.contains("bridge") ||
            normalized.contains("ipheth") ||
            normalized.contains("rndis") ||
            normalized.contains("usb")
    }

    private static func isWifiLike(_ name: String) -> Bool {
        let normalized = name.lowercased()
        return normalized == "en0" || normalized.contains("wifi") || normalized.contains("wlan")
    }

    private static func listInterfaceNames() -> [String] {
        Array(Set(listInterfaceAddresses().values)).sorted()
    }

    private static func listInterfaceAddresses() -> [String: String] {
        var results: [String: String] = [:]
        var ifaddrPointer: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddrPointer) == 0, let start = ifaddrPointer else {
            return results
        }
        defer { freeifaddrs(ifaddrPointer) }

        var pointer = start
        while true {
            let interface = pointer.pointee
            let flags = Int32(interface.ifa_flags)
            let isUp = (flags & IFF_UP) != 0
            let isLoopback = (flags & IFF_LOOPBACK) != 0
            if isUp, !isLoopback, let sa = interface.ifa_addr {
                let family = sa.pointee.sa_family
                if family == UInt8(AF_INET) || family == UInt8(AF_INET6) {
                    var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
                    let length = socklen_t(sa.pointee.sa_len)
                    let result = getnameinfo(
                        sa,
                        length,
                        &host,
                        socklen_t(host.count),
                        nil,
                        0,
                        NI_NUMERICHOST
                    )
                    if result == 0 {
                        let address = String(cString: host)
                        let name = String(cString: interface.ifa_name)
                        results[address] = name
                    }
                }
            }

            guard let next = interface.ifa_next else {
                break
            }
            pointer = next
        }

        return results
    }
}
