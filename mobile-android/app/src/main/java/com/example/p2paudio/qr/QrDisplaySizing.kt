package com.example.p2paudio.qr

object QrDisplaySizing {
    private const val MEDIUM_PAYLOAD_THRESHOLD = 650
    private const val DENSE_PAYLOAD_THRESHOLD = 900

    const val DEFAULT_DISPLAY_SIZE_DP = 280
    const val DEFAULT_BITMAP_SIZE_PX = 896

    fun recommendedDisplaySizeDp(payloadLength: Int): Int = when {
        payloadLength >= DENSE_PAYLOAD_THRESHOLD -> 360
        payloadLength >= MEDIUM_PAYLOAD_THRESHOLD -> 320
        else -> DEFAULT_DISPLAY_SIZE_DP
    }

    fun recommendedBitmapSizePx(payloadLength: Int): Int = when {
        payloadLength >= DENSE_PAYLOAD_THRESHOLD -> 1_280
        payloadLength >= MEDIUM_PAYLOAD_THRESHOLD -> 1_024
        else -> DEFAULT_BITMAP_SIZE_PX
    }
}
