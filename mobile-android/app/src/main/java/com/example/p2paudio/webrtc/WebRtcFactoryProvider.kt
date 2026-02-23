package com.example.p2paudio.webrtc

import android.content.Context
import org.webrtc.DefaultVideoDecoderFactory
import org.webrtc.DefaultVideoEncoderFactory
import org.webrtc.EglBase
import org.webrtc.PeerConnectionFactory

object WebRtcFactoryProvider {
    @Volatile
    private var initialized = false

    fun create(context: Context): PeerConnectionFactory {
        if (!initialized) {
            synchronized(this) {
                if (!initialized) {
                    PeerConnectionFactory.initialize(
                        PeerConnectionFactory.InitializationOptions.builder(context)
                            .setEnableInternalTracer(false)
                            .createInitializationOptions()
                    )
                    initialized = true
                }
            }
        }

        val egl = EglBase.create()
        return PeerConnectionFactory.builder()
            .setVideoEncoderFactory(DefaultVideoEncoderFactory(egl.eglBaseContext, true, true))
            .setVideoDecoderFactory(DefaultVideoDecoderFactory(egl.eglBaseContext))
            .createPeerConnectionFactory()
    }
}
