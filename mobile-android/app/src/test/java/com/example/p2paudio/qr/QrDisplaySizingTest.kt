package com.example.p2paudio.qr

import org.junit.Assert.assertEquals
import org.junit.Test

class QrDisplaySizingTest {

    @Test
    fun `recommended display size grows with payload length`() {
        assertEquals(280, QrDisplaySizing.recommendedDisplaySizeDp(200))
        assertEquals(320, QrDisplaySizing.recommendedDisplaySizeDp(700))
        assertEquals(360, QrDisplaySizing.recommendedDisplaySizeDp(950))
    }

    @Test
    fun `recommended bitmap size grows with payload length`() {
        assertEquals(896, QrDisplaySizing.recommendedBitmapSizePx(200))
        assertEquals(1_024, QrDisplaySizing.recommendedBitmapSizePx(700))
        assertEquals(1_280, QrDisplaySizing.recommendedBitmapSizePx(950))
    }
}
