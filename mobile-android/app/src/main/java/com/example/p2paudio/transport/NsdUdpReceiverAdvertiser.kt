package com.example.p2paudio.transport

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Build
import com.example.p2paudio.logging.AppLogger
import java.util.Locale
import java.util.concurrent.atomic.AtomicReference

class NsdUdpReceiverAdvertiser(
    context: Context
) {
    private val nsdManager = context.getSystemService(NsdManager::class.java)
    private val registeredServiceName = AtomicReference<String?>(null)
    private var registrationListener: NsdManager.RegistrationListener? = null

    fun register(
        port: Int,
        onRegistered: (String) -> Unit,
        onFailure: (Throwable) -> Unit
    ) {
        unregister()

        val serviceInfo = NsdServiceInfo().apply {
            serviceName = buildServiceName()
            serviceType = SERVICE_TYPE
            this.port = port
            setAttribute("codec", "opus")
            setAttribute("ptime", "10")
            setAttribute("channels", "1")
            setAttribute("sampleRate", "48000")
            setAttribute("role", "listener")
        }

        val listener = object : NsdManager.RegistrationListener {
            override fun onRegistrationFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
                val failure = IllegalStateException("mDNS registration failed: $errorCode")
                AppLogger.e(
                    "NsdUdpReceiverAdvertiser",
                    "register_failed",
                    "Failed to register UDP listener service",
                    context = mapOf("errorCode" to errorCode),
                    throwable = failure
                )
                registrationListener = null
                onFailure(failure)
            }

            override fun onUnregistrationFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
                AppLogger.w(
                    "NsdUdpReceiverAdvertiser",
                    "unregister_failed",
                    "Failed to unregister UDP listener service",
                    context = mapOf("errorCode" to errorCode)
                )
            }

            override fun onServiceRegistered(serviceInfo: NsdServiceInfo) {
                val actualName = serviceInfo.serviceName
                registeredServiceName.set(actualName)
                AppLogger.i(
                    "NsdUdpReceiverAdvertiser",
                    "register_success",
                    "Registered UDP listener service",
                    context = mapOf(
                        "serviceName" to actualName,
                        "port" to serviceInfo.port
                    )
                )
                onRegistered(actualName)
            }

            override fun onServiceUnregistered(serviceInfo: NsdServiceInfo) {
                AppLogger.i(
                    "NsdUdpReceiverAdvertiser",
                    "unregister_success",
                    "Unregistered UDP listener service",
                    context = mapOf("serviceName" to serviceInfo.serviceName)
                )
                registeredServiceName.set(null)
            }
        }

        registrationListener = listener
        nsdManager.registerService(serviceInfo, NsdManager.PROTOCOL_DNS_SD, listener)
    }

    fun unregister() {
        val listener = registrationListener ?: return
        registrationListener = null
        runCatching {
            nsdManager.unregisterService(listener)
        }.onFailure {
            AppLogger.w(
                "NsdUdpReceiverAdvertiser",
                "unregister_exception",
                "Exception while unregistering UDP listener service",
                context = mapOf("reason" to (it.message ?: "unknown"))
            )
        }
    }

    fun currentServiceName(): String? = registeredServiceName.get()

    private fun buildServiceName(): String {
        val device = (Build.MODEL ?: "android")
            .lowercase(Locale.US)
            .replace(Regex("[^a-z0-9]+"), "-")
            .trim('-')
            .ifBlank { "android" }
        return "p2paudio-$device-${System.currentTimeMillis() % 10_000}"
    }

    companion object {
        const val SERVICE_TYPE = "_p2paudio-udp._udp."
    }
}
